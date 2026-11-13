using System.Numerics; // Vector3 "puro" de .NET para la posición física (sin depender de Godot)

namespace ElectricSim.Core;

/// <summary>
/// Clase base de todo componente eléctrico de 2 bornes: cable, resistencia,
/// batería, LED, capacitor, interruptor... Cada componente concreto solo
/// necesita implementar GetResistance() (cuánto "cuesta" pasar corriente por
/// él en este instante). Todo lo demás —calcular corriente, potencia,
/// voltaje en bornes, temperatura, y detectar si se rompió— vive acá, en un
/// solo lugar, para que cada componente nuevo lo herede gratis.
///
/// DECISIÓN DE DISEÑO (a propósito, para mantener el simulador liviano):
/// cada componente tiene UNA sola ficha de estadísticas. No modelamos por
/// separado la resistencia de cada pata, del material interno, etc. — un
/// componente es una unidad: o funciona entero, o se rompe entero.
/// </summary>
public abstract class Component
{
    public string Name { get; set; }

    /// <summary>Primer borne del componente (por convención, el POSITIVO).</summary>
    public Pin PinA { get; }

    /// <summary>Segundo borne del componente (por convención, el NEGATIVO).</summary>
    public Pin PinB { get; }

    // =====================================================================
    //  LÍMITES — la "ficha de estadísticas" del componente.
    //  Si CUALQUIERA de los cuatro (voltaje, corriente, potencia o
    //  temperatura) supera su máximo, el componente se rompe de forma
    //  PERMANENTE y pasa a comportarse como circuito abierto para siempre.
    //  Todos son editables en vivo desde el inspector de la interfaz.
    // =====================================================================

    /// <summary>Voltaje máximo (Volts) tolerado entre bornes antes de romperse. Infinito = sin límite.</summary>
    public double MaxVoltage { get; set; } = double.PositiveInfinity;

    /// <summary>Corriente máxima (Amps) tolerada antes de romperse. Infinito = sin límite.</summary>
    public double MaxCurrent { get; set; } = double.PositiveInfinity;

    /// <summary>Potencia máxima (Watts) tolerada antes de romperse. Infinito = sin límite.</summary>
    public double MaxPower { get; set; } = double.PositiveInfinity;

    /// <summary>Temperatura máxima (°C) tolerada antes de romperse.</summary>
    public double MaxTemperature { get; set; } = 150;

    /// <summary>Voltaje mínimo (Volts) necesario entre bornes para funcionar. 0 = sin mínimo.</summary>
    public double MinVoltage { get; set; } = 0;

    /// <summary>Corriente mínima (Amps) necesaria para funcionar. 0 = sin mínimo.</summary>
    public double MinCurrent { get; set; } = 0;

    /// <summary>
    /// Potencia mínima (Watts) necesaria para funcionar, calculada
    /// automáticamente como <see cref="MinVoltage"/> × <see cref="MinCurrent"/>.
    /// </summary>
    public double MinPower => Math.Max(0, MinVoltage) * Math.Max(0, MinCurrent);

    /// <summary>True si el componente está sano pero no alcanza el voltaje, amperaje o watts mínimos configurados.</summary>
    public bool BajoMinimoOperacion { get; protected set; }

    /// <summary>Descripción de qué mínimo no se alcanza, o cadena vacía si cumple.</summary>
    public string MotivoBajoMinimo { get; protected set; } = "";

    /// <summary>
    /// Cuántos ticks consecutivos de sobrecarga eléctrica se toleran antes de
    /// romperse (evita que un pico de 1 solo tick lo destruya). La ruptura
    /// por temperatura es inmediata: el calor ya sube de a poco por sí solo.
    /// </summary>
    /// <summary>
    /// Cuántos SEGUNDOS seguidos tiene que sostenerse una sobrecarga (o un
    /// cortocircuito) para que el componente se queme. Antes esto se contaba
    /// en ticks, y subir TicksPorSegundo (p.ej. a 60 para trabajar con el
    /// Arduino) hacía que todo muriera 6 veces más rápido — un bug. En
    /// segundos, el mundo se rompe igual de rápido a cualquier tick-rate.
    /// </summary>
    public double SegundosParaQuemarse { get; set; } = 0.3;

