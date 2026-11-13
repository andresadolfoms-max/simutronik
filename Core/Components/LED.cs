namespace ElectricSim.Core.Components;

/// <summary>
/// LED modelado como "diodo de caída constante" con convergencia por
/// resistencia dinámica. Es el modelo estándar de los simuladores livianos,
/// y reemplaza al viejo "umbral + resistencia FIJA de 100Ω", que tenía un
/// bug serio: con la resistencia limitadora de manual (330–1000Ω) el LED
/// PARPADEABA para siempre — al conducir, la resistencia fija le dejaba caer
/// menos voltaje que su umbral, así que se apagaba al tick siguiente, se
/// volvía a prender al otro, y así a 5Hz. (Verificado con un test: hoy hay
/// una regresión que lo vigila.)
///
/// CÓMO FUNCIONA AHORA: cuando el voltaje directo alcanza el umbral, el LED
/// conduce con una resistencia DINÁMICA que se reajusta una vez por tick con
/// la regla del modelo compañero:
///
///     R_siguiente = VoltajeUmbral / |I_actual|
///
/// Esa regla es una contracción (factor VoltajeUmbral / V_total, siempre
/// menor a 1 cuando hay voltaje de sobra), así que converge sola, sin
/// oscilar, al punto de operación físicamente EXACTO de un diodo ideal:
///
///     V_led = VoltajeUmbral      I = (V_fuente − VoltajeUmbral) / R_serie
///
/// que es exactamente la fórmula que se enseña para calcular la resistencia
/// limitadora de un LED. Antes el simulador daba I = V/(R_serie+100), un
/// ~12% de error; ahora da el número del apunte.
///
/// PRECIO ASUMIDO (documentado a propósito): la convergencia toma unos
/// ticks. Con la SIEMBRA DEL PISO al entrar en conducción, el LED da luz en
/// el segundo tick y llega a su punto exacto en 4 (antes: 10 ticks con 9V y
/// 29 con una pila de 3V). Es el costo de mantener la decisión de diseño de
/// congelar la resistencia POR TICK: si el LED cambiara su resistencia en el
/// medio de las iteraciones de relajación, el solver puede quedar oscilando
/// dentro del mismo tick y reportar corrientes inconsistentes (lo vimos
/// pasar con un LED detrás de un fusible cortado).
///
/// Además el LED "necesita un mínimo para funcionar" por partida doble:
/// voltaje de umbral para conducir, y una corriente mínima para dar luz
/// visible (con microamperes de fuga no titila).
///
/// Es un componente SOLO-DC: si le llega corriente alterna, la clasificación
/// de la red lo detecta y el LED queda inerte (con advertencia en el
/// inspector). Y como todo diodo, tiene polaridad: conectado al revés no
/// prende, y si el voltaje inverso supera MaxVoltajeInverso durante varios
/// ticks, se rompe (ruptura inversa, como un LED real).
/// </summary>
public class LED : Component, IComponenteLuminoso
{
    /// <summary>Color personalizado (RGB 0..1) para la cápsula y la luz emitida; null = predeterminado.</summary>
    public ColorLuzRgb? ColorPersonalizado { get; set; }

    /// <summary>Voltaje de caída directa (Volts): el umbral para conducir Y la caída que el LED mantiene conduciendo. ~1.8-2.2V en LEDs rojos típicos.</summary>
    public double VoltajeUmbral { get; set; }

    /// <summary>
    /// Resistencia MÍNIMA (Ohms) del LED conduciendo — la resistencia
    /// dinámica nunca baja de acá. Representa la resistencia interna real del
    /// diodo (unos pocos Ohms) y es el "freno" que decide cuánta corriente
    /// pasa si conectás el LED SIN resistencia limitadora (y por lo tanto,
    /// qué tan rápido se quema en ese caso).
    /// </summary>
    public double ResistenciaEncendido { get; set; }

    /// <summary>Corriente mínima (Amps) para que el LED dé luz visible. Por debajo, conduce pero no ilumina.</summary>
    public double CorrienteMinimaEncendido { get; set; } = 0.001; // 1mA

    /// <summary>Corriente (Amps) que se considera "brillo máximo" para normalizar Brillo a 0-1.</summary>
    public double CorrienteBrilloMaximo { get; set; } = 0.020; // 20mA, típico de un LED indicador chico

    /// <summary>
    /// Voltaje de RUPTURA INVERSA (Volts): más allá de esta caída al revés,
    /// el LED entra en AVALANCHA — conduce en inversa con la caída clavada
    /// en este valor (mismo modelo compañero de la directa, espejado). La
    /// muerte NO es por el voltaje en sí: es por la potencia/corriente de la
    /// avalancha. Al revés y SIN limitadora, la corriente solo la frena la
    /// fuente → muere en segundos; CON limitadora la corriente queda chica y
    /// sobrevive — los dos experimentos clásicos salen bien de la misma física.
    /// (Antes había una guardia de corriente con el LED "abierto" en inversa
    /// que era inalcanzable: el LED sobrevivía hasta 400V al revés — QA4.)
    /// </summary>
    /// <summary>
    /// Tensión inversa a la que la juntura SE PERFORA (destructivo). Los 5V de
    /// las hojas de datos son el "máximo garantizado", no dónde revienta: un
    /// LED real aguanta ~15-25V al revés antes de morir.
    /// </summary>
    public double MaxVoltajeInverso { get; set; } = 20;

    /// <summary>True mientras el LED está conduciendo en ruptura inversa (avalancha). No emite luz.</summary>
    public bool EnAvalanchaInversa { get; private set; }

    public bool EstaEncendido { get; private set; }

    /// <summary>0 = apagado, 1 = brillo máximo.</summary>
    public double Brillo { get; private set; }

