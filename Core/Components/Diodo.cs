namespace ElectricSim.Core.Components;

/// <summary>
/// Diodo rectificador y diodo Zener — la MISMA clase, porque son el mismo
/// bicho con una diferencia: qué pasa cuando lo polarizás al revés.
///
///  - En DIRECTA (ánodo + arriba del cátodo): los dos conducen igual que el
///    LED, con el modelo de caída constante por resistencia dinámica que ya
///    está probado — converge solo al punto V_diodo = VoltajeUmbral (~0.7V
///    de silicio), que es la fórmula del apunte.
///
///  - En INVERSA: el diodo COMÚN bloquea; si lo forzás más allá de
///    MaxVoltajeInverso sostenido, se perfora y muere (avalancha
///    destructiva, como el 1N4007 real más allá de sus 1000V).
///    El ZENER, en cambio, tiene la ruptura DOMESTICADA: al llegar a su
///    VoltajeZener conduce al revés manteniendo esa caída clavada — y de
///    eso vive: es el regulador de tensión más simple que existe. Un Zener
///    no muere por ruptura (es su trabajo); muere por POTENCIA, si te
///    olvidás su resistencia limitadora.
///
/// La diferencia entre ambos es un solo número: VoltajeZener = ∞ para el
/// común (nunca regula, solo revienta), finito para el Zener (regula ahí, y
/// MaxVoltajeInverso pasa a ∞ porque la ruptura dejó de ser una falla).
/// Las dos entradas del catálogo son dos fábricas de esta misma clase.
///
/// CON ALTERNA (nota honesta, documentada): este modelo estático no ve la
/// onda — el RMS que empuja la fuente es un valor constante positivo, así
/// que el diodo conduce como si fuera DC de ese valor. La rectificación de
/// media onda DE VERDAD (recortar los semiciclos negativos) necesita
/// simular la forma de onda, que no existe a propósito. Para convertir
/// AC→DC en el simulador, el camino es el Puente Rectificador.
/// </summary>
public class Diodo : Component
{
    /// <summary>Caída directa (Volts): el umbral para conducir Y la caída que mantiene conduciendo. ~0.7V en silicio.</summary>
    public double VoltajeUmbral { get; set; } = 0.7;

    /// <summary>Resistencia mínima conduciendo (el "piso" de la resistencia dinámica): la resistencia interna real del diodo.</summary>
    public double ResistenciaEncendido { get; set; } = 2;