    /// <summary>True una vez que el componente se rompió. A partir de acá actúa como circuito abierto para siempre.</summary>
    public bool IsBurnt { get; protected set; }

    /// <summary>Por qué se rompió: "potencia", "corriente", "voltaje", "temperatura", "cortocircuito"... Para mostrar en la interfaz.</summary>
    public string MotivoQuemado { get; private set; } = "";

    // =====================================================================
    //  LECTURAS del último tick resuelto (lo que muestra el inspector).
    // =====================================================================

    /// <summary>Corriente que atraviesa el componente (Amps). Positiva = de PinA hacia PinB.</summary>
    public double Current { get; protected set; }

    /// <summary>Potencia disipada en el último tick resuelto (Watts).</summary>
    public double Power { get; protected set; }

    /// <summary>Diferencia de voltaje entre bornes, en valor absoluto (Volts).</summary>
    public double VoltajeEnBornes { get; protected set; }

    /// <summary>
    /// True si en el tick en curso este componente actuó como conexión
    /// perfecta y sus pines quedaron fusionados en un solo nodo. Lo marca
    /// CircuitGraph al comienzo de cada tick.
    /// </summary>
    internal bool FusionadoEsteTick;

    /// <summary>
    /// La corriente que ATRAVIESA este componente cuando está fusionado,
    /// calculada por CircuitGraph con Ley de Kirchhoff (ver
    /// AsignarCorrientesDeFusionados). Un componente fusionado no puede
    /// calcular su corriente con (Va−Vb)/R — sus dos bornes son el MISMO
    /// nodo y la cuenta daría 0/algo = 0 — así que el grafo se la asigna.
    /// </summary>
    internal double CorrienteFusionada;

    // =====================================================================
    //  CORRIENTE ALTERNA / CONTINUA
    // =====================================================================

    /// <summary>Con qué tipo de corriente puede trabajar este componente.</summary>
    public CompatibilidadCorriente Compatibilidad { get; set; } = CompatibilidadCorriente.ACyDC;

    /// <summary>Qué tipo de corriente le está llegando ahora mismo (lo calcula CircuitGraph cada tick).</summary>
    public TipoCorriente TipoCorrienteRecibida { get; internal set; } = TipoCorriente.Ninguna;

    /// <summary>
    /// True si el componente está recibiendo un tipo de corriente con el que
    /// no puede trabajar. En ese estado se comporta como circuito abierto:
    /// no conduce, no funciona, pero NO se rompe (simplemente "no anda").
    /// </summary>
    public bool EsIncompatible =>
        TipoCorrienteRecibida != TipoCorriente.Ninguna &&
        Compatibilidad switch
        {
            CompatibilidadCorriente.SoloDC => TipoCorrienteRecibida != TipoCorriente.DC,
            CompatibilidadCorriente.SoloAC => TipoCorrienteRecibida != TipoCorriente.AC,
            _ => false,
        };

    // =====================================================================
    //  TÉRMICO — cada componente genera calor según la potencia que disipa,
    //  se lo puede pasar a los componentes cercanos, y se enfría solo hacia
    //  la temperatura ambiente. Todo esto lo mueve SimuladorTermico.
    // =====================================================================

    /// <summary>Temperatura actual del componente (°C). Arranca en la temperatura ambiente al colocarlo.</summary>
    public double Temperatura { get; set; } = CircuitConstants.TemperaturaAmbienteDefault;

    /// <summary>
    /// Cuánta energía (Joules) hace falta para subirle 1 °C. Representa el
    /// "material y tamaño" del componente: valores chicos = se calienta y se
    /// enfría rápido (un filamento); grandes = lento (un bloque de batería).
    /// </summary>
    public double CapacidadTermica { get; set; } = 0.8;