    /// <summary>
    /// La resistencia dinámica del modelo compañero, congelada durante cada
    /// tick (ver comentario de la clase) y reajustada al final en
    /// ActualizarEstado. Arranca en "abierto" (LED apagado).
    /// </summary>
    private double _resistenciaDinamica = CircuitConstants.OpenResistance;

    public LED(string name, double voltajeUmbral = 2.0, double resistenciaEncendido = 12) : base(name)
    {        // Polaridad REAL: conectarlo al revés cambia lo que hace.
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;

        VoltajeUmbral = voltajeUmbral;
        ResistenciaEncendido = resistenciaEncendido;
        MaxPower = 0.25; // vatios — valor típico antes de que un LED chico se queme
        Compatibilidad = CompatibilidadCorriente.SoloDC;
        FraccionCalorDisipado = 0.75; // parte de la energía se va en luz
        CapacidadTermica = 0.3;
        CoeficienteEnfriamiento = 0.06;
        MaxTemperature = 120;
    }

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;
        return _resistenciaDinamica;
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        double deltaV = PinA.Voltage - PinB.Voltage;

        // ¿Está conduciendo ahora mismo? (la resistencia dinámica está por
        // debajo de "abierto"). Lo usamos para la histéresis de abajo.
        bool conduciendo = _resistenciaDinamica < CircuitConstants.OpenResistance * 0.5;

        // Ruptura inversa. Ya NO hay "permanencia": la ruptura de un LED es
        // destructiva, así que se entra una sola vez y no se vuelve (P10-1).
        bool entraEnAvalancha = !double.IsPositiveInfinity(MaxVoltajeInverso)
                                && deltaV <= -MaxVoltajeInverso;

        double vfEfectivo = MinVoltage > 0 ? Math.Max(VoltajeUmbral, MinVoltage) : VoltajeUmbral;

        // DIRECTA: el MISMO criterio que la avalancha — se ENTRA por voltaje y
        // se SIGUE por corriente. Reemplaza a la histéresis de 50mV, y es la
        // mitad IMPRESCINDIBLE del arranque rápido: apenas se siembra el piso,
        // la caída del LED se desploma muy por debajo del umbral (con 12Ω en un
        // lazo de 220Ω son 0.46V), y el viejo "deltaV >= umbral" lo apagaba al
        // tick siguiente, dejándolo parpadeando entre 12Ω y 1MΩ para siempre.
        bool entraEnDirecta = !conduciendo && deltaV >= vfEfectivo;
        bool sigueEnDirecta = conduciendo && Current > 1e-5;

        if (entraEnDirecta || sigueEnDirecta)
        {
            EnAvalanchaInversa = false;
            if (!conduciendo)
            {
                // ARRANQUE: en el tick en que ENTRA en conducción, sembrar la
                // resistencia en su piso y saltear el modelo compañero, que
                // todavía vería la corriente calculada con el 1MΩ de "abierto"
                // (microamperes) y devolvería otro 1MΩ. Es exactamente lo que
                // ya hacía la rama de avalancha. Desde el piso, la contracción
                // converge SUBIENDO en 2-3 ticks.
                _resistenciaDinamica = Math.Max(ResistenciaEncendido, CircuitConstants.MinResistance);
            }
            else
            {
                // Regla del modelo compañero: apuntar la resistencia para que la
                // caída del próximo tick sea el VoltajeUmbral (o MinVoltage si es mayor). Con la corriente de
                // ESTE tick (I), la resistencia que dejaría caer exactamente Vf es
                // Vf/I. Es una contracción: converge en unos pocos ticks, sin
                // oscilar, al punto exacto V_led = Vf.
                double corriente = Math.Abs(Current);
                double objetivo = corriente > 1e-9
                    ? vfEfectivo / corriente
                    : CircuitConstants.OpenResistance;

                _resistenciaDinamica = Math.Clamp(
                    objetivo, ResistenciaEncendido, CircuitConstants.OpenResistance);
            }
        }
        else if (entraEnAvalancha)
        {
            // RUPTURA INVERSA DESTRUCTIVA. Un LED NO es un Zener: pasada su
            // tensión inversa, la juntura se PERFORA y el LED queda muerto.
            // Antes esto era una avalancha CONDUCENTE que clavaba la caída en
            // MaxVoltajeInverso — o sea que con 220Ω el LED al revés regulaba
            // 5.00V a 18mA y se podía usar de referencia de tensión (B1).
            // Además MaxVoltajeInverso era 5V, que es el "máximo garantizado"
            // de la hoja de datos, no la tensión a la que revienta.
            EnAvalanchaInversa = true;
            _resistenciaDinamica = CircuitConstants.OpenResistance;
            Quemar("ruptura inversa");
        }
        else
        {
            EnAvalanchaInversa = false;
            _resistenciaDinamica = CircuitConstants.OpenResistance; // apagado
        }

        bool cumpleMinimos = EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
        EstaEncendido = cumpleMinimos && Current >= Math.Max(CorrienteMinimaEncendido, MinCurrent);
        Brillo = EstaEncendido ? Math.Clamp(Current / CorrienteBrilloMaximo, 0, 1) : 0;
    }

    protected override void Quemar()
    {
        base.Quemar();
        EstaEncendido = false;
        EnAvalanchaInversa = false;
        Brillo = 0;
        _resistenciaDinamica = CircuitConstants.OpenResistance;
    }

    protected override void Reparado()
    {
        _resistenciaDinamica = CircuitConstants.OpenResistance; // arranca apagado y re-converge solo
        EnAvalanchaInversa = false;
        EstaEncendido = false;
        Brillo = 0;
    }
}
