namespace ElectricSim.Core;

/// <summary>
/// Base común de toda fuente de voltaje (Battery para continua, FuenteAC para
/// alterna). Centraliza lo que las fuentes tienen de especial respecto de un
/// componente normal:
///
///  1. Sus dos bornes son pines FIJOS: el solver no les calcula el voltaje,
///     lo respeta como dato (PinB = 0V de referencia, PinA = Voltage).
///
///  2. Su corriente NO se puede calcular con la fórmula genérica (V−V)/R,
///     porque con ambos bornes fijos daría un número inventado. En su lugar,
///     CircuitGraph le asigna la corriente REAL por Ley de Kirchhoff: la suma
///     de las corrientes de todas las aristas que salen de su nodo positivo.
///     (Esto reemplaza la limitación de la v1, donde la batería mostraba 0.)
///
///  3. CORTOCIRCUITO: si sus dos bornes terminan fusionados en el mismo nodo
///     (un cable directo de + a −), o fusionados con los bornes fijos de otra
///     fuente a distinto voltaje, CircuitGraph lo detecta y le avisa. La
///     corriente del corto es la física: V / R_interna. Qué pasa después
///     depende de la fuente: si esa corriente supera su MaxCurrent, muere en
///     SegundosParaQuemarse (pila botón, regulador del Arduino); si no, se
///     queda viva EN CORTO convertida en estufa (I²·R adentro), calentándose
///     hacia su equilibrio térmico — como una 9V real, que cortocircuitada
///     se pone que quema pero no explota.
///
/// Fuentes EN SERIE (el + de una al − de otra) SUMAN voltajes de verdad:
/// cada fuente impone una DIFERENCIA (V+ = V− + Voltaje) y CircuitGraph las
/// propaga hasta que todos los nodos fijos cierran — dos 9V dan 18V, como
/// corresponde. Fuentes en PARALELO del mismo voltaje se reparten la
/// corriente en partes iguales; de voltajes DISTINTOS es un conflicto real
/// y ambas quedan marcadas en corto.
/// </summary>
public abstract class FuenteBase : Component, IVoltageSource
{
    /// <summary>Voltaje que la fuente empuja a la red. En una fuente AC, es el valor RMS.</summary>
    public double Voltage { get; set; }

    /// <summary>Qué tipo de corriente entrega esta fuente (DC o AC). Lo usa la clasificación de la red.</summary>
    public abstract TipoCorriente Tipo { get; }

    /// <summary>
    /// True si la fuente está ENCENDIDA: viva y con voltaje apreciable. Una
    /// fuente apagada (V≈0: convertidor sin alimentar, regulador en
    /// protección, batería puesta en 0V) se comporta como ALTA IMPEDANCIA:
    /// no fija sus bornes, no participa del árbol de corrientes y su arista
    /// interna queda abierta. Sin esto, un aparato apagado colgado en
    /// paralelo aplastaba el riel entero a 0V (bug QA13).
    /// </summary>
    public bool EstaEncendida => !IsBurnt && Math.Abs(Voltage) > 0.05;

    /// <summary>
    /// ¿Esta fuente está realmente empujando su tipo a la red AHORA? Sigue a
    /// EstaEncendida: una batería es DC aunque nadie la use, pero un
    /// transformador sin alimentar no etiqueta su secundario como "AC" — sin
    /// esto, un LED conectado ahí diría "incompatible" sin que circule nada.
    /// </summary>
    public virtual bool EstaEntregandoTipo => EstaEncendida;

    /// <summary>
    /// Resistencia interna NOMINAL (Ohms). SÍ participa del solver ahora: el
    /// voltaje efectivo que la fuente empuja a la red se ajusta cada tick como
    /// V_ef = V·V_ef / (V_ef + |I|·R_int), una fórmula analítica del divisor
    /// de tensión que converge en 1-2 ticks al punto físico correcto. Efecto:
    /// bajo carga la fuente CEDE de verdad (bornes = V - I·R), y en corto
    /// V_ef colapsa a 0. La serie de fuentes sigue funcionando: cada fuente
    /// aporta su V_ef al ResolverGruposFijos exactamente como antes.
    /// </summary>
    public double ResistenciaInternaNominal { get; set; } = 0.8;

    /// <summary>True si en este tick el solver detectó a esta fuente en cortocircuito.</summary>
    public bool EnCortocircuito { get; internal set; }