    /// <summary>
    /// Qué tan rápido tiende hacia la temperatura ambiente (fracción por
    /// segundo). Representa qué tan bien ventila/disipa su material.
    /// </summary>
    /// <summary>
    /// RESISTENCIA TÉRMICA en °C/W: cuántos grados sube este componente por
    /// cada Watt que disipa, en equilibrio. Es LA unidad que aparece en las
    /// hojas de datos (θ_ja), así que cada valor del simulador se puede
    /// justificar contra una: TO-220 sin disipador ≈ 60, TO-92 ≈ 200,
    /// resistencia de ½W ≈ 250.
    ///
    /// Sale directo de la ecuación de equilibrio del SimuladorTermico:
    ///     T_eq = T_ambiente + P × ResistenciaTermica
    /// Antes la unidad primaria era CoeficienteEnfriamiento, un número opaco
    /// del que no se podía decir si estaba bien o mal — y estaba mal: el 7805
    /// daba 6.7 °C/W cuando un TO-220 pelado tiene ~60, o sea 9× más frío, y
    /// su protección térmica era inalcanzable (0.5).
    /// </summary>
    public double ResistenciaTermica { get; set; } = 25;   // = 1/(0.8 × 0.05): idéntico al default viejo

    /// <summary>
    /// Cuánto se acerca al ambiente por segundo. Ya no se guarda: se deriva de
    /// ResistenciaTermica. Se mantiene como propiedad (con setter) para no
    /// romper el código que la seteaba directo — ojo con el orden, el setter
    /// usa CapacidadTermica, así que asignala DESPUÉS de la capacidad.
    /// </summary>
    public double CoeficienteEnfriamiento
    {
        get => 1.0 / (Math.Max(CapacidadTermica, 0.01) * Math.Max(ResistenciaTermica, 0.01));
        set => ResistenciaTermica = 1.0 / (Math.Max(CapacidadTermica, 0.01) * Math.Max(value, 1e-6));
    }

    /// <summary>
    /// Qué tan bien intercambia calor con VECINOS cercanos (factor relativo,
    /// 1 = normal). Un material aislante tendría un valor bajo.
    /// </summary>
    public double ConductividadTermica { get; set; } = 1.0;

    /// <summary>
    /// Qué fracción de la potencia eléctrica disipada se convierte en calor
    /// propio. En una resistencia es prácticamente todo (1.0); en un LED,
    /// parte se va en luz.
    /// </summary>
    public double FraccionCalorDisipado { get; set; } = 1.0;

    /// <summary>
    /// Posición del componente en el mundo (la escribe la capa visual al
    /// colocarlo o moverlo). El motor la usa SOLO para saber qué componentes
    /// están cerca entre sí y pueden intercambiar calor. Es un Vector3 de
    /// System.Numerics a propósito: el motor no depende de Godot para nada.
    /// </summary>
    public Vector3 PosicionFisica { get; set; }

    /// <summary>
    /// Potencia térmica (Watts) que este componente está generando AHORA.
    /// Por defecto: potencia eléctrica disipada × fracción que se vuelve
    /// calor. Las fuentes la sobreescriben (calientan por su resistencia
    /// interna, y muchísimo si están en cortocircuito).
    /// </summary>
    public virtual double PotenciaTermica => IsBurnt ? 0 : Power * FraccionCalorDisipado;

    /// <summary>
    /// Techo de temperatura que este componente puede alcanzar con su punto de
    /// operación ACTUAL. Por defecto no hay techo y manda la ecuación térmica.
    /// La Lampara lo usa: su resistencia sube con la temperatura, así que su
    /// propia física la frena — pero ese lazo sólo se cierra ENTRE ticks, y con
    /// un tick grueso el integrador se pasa de largo antes de que la R alcance
    /// a subir (ver Lampara.TemperaturaEquilibrio).
    /// </summary>
    public virtual double TechoTermico => double.PositiveInfinity;

