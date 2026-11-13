namespace ElectricSim.Core;

/// <summary>
/// El sistema de calor del simulador. Corre UNA vez por tick, después del
/// paso eléctrico, y hace tres cosas con cada componente:
///
///  1. CALENTAR: suma la energía de su potencia térmica de este tick
///     (potencia disipada × fracción que se vuelve calor, en Joules) dividida
///     por su capacidad térmica → cuántos °C sube.
///
///  2. ENFRIAR: lo acerca a la temperatura ambiente una fracción proporcional
///     a su coeficiente de enfriamiento. Un componente que dejó de recibir
///     energía vuelve solo, de a poco, a la temperatura ambiente.
///
///  3. CONTAGIAR: los componentes CERCANOS entre sí (dentro de RadioVecindad)
///     intercambian calor: el más caliente le pasa energía al más frío,
///     proporcional a la diferencia de temperatura, a qué tan cerca están y a
///     la conductividad térmica de ambos materiales.
///
/// La lista de pares vecinos se recalcula SOLO cuando algo se movió, se
/// agregó o se eliminó (MarcarPosicionesCambiadas), nunca tick a tick — con
/// eso el costo por tick es mínimo. El piso de madera no participa de nada
/// de esto: no es un componente, no conduce ni electricidad ni calor.
///
/// La temperatura ambiente arranca en 20 °C y el usuario la puede cambiar en
/// vivo desde la barra superior.
/// </summary>
public class SimuladorTermico
{
    /// <summary>
    /// INTERRUPTOR GLOBAL de todo el sistema de calor. En false el simulador
    /// se vuelve puramente eléctrico: nada se calienta, nada se quema por
    /// temperatura, ninguna protección térmica actúa y no hay contagio entre
    /// vecinos. Es estático a propósito: es una decisión del usuario sobre el
    /// simulador entero, no sobre una instancia.
    ///
    /// Implementado clavando todas las temperaturas al ambiente en vez de
    /// "saltear el paso": así ninguna lógica que MIRE la temperatura (la
    /// protección del 7805, RevisarTemperatura, el brillo por calor) se puede
    /// disparar, sin tener que meterle un `if` a cada componente.
    /// </summary>
    public static bool Habilitada { get; set; } = true;

    /// <summary>
    /// Ambiente que está usando el simulador AHORA. Es estático por la misma
    /// razón que Habilitada: hay uno solo y los componentes que necesitan
    /// resolver su propio punto de trabajo (la Lampara con su R(T)) lo tienen
    /// que consultar sin cargarse una referencia al simulador.
    /// </summary>
    public static double AmbienteActual { get; private set; } = CircuitConstants.TemperaturaAmbienteDefault;

    /// <summary>Temperatura del aire (°C). Todo tiende hacia acá cuando no recibe energía.</summary>
    public double TemperaturaAmbiente { get; set; } = CircuitConstants.TemperaturaAmbienteDefault;

    /// <summary>A qué distancia (en unidades del mundo) dos componentes se consideran "vecinos" y se pasan calor.</summary>
    public double RadioVecindad { get; set; } = 0.9;

    /// <summary>Factor global de cuánto calor se pasan los vecinos (subilo para un mundo más "contagioso").</summary>
    public double EscalaIntercambio { get; set; } = 0.02;

    private readonly List<(Component a, Component b, double cercania)> _pares = new();
    private bool _paresSucios = true;

    /// <summary>
    /// Avisale al sistema que algo se movió / apareció / desapareció, para
    /// que recalcule qué componentes son vecinos de cuáles. Es barato
    /// llamarlo de más: solo marca una bandera.
    /// </summary>
    public void MarcarPosicionesCambiadas() => _paresSucios = true;