    /// <summary>
    /// Voltaje de ruptura CONTROLADA en inversa. ∞ = diodo común (no regula
    /// nunca). Finito = es un Zener: a partir de ese voltaje inverso conduce
    /// al revés manteniendo la caída clavada en este valor.
    /// </summary>
    public double VoltajeZener { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Voltaje de ruptura del diodo COMÚN: más allá, entra en AVALANCHA
    /// (conduce al revés con esta caída clavada, como un Zener fuera de
    /// especificación). Muere por la POTENCIA de esa avalancha, no por el
    /// voltaje en sí. El Zener lo tiene en ∞ (su ruptura es VoltajeZener).
    /// </summary>
    public double MaxVoltajeInverso { get; set; } = 100;

    /// <summary>True si en este momento conduce en directa (ánodo→cátodo).</summary>
    public bool ConduciendoDirecta { get; private set; }

    /// <summary>
    /// True si está regulando en inversa DE VERDAD: no alcanza con estar en la
    /// rama Zener, la caída tiene que estar efectivamente clavada en
    /// VoltajeZener (±0.1V). Antes esto era un flag de estado a secas y el
    /// inspector anunciaba "REGULANDO" durante los 2 segundos en que la
    /// caída real todavía era de 8.99V — un componente cuya única función es
    /// clavar un voltaje no puede mentir sobre si lo está clavando (A7).
    /// </summary>
    public bool RegulandoZener =>
        _enRamaZener && Math.Abs(VoltajeEnBornes - VoltajeZener) < 0.1;

    /// <summary>Estado interno: el solver está resolviendo por la rama Zener. Es lo que RegulandoZener era antes.</summary>
    private bool _enRamaZener;

    /// <summary>True si un diodo COMÚN está conduciendo en ruptura inversa (avalancha, fuera de especificación).</summary>
    public bool EnAvalancha { get; private set; }

    /// <summary>¿Esta instancia es un Zener? (azúcar para el inspector).</summary>
    public bool EsZener => !double.IsPositiveInfinity(VoltajeZener);

    private double _resistenciaDinamica = CircuitConstants.OpenResistance;

    public Diodo(string name, double voltajeUmbral = 0.7) : this(name, null, null, voltajeUmbral) { }

    /// <summary>
    /// Constructor para diodos que viven ADENTRO de otro componente y comparten
    /// sus bornes — el diodo de cuerpo del MOSFET, por ejemplo. PinA y PinB son
    /// de solo lectura, así que los pines tienen que ir por el constructor base.
    /// </summary>
    public Diodo(string name, Pin? pinA, Pin? pinB, double voltajeUmbral = 0.7)
        : base(name, pinA, pinB)
    {        // Polaridad REAL: conectarlo al revés cambia lo que hace.
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;

        VoltajeUmbral = voltajeUmbral;
        MaxCurrent = 1.0;   // como un 1N4007: 1A continuo
        MaxPower = 1.5;
        CapacidadTermica = 0.3;
        CoeficienteEnfriamiento = 0.06;
        MaxTemperature = 150; // los rectificadores aguantan caldo
    }

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;
        return _resistenciaDinamica;
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        double deltaV = PinA.Voltage - PinB.Voltage; // + = directa (ánodo arriba)

        // Las TRES ramas conducentes usan el mismo criterio: se ENTRA por
        // voltaje y se SIGUE por corriente. Antes la directa y la Zener
        // entraban y seguían por voltaje con histéresis de 50mV, lo que hacía
        // imposible sembrar el piso (con la R en el piso la caída se desploma
        // abajo del umbral y el diodo se apagaría al tick siguiente).
        bool entraEnDirecta = !ConduciendoDirecta && deltaV >= VoltajeUmbral;
        bool sigueEnDirecta = ConduciendoDirecta && Current > 1e-5;

        bool entraEnZener = EsZener && !_enRamaZener && deltaV <= -VoltajeZener;
        bool sigueEnZener = _enRamaZener && Current < 0 && Math.Abs(Current) > 1e-5;

        // --- INVERSA-AVALANCHA (el común) ---
        // La permanencia se juzga por CORRIENTE, no por voltaje: una vez que
        // conduce, la caída ES el V de ruptura y comparar el ΔV instantáneo
        // contra el umbral lo hacía salir y entrar en bucle mientras el modelo
        // compañero convergía. Mientras siga circulando corriente inversa
        // apreciable, la avalancha está en curso. (Mismo criterio que usa el
        // optoacoplador para su acople.)
        bool entraEnAvalancha = !EsZener && !double.IsPositiveInfinity(MaxVoltajeInverso)
                                && deltaV <= -MaxVoltajeInverso;
        bool sigueEnAvalancha = EnAvalancha && Current < 0 && Math.Abs(Current) > 1e-5;

        if (entraEnDirecta || sigueEnDirecta)
        {
            // ARRANQUE RÁPIDO: en el tick en que ENTRA, sembrar el piso y
            // saltear el modelo compañero (idéntico a la rama de avalancha).
            _resistenciaDinamica = ConduciendoDirecta
                ? ResistenciaObjetivo(VoltajeUmbral)
                : Math.Max(ResistenciaEncendido, CircuitConstants.MinResistance);
            ConduciendoDirecta = true;
            _enRamaZener = false;
            EnAvalancha = false;
        }
        else if (entraEnZener || sigueEnZener)
        {
            // Ruptura domesticada: conduce al revés apuntando a que la caída
            // inversa quede clavada en VoltajeZener. La contracción del
            // modelo compañero converge igual que en directa — y con la
            // siembra del piso llega en 7 ticks en vez de 20 (A7).
            _resistenciaDinamica = _enRamaZener
                ? ResistenciaObjetivo(VoltajeZener)
                : Math.Max(ResistenciaEncendido, CircuitConstants.MinResistance);
            _enRamaZener = true;
            ConduciendoDirecta = false;
            EnAvalancha = false;
        }
        else if (entraEnAvalancha || sigueEnAvalancha)
        {
            // AVALANCHA del diodo común: pasado el V de ruptura, CONDUCE al
            // revés con la caída clavada en MaxVoltajeInverso — exactamente
            // la mecánica del Zener, pero fuera de especificación. La muerte
            // NO es por el voltaje: es por la POTENCIA de la avalancha
            // (V_ruptura × I). Con la corriente limitada a µA/mA (detrás de
            // una resistencia grande) el diodo aguanta, como el 1N4007 real
            // en avalancha controlada; forzado en directo desde la fuente,
            // los amperes lo revientan por MaxPower/MaxCurrent en
            // SegundosParaQuemarse. (Antes moría siempre por un contador de
            // "voltaje inverso" aunque circularan microamperes — QA5b.)
            // ARRANQUE RÁPIDO: en el tick en que ENTRA en avalancha, sembrar la
            // resistencia en su piso y saltear el modelo compañero (que todavía
            // vería la corriente calculada con el 1MΩ de "abierto" y no serviría
            // de nada). Desde el piso, la contracción converge en pocos ticks
            // subiendo; partiendo del 1MΩ tardaba ~34 ticks (3.4s), y una
            // ruptura destructiva no puede demorar segundos en manifestarse.
            ConduciendoDirecta = false;
            _enRamaZener = false;
            _resistenciaDinamica = EnAvalancha
                ? ResistenciaObjetivo(MaxVoltajeInverso)
                : Math.Max(ResistenciaEncendido, CircuitConstants.MinResistance);
            EnAvalancha = true;
        }
        else
        {
            ConduciendoDirecta = false;
            _enRamaZener = false;
            EnAvalancha = false;
            _resistenciaDinamica = CircuitConstants.OpenResistance;
        }
    }

    /// <summary>La regla del modelo compañero: la resistencia que dejaría caer exactamente `caidaObjetivo` con la corriente de este tick.</summary>
    private double ResistenciaObjetivo(double caidaObjetivo)
    {
        double corriente = Math.Abs(Current);
        double objetivo = corriente > 1e-9
            ? caidaObjetivo / corriente
            : CircuitConstants.OpenResistance;
        return Math.Clamp(objetivo, ResistenciaEncendido, CircuitConstants.OpenResistance);
    }

    protected override void Quemar()
    {
        base.Quemar();
        ConduciendoDirecta = false;
        _enRamaZener = false;
        EnAvalancha = false;
        _resistenciaDinamica = CircuitConstants.OpenResistance;
    }

    protected override void Reparado()
    {
        _resistenciaDinamica = CircuitConstants.OpenResistance;
        ConduciendoDirecta = false;
        _enRamaZener = false;
        EnAvalancha = false;
    }
}