    /// <summary>
    /// La temperatura que un VECINO percibe de este componente cuando se
    /// contagian calor. Casi siempre es su propia temperatura. La Lampara la
    /// redefine: su `Temperatura` es la del FILAMENTO (~2000°C), que el
    /// componente de al lado no toca — lo que toca es el vidrio.
    /// </summary>
    public virtual double TemperaturaParaContagio => Temperatura;

    /// <summary>
    /// ¿Este componente se calienta o enfría por el contagio de sus vecinos?
    /// La Lampara no: el calor que le pasa un vecino no llega a su filamento, y
    /// si llegara le cambiaría la resistencia y, con ella, su corriente.
    /// </summary>
    public virtual bool AbsorbeContagio => true;

    /// <summary>
    /// Resultado del SolverAlterna para este componente en este tick (tensión y
    /// corriente eficaces, potencia activa, fase), o null si no está en una red
    /// de alterna con capacitores o inductores. Lo calcula CircuitGraph antes
    /// del PostSolve; los componentes sólo lo leen.
    /// </summary>
    public ResultadoAlterna? ResultadoAC { get; internal set; }

    private double _segundosEnSobrecarga;

    /// <summary>
    /// Si devuelve true, en este tick el componente se trata como una conexión
    /// PERFECTA: sus dos pines se fusionan en un solo nodo eléctrico (en vez de
    /// ser una arista de relajación con resistencia). Lo implementan los Cables
    /// y los Interruptores cerrados. Default false: la mayoría de los
    /// componentes (resistencia, LED, capacitor, batería) sí tienen una caída
    /// de voltaje real y deben pasar por la relajación.
    /// </summary>
    public virtual bool EsConexionPerfecta() => false;

    /// <summary>
    /// La versión de EsConexionPerfecta que usa el solver: un componente roto
    /// o incompatible con la corriente que recibe NUNCA fusiona sus pines,
    /// diga lo que diga su lógica propia. Así cada componente concreto no
    /// tiene que acordarse de chequear IsBurnt por su cuenta.
    /// </summary>
    internal bool EsConexionPerfectaEfectiva() => !IsBurnt && !EsIncompatible && EsConexionPerfecta();

    /// <summary>
    /// La resistencia que ve el solver: circuito abierto si está roto o si es
    /// incompatible con el tipo de corriente que recibe; si no, la resistencia
    /// real del componente (con piso numérico de seguridad).
    /// </summary>
    internal double ResistenciaParaSolver() =>
        IsBurnt || EsIncompatible
            ? CircuitConstants.OpenResistance
            : Math.Max(GetResistance(), CircuitConstants.MinResistance);

    /// <param name="pinA">Pin externo a reutilizar como borne A (ej. para un Cable). Si es null, se crea un pin propio.</param>
    /// <param name="pinB">Pin externo a reutilizar como borne B. Si es null, se crea un pin propio.</param>
    protected Component(string name, Pin? pinA = null, Pin? pinB = null)
    {
        Name = name;
        // Cada componente tiene asignado su positivo y su negativo. Si el pin
        // viene de afuera (caso Cable, que une bornes que YA existen), se
        // respeta la polaridad que ya traía.
        // POR DEFECTO NO HAY POLARIDAD, y es a propósito. Antes todo componente
        // nacía con un borne rojo y uno negro: una resistencia, un interruptor,
        // un fusible, un cable. Es mentira — esos componentes se conectan para
        // cualquier lado y funcionan igual, y de hecho el solver SIEMPRE los
        // trató así (relaja voltajes; la corriente va de más a menos, y la
        // polaridad nunca se consultó en la física).
        //
        // Los que SÍ tienen polaridad de verdad la declaran ellos: pilas, LED,
        // diodos, electrolítico, transistores, MOSFET, 7805, Arduino. Y las
        // fuentes de alterna usan Fase/Neutro, que es otra cosa.
        PinA = pinA ?? new Pin(this, $"{name}.A") { Polaridad = Polaridad.Ninguna };
        PinB = pinB ?? new Pin(this, $"{name}.B") { Polaridad = Polaridad.Ninguna };
    }