    /// <summary>Corriente real (Amps) que la fuente está entregando, calculada por Kirchhoff. La asigna CircuitGraph.</summary>
    public double CorrienteEntregada { get; internal set; }

    /// <summary>
    /// El voltaje que la fuente "empuja" al solver EN ESTE MOMENTO. Con carga
    /// converge a V - I·R_int en pocos ticks. En corto, colapsa a 0.
    /// </summary>
    public double VoltajeEfectivoParaSolver => _vEfectivo;

    private double _vEfectivo;

    /// <summary>
    /// Cuanto le comio DE VERDAD la resistencia interna en este tick:
    /// V_nominal − V_bornes. Desde el Fix 4 este numero no es una estimacion
    /// paralela — es la caida que el solver ya aplico. Con la fuente en vacio
    /// da 0; en corto da el voltaje entero.
    /// </summary>
    public double CaidaInternaReal =>
        IsBurnt ? 0 : Math.Max(0, Math.Abs(Voltage) - VoltajeEnBornes);

    private double _segundosEnCorto;

    protected FuenteBase(string name, double voltage) : this(name, voltage, null, null) { }

    /// <summary>
    /// Variante con bornes provistos desde afuera: la usan los componentes
    /// compuestos que llevan una fuente ADENTRO conectada a bornes que crea
    /// el dueño (ej. el regulador de 3.3V de la placa Arduino).
    /// </summary>
    protected FuenteBase(string name, double voltage, Pin? pinA, Pin? pinB) : base(name, pinA, pinB)
    {
        Voltage = voltage;
        _vEfectivo = voltage; // arranca en el nominal; se ajusta con la carga
        PinA.IsFixed = true;
        PinB.IsFixed = true;

        // Una fuente de continua SÍ tiene polaridad: es su razón de ser.
        // (FuenteAC la pisa con Fase/Neutro, que es otra cosa distinta.)
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;

        // Valores térmicos típicos de una fuente: cuerpo grande que tarda en
        // calentarse, pero que una vez caliente retiene (y muere a los 90 °C).
        CapacidadTermica = 25;
        ResistenciaTermica = 1;      // = 1/(25 × 0.04): la calibración de siempre, ahora explícita
        MaxTemperature = 90;
        SegundosParaQuemarse = 0.4; // el corto/sobrecarga se "aguanta" un cachito más que en un componente chico, para que se vea el drama
    }

    /// <summary>Fija los voltajes de los bornes: el negativo/neutro es la referencia (0V) del circuito, el positivo al V_efectivo actual (V nominal menos caída interna).</summary>
    public virtual void FixVoltages()
    {
        PinB.Voltage = 0;
        PinA.Voltage = _vEfectivo;
    }

    /// <summary>
    /// Ajusta el V_efectivo para reflejar la caída interna I·R_int bajo carga
    /// actual. Fórmula analítica del divisor: V_ef_new = V·V_ef/(V_ef+|I|·R).
    /// Con I=0 → V_nominal (sin carga, sin caída). Con V_ef→0 → 0 (colapso en
    /// corto). Converge en 1-2 iteraciones incluso con R_int >> R_ext
    /// (supercap descargado directo a pila botón), donde la fórmula lineal
    /// V-I·R oscila.
    /// </summary>
    internal void ActualizarVEfectivo()
    {
        if (IsBurnt) return;
        double iAbs = Math.Abs(CorrienteEntregada);
        double vObjetivo;
        if (EnCortocircuito)
        {
            vObjetivo = 0.0;
        }
        else if (iAbs < 1e-9)
        {
            vObjetivo = Voltage;
        }
        else
        {
            double denom = Math.Abs(_vEfectivo) + iAbs * ResistenciaInternaNominal;
            vObjetivo = Voltage * Math.Abs(_vEfectivo) / Math.Max(denom, 1e-9);
            vObjetivo = Math.Clamp(vObjetivo, 0.0, Voltage);
        }
        // Suavizado 30/70: rápido pero sin bandazos.
        _vEfectivo = _vEfectivo * 0.3 + vObjetivo * 0.7;
    }

    // Encendida: su arista propia es un cable entre dos bornes ya fijos
    // (inofensiva). Apagada: ALTA IMPEDANCIA — sin esto, con los bornes ya
    // sueltos, la arista de 0.001Ω sería un cortocircuito a través del
    // aparato apagado.
    public override double GetResistance() =>
        EstaEncendida ? CircuitConstants.WireResistance : CircuitConstants.OpenResistance;