    /// <summary>Un paso térmico completo. Llamalo después de CircuitGraph.Tick, con el mismo deltaTime.</summary>
    public void Paso(IReadOnlyList<Component> componentes, double deltaTime)
    {
        AmbienteActual = TemperaturaAmbiente;

        if (!Habilitada)
        {
            // Térmica apagada: todo al ambiente y salimos. No calienta, no
            // enfría, no contagia y NO se revisa MaxTemperature.
            foreach (var c in componentes)
            {
                c.Temperatura = TemperaturaAmbiente;
            }
            return;
        }

        if (_paresSucios)
        {
            ReconstruirPares(componentes);
        }

        // --- 1 y 2: calentamiento propio + enfriamiento, en UN paso implícito ---
        // BACKWARD EULER, el mismo método del capacitor y del inductor:
        //     C·dT/dt = P − (T − T_amb)/R_termica
        //     T_new = (T + dt·P/C + dt·k·T_amb) / (1 + dt·k)
        // El paso explícito de antes (calentar, y después acercar al ambiente)
        // es INESTABLE cuando la constante de tiempo del componente es más
        // corta que el tick: el filamento de una lámpara, que calienta en 30ms,
        // se iba a decenas de miles de grados en un tick de 100ms y se "fundía"
        // al prenderla. Backward Euler NUNCA se pasa del equilibrio, tenga el
        // dt que tenga — y el equilibrio es exactamente el mismo de antes, así
        // que no recalibra nada de la Parte 4.
        foreach (var c in componentes)
        {
            double capacidad = Math.Max(c.CapacidadTermica, 0.01);
            double k = Math.Max(c.CoeficienteEnfriamiento, 0);

            c.Temperatura = (c.Temperatura + deltaTime * c.PotenciaTermica / capacidad
                             + deltaTime * k * TemperaturaAmbiente)
                            / (1 + deltaTime * k);

            // Y respetar el techo que el componente se pone a sí mismo.
            double techo = c.TechoTermico;
            if (c.Temperatura > techo) c.Temperatura = techo;
        }

        // --- 3: intercambio entre vecinos cercanos ---
        foreach (var (a, b, cercania) in _pares)
        {
            double deltaT = a.TemperaturaParaContagio - b.TemperaturaParaContagio;
            if (Math.Abs(deltaT) < 0.001) continue;

            // k ≈ cuántos Watts fluyen por cada °C de diferencia entre este par.
            double k = Math.Min(a.ConductividadTermica, b.ConductividadTermica) * cercania * EscalaIntercambio;
            double joules = deltaT * k * deltaTime;

            // Límite de estabilidad: nunca transferir más de la mitad de la
            // energía que IGUALARÍA las dos temperaturas (evita oscilaciones
            // si el usuario pone un tick rate muy bajo con deltaTime grande).
            double capA = Math.Max(a.CapacidadTermica, 0.01);
            double capB = Math.Max(b.CapacidadTermica, 0.01);
            double joulesDeEquilibrio = deltaT * (capA * capB / (capA + capB));
            joules = Math.Clamp(joules, -Math.Abs(joulesDeEquilibrio) * 0.5, Math.Abs(joulesDeEquilibrio) * 0.5);

            // Un componente aislado (el filamento de una lámpara) puede ceder
            // calor sin recibirlo, y al revés.
            if (a.AbsorbeContagio) a.Temperatura -= joules / capA;
            if (b.AbsorbeContagio) b.Temperatura += joules / capB;
        }

        // --- ¿alguno superó su temperatura máxima? → se rompe ---
        foreach (var c in componentes)
        {
            c.RevisarTemperatura();
        }
    }

    private void ReconstruirPares(IReadOnlyList<Component> componentes)
    {
        _pares.Clear();
        for (int i = 0; i < componentes.Count; i++)
        {
            for (int j = i + 1; j < componentes.Count; j++)
            {
                double distancia = System.Numerics.Vector3.Distance(
                    componentes[i].PosicionFisica, componentes[j].PosicionFisica);

                if (distancia < RadioVecindad)
                {
                    // cercania: 1 pegados, 0 justo en el borde del radio.
                    _pares.Add((componentes[i], componentes[j], 1.0 - distancia / RadioVecindad));
                }
            }
        }
        _paresSucios = false;
    }
}