    /// <summary>
    /// Resistencia actual del componente, en Ohms, según su estado en este instante.
    /// Se llama varias veces por tick (una por iteración de relajación), así que
    /// tiene que ser una operación liviana. Nunca debe devolver 0 exacto.
    /// </summary>
    public abstract double GetResistance();

    /// <summary>
    /// Se llama UNA VEZ por tick, después de que CircuitGraph terminó de resolver
    /// los voltajes de toda la red. Acá se calcula la corriente/potencia final y
    /// se le da a cada componente la chance de actualizar su estado interno
    /// (cargar un capacitor, decidir si un LED brilla, chequear si se rompió, etc).
    /// </summary>
    public virtual void PostSolve(double deltaTime)
    {
        if (IsBurnt)
        {
            Current = 0;
            Power = 0;
            VoltajeEnBornes = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (EsIncompatible)
        {
            // Incompatible con el tipo de corriente que recibe: el componente
            // queda INERTE. Mostramos el voltaje presente en sus bornes (dato
            // útil en el inspector) pero corriente y potencia son cero duro —
            // sin esto quedaría la microfuga de la resistencia "abierta"
            // (1MΩ), que confunde más de lo que enseña.
            VoltajeEnBornes = Math.Abs(PinA.Voltage - PinB.Voltage);
            Current = 0;
            Power = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (ResultadoAC is { } alterna && !FusionadoEsteTick)
        {
            // Red de alterna con reactancias, resuelta con fasores: valores
            // RMS de verdad (ver SolverAlterna).
            VoltajeEnBornes = alterna.VoltajeRms;
            Current = alterna.CorrienteRms;
            Power = alterna.PotenciaActiva;
            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ActualizarEstado(deltaTime);
            ChequearQuemado(deltaTime);
            return;
        }

        if (FusionadoEsteTick)
        {
            // Conexión perfecta: sus dos bornes son el mismo nodo, así que no
            // CAE voltaje en él. Pero la CORRIENTE que lo atraviesa es bien
            // real (la calculó CircuitGraph por Kirchhoff) y su resistencia
            // también, así que I²·R ES calor de verdad: un cable de arranque
            // con 240A disipa 57.6W y se pone al rojo. Antes esto era 0 y por
            // eso un aro en corto sobre una batería de auto quedaba a
            // temperatura ambiente (B8, A9).
            VoltajeEnBornes = 0;
            Current = CorrienteFusionada;
            Power = Current * Current * Math.Max(GetResistance(), 0);

            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ActualizarEstado(deltaTime); // el pulsador descuenta su timer acá
            ChequearQuemado(deltaTime);
            return;
        }

        double r = ResistenciaParaSolver();
        double deltaV = PinA.Voltage - PinB.Voltage;
        VoltajeEnBornes = Math.Abs(deltaV);
        Current = LeyesElectricas.Corriente(deltaV, r);
        Power = LeyesElectricas.PotenciaDesdeVI(deltaV, Current);

        EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
        ActualizarEstado(deltaTime);
        ChequearQuemado(deltaTime);
    }

    /// <summary>Limpia el indicador de falta de energía mínima.</summary>
    protected void LimpiarMinimosOperacion()
    {
        BajoMinimoOperacion = false;
        MotivoBajoMinimo = "";
    }

    /// <summary>
    /// Verifica si el voltaje, el amperaje y su producto automático en watts
    /// superan los mínimos configurados. Devuelve true cuando cumple o no hay mínimos activos.
    /// </summary>
    protected bool EvaluarMinimosOperacion(double voltaje, double corriente, double potencia)
    {
        if (IsBurnt || EsIncompatible)
        {
            LimpiarMinimosOperacion();
            return false;
        }

        double tolV = Math.Max(1e-4, MinVoltage * 1e-3);
        double tolI = Math.Max(1e-6, MinCurrent * 1e-3);
        double tolP = Math.Max(1e-6, MinPower * 1e-3);
        bool faltoV = MinVoltage > 0 && voltaje + tolV < MinVoltage;
        bool faltoI = MinCurrent > 0 && Math.Abs(corriente) + tolI < MinCurrent;
        bool faltoP = MinPower > 0 && potencia + tolP < MinPower;

        if (!faltoV && !faltoI && !faltoP)
        {
            LimpiarMinimosOperacion();
            return true;
        }

        BajoMinimoOperacion = true;
        var partes = new List<string>(3);
        if (faltoV) partes.Add($"voltaje ({voltaje:F2} V < {MinVoltage:F2} V)");
        if (faltoI) partes.Add($"amperaje ({Math.Abs(corriente):F3} A < {MinCurrent:F3} A)");
        if (faltoP) partes.Add($"watts ({potencia:F3} W < {MinPower:F3} W)");
        MotivoBajoMinimo = string.Join(" · ", partes);
        return false;
    }

    /// <summary>Hook para que cada componente concreto actualice su lógica propia (capacitor cargándose, LED prendiéndose, etc).</summary>
    protected virtual void ActualizarEstado(double deltaTime) { }

    /// <summary>
    /// Revisa los límites ELÉCTRICOS (potencia, corriente, voltaje). Si alguno
    /// se supera durante SegundosParaQuemarse segundos seguidos, el componente
    /// se rompe y queda registrado el motivo. La temperatura se revisa aparte
    /// (RevisarTemperatura), porque la mueve el SimuladorTermico después del
    /// paso eléctrico.
    /// </summary>
    protected void ChequearQuemado(double deltaTime)
    {
        string? motivo = null;
        if (Power > MaxPower) motivo = "potencia";
        else if (Math.Abs(Current) > MaxCurrent) motivo = "corriente";
        else if (VoltajeEnBornes > MaxVoltage) motivo = "voltaje";

        if (motivo != null)
        {
            _segundosEnSobrecarga += deltaTime;
            if (_segundosEnSobrecarga >= SegundosParaQuemarse)
            {
                Quemar(motivo);
            }
        }
        else
        {
            _segundosEnSobrecarga = 0;
        }
    }

    /// <summary>La llama SimuladorTermico después de actualizar las temperaturas de todos.</summary>
    public void RevisarTemperatura()
    {
        if (!IsBurnt && Temperatura > MaxTemperature)
        {
            Quemar("temperatura");
        }
    }

    /// <summary>Rompe el componente dejando registrado el motivo (para el inspector).</summary>
    protected void Quemar(string motivo)
    {
        if (IsBurnt) return;
        MotivoQuemado = motivo;
        Quemar();
    }

    /// <summary>Qué pasa cuando el componente se rompe. Sobreescribible si un componente necesita lógica extra (ej. apagar su luz).</summary>
    protected virtual void Quemar()
    {
        IsBurnt = true;
        Current = 0;
        Power = 0;
        LimpiarMinimosOperacion();
    }

    /// <summary>
    /// Repara el componente al instante: deja de estar quemado, olvida el
    /// motivo, se enfría a la temperatura que le pasan y resetea los timers
    /// de sobrecarga (así no se vuelve a quemar en el acto por un contador
    /// viejo). Cada componente concreto revive su estado propio
    /// sobreescribiendo Reparado() — el inverso de Quemar().
    /// </summary>
    public void Reparar(double temperatura)
    {
        IsBurnt = false;
        MotivoQuemado = "";
        Temperatura = temperatura;
        Current = 0;
        Power = 0;
        VoltajeEnBornes = 0;
        _segundosEnSobrecarga = 0;
        LimpiarMinimosOperacion();
        Reparado();
    }

    /// <summary>Hook para revivir el estado propio de un componente al repararlo (inverso de Quemar()). Por defecto no hace nada.</summary>
    protected virtual void Reparado() { }

    public override string ToString() =>
        $"{Name}: {(IsBurnt ? $"QUEMADO ({MotivoQuemado})" : $"{Current * 1000:F2}mA, {Power * 1000:F2}mW, {Temperatura:F1}°C")}";
}