    /// <summary>
    /// El calor de una fuente no viene de la potencia que ENTREGA (esa se va
    /// al circuito), sino de las pérdidas I²·R en su resistencia interna.
    /// En un cortocircuito la corriente asignada es V/R_interna, así que esta
    /// misma fórmula da V²/R: TODA la potencia descargándose adentro — por
    /// eso una fuente en corto se convierte en una estufa.
    /// </summary>
    public override double PotenciaTermica =>
        IsBurnt ? 0 : LeyesElectricas.PotenciaDesdeIR(CorrienteEntregada, ResistenciaInternaNominal);

    /// <summary>Indica si PostSolve debe evaluar mínimos usando la salida propia o lo hace la subclase.</summary>
    protected virtual bool EvaluarMinimosDesdeSalida => true;

    public override void PostSolve(double deltaTime)
    {
        if (IsBurnt)
        {
            Current = 0;
            Power = 0;
            VoltajeEnBornes = 0;
            CorrienteEntregada = 0;
            LimpiarMinimosOperacion();
            return;
        }

        // La corriente real la asignó CircuitGraph por Kirchhoff antes de
        // llegar acá. Con V_efectivo dinámico, la fuente ya no puede entregar
        // corrientes "inventadas": el solver la limita naturalmente al hundir
        // V_ef bajo carga pesada. El único caso donde CorrienteEntregada
        // vendría de otro lado es el cortocircuito directo (nodos + y − en el
        // mismo grupo fusionado), donde Kirchhoff da 0 pero la física dice
        // que toda la fem se descarga sobre R_interna.
        if (EnCortocircuito)
        {
            CorrienteEntregada = Math.Abs(Voltage) / Math.Max(ResistenciaInternaNominal, 0.05);
        }
        Current = CorrienteEntregada;
        // Con V_efectivo, el voltaje entre bornes SE HUNDE bajo carga
        // automáticamente. En corto colapsa a 0.
        VoltajeEnBornes = EnCortocircuito ? 0 : Math.Abs(PinA.Voltage - PinB.Voltage);
        // Potencia ENTREGADA al circuito: con el voltaje REAL de bornes, no
        // el nominal. Bajo carga es V_ef·I (lo que de verdad recibe la red);
        // en corto es 0 — afuera no llega nada, el drama es todo interno y
        // lo cuenta PotenciaTermica (I²·R_int).
        Power = LeyesElectricas.PotenciaDesdeVI(VoltajeEnBornes, Current);

        ActualizarEstado(deltaTime);

        if (EnCortocircuito)
        {
            // Un corto NO es muerte automática: depende de la fuente. Una 9V
            // (corto de ~5A, tope de 6A) se lo banca — queda EN CORTO en el
            // inspector, convertida en estufa, calentándose hacia su
            // equilibrio térmico. Una pila botón o el regulador del Arduino,
            // cuya corriente de corto SÍ supera su MaxCurrent, muere a los
            // SegundosParaQuemarse — con el motivo que corresponde. Y con
            // MaxCurrent en ∞ (sandbox) nunca muere, coherente con el resto.
            _segundosEnCorto += deltaTime;
            if (_segundosEnCorto >= SegundosParaQuemarse && CorrienteEntregada > MaxCurrent)
            {
                Quemar("cortocircuito");
            }
        }
        else
        {
            _segundosEnCorto = 0;
        }

        if (!IsBurnt)
        {
            if (EvaluarMinimosDesdeSalida)
                EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ChequearQuemado(deltaTime); // límites normales: corriente máx, potencia máx, voltaje máx
        }
    }

    /// <summary>
    /// Una fuente muerta deja de fijar voltajes: sus bornes pasan a ser puntos
    /// libres cualquiera (el circuito queda sin alimentación, como corresponde).
    /// </summary>
    protected override void Quemar()
    {
        base.Quemar();
        CorrienteEntregada = 0;
        PinA.IsFixed = false;
        PinB.IsFixed = false;
        _vEfectivo = 0;
    }

    /// <summary>Al repararse, la fuente vuelve a fijar sus bornes (vuelve a alimentar) y olvida el cortocircuito.</summary>
    protected override void Reparado()
    {
        PinA.IsFixed = true;
        PinB.IsFixed = true;
        EnCortocircuito = false;
        CorrienteEntregada = 0;
        _vEfectivo = Voltage;
        _segundosEnCorto = 0;
    }
}
