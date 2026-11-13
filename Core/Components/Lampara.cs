namespace ElectricSim.Core.Components;

/// <summary>
/// Lámpara incandescente chica: eléctricamente es una resistencia fija, pero
/// con dos comportamientos propios que la hacen la estrella del sistema
/// térmico:
///
///  - Necesita una POTENCIA MÍNIMA para emitir luz ("necesita un mínimo para
///    funcionar"): por debajo de PotenciaMinima conduce igual, pero no brilla.
///  - Casi toda su potencia se vuelve CALOR (FraccionCalorDisipado alta):
///    es el componente ideal para ver cómo un objeto caliente le contagia
///    temperatura a los vecinos cercanos y cómo se enfría al cortarle la
///    corriente.
///
/// A diferencia del LED, funciona igual con continua o alterna (un filamento
/// no distingue el sentido de la corriente) — el contraste educativo entre
/// LED y lámpara frente a una FuenteAC es a propósito.
///
/// [P10-3] Y su resistencia SUBE CON LA TEMPERATURA, que es de donde sale el
/// PICO DE ARRANQUE de 10× que tiene una lámpara de verdad. Ese pico es la
/// mejor demostración de por qué el simulador tiene sistema térmico: sin
/// calor, no se puede modelar. La `Resistencia` del catálogo es la de TRABAJO
/// (caliente); en frío el filamento vale ~10 veces menos.
/// </summary>
public class Lampara : Component, IComponenteLuminoso
{
    /// <summary>Color personalizado (RGB 0..1) para el bulbo y la luz emitida; null = cálido predeterminado.</summary>
    public ColorLuzRgb? ColorPersonalizado { get; set; }

    /// <summary>Resistencia del filamento CALIENTE, o sea trabajando (Ohms). Es el número del catálogo.</summary>
    public double Resistencia { get; set; }

    /// <summary>Cuántas veces más resistencia tiene caliente que fría. Tungsteno ≈ 10.</summary>
    public double RelacionFrioCaliente { get; set; } = 10;

    /// <summary>Temperatura de trabajo del filamento (°C). Uno de tungsteno anda al rojo blanco.</summary>
    public double TemperaturaNominal { get; set; } = 2000;

    /// <summary>Lo que tarda el filamento en llegar a régimen. Un pelo de tungsteno no tiene masa: decenas de ms.</summary>
    public double SegundosCalentamiento { get; set; } = 0.05;

    /// <summary>Resistencia con el filamento frío: es la que hace el pico de arranque.</summary>
    public double ResistenciaFria => Resistencia / Math.Max(RelacionFrioCaliente, 1);

    /// <summary>α del filamento. Se DEDUCE para que a TemperaturaNominal la resistencia sea la del catálogo.</summary>
    public double CoefTemperatura =>
        (RelacionFrioCaliente - 1) / Math.Max(TemperaturaNominal - CircuitConstants.TemperaturaAmbienteDefault, 1);

    /// <summary>
    /// La temperatura donde R(T) y P(T) se hacen consistentes con el voltaje
    /// que tiene puesto AHORA: a dónde va a terminar el filamento.
    ///
    /// Hace falta porque el lazo R(T)→I→P→T→R sólo se cierra ENTRE ticks: en
    /// el primer tick la lámpara está fría, chupa 10×, y el equilibrio de ESE
    /// punto de operación son ~19.000°C. Con un tick de 100ms el filamento se
    /// "fundiría" al prenderla, cosa que no pasa en la realidad porque su
    /// resistencia sube en 30ms. Resolviendo el punto de trabajo de una, el
    /// régimen queda exacto, el pico de arranque se sigue viendo si el tick es
    /// fino, y con ticks gruesos simplemente converge más rápido.
    /// </summary>
    public double TemperaturaEquilibrio
    {
        get
        {
            double ambiente = SimuladorTermico.AmbienteActual;
            if (VoltajeEnBornes <= 1e-9) return ambiente;

            double t = Temperatura;
            for (int i = 0; i < 24; i++)
            {
                double r = Math.Clamp(
                    ResistenciaFria * (1 + CoefTemperatura * (t - ambiente)),
                    ResistenciaFria, Resistencia * 1.5);
                double p = VoltajeEnBornes * VoltajeEnBornes / Math.Max(r, CircuitConstants.MinResistance)
                           * FraccionCalorDisipado;
                double siguiente = ambiente + p * ResistenciaTermica;
                if (Math.Abs(siguiente - t) < 0.5) return siguiente;
                t = 0.5 * t + 0.5 * siguiente;
            }
            return t;
        }
    }

    public override double TechoTermico => TemperaturaEquilibrio;

    /// <summary>Qué fracción del salto filamento→ambiente llega al VIDRIO (~10%: un foco de 2000°C de filamento tiene ~200°C de bulbo).</summary>
    public double FraccionVidrio { get; set; } = 0.1;

    /// <summary>
    /// Lo que el componente de AL LADO percibe: el vidrio, no el filamento.
    /// Con el filamento a ~1900°C el contagio le pasaba ~19W a una resistencia
    /// pegada y la quemaba en segundos, y de paso le enfriaba el filamento a
    /// la lámpara y le cambiaba su corriente.
    /// </summary>
    public override double TemperaturaParaContagio =>
        SimuladorTermico.AmbienteActual + FraccionVidrio * (Temperatura - SimuladorTermico.AmbienteActual);

    public override bool AbsorbeContagio => false;

    private double _dtCalibrado = double.NaN;
    private bool _limitesFijados;

    /// <summary>Potencia (Watts) a la que la lámpara brilla al máximo (Brillo = 1).</summary>
    public double PotenciaNominal { get; set; } = 2.4;

    /// <summary>Potencia mínima (Watts) para que el filamento empiece a dar luz visible.</summary>
    public double PotenciaMinima { get; set; } = 0.05;

    /// <summary>0 = apagada, 1 = brillo máximo.</summary>
    public double Brillo { get; private set; }

    public bool EstaEncendida => Brillo > 0;

    public Lampara(string name, double resistenciaOhms = 60) : base(name)
    {
        Resistencia = resistenciaOhms;
        FraccionCalorDisipado = 0.9;  // una incandescente es 90% estufa, 10% luz
        CapacidadTermica = 0.2;       // filamento + ampolla chica: calienta rápido y en serio
        CoeficienteEnfriamiento = 0.06;
        MaxTemperature = 300;
        // Con R(T) la lámpara NO muere por watts por defecto: muere cuando el filamento
        // FUNDE por temperatura, salvo que el usuario fije un máximo de watts explícito.
        MaxPower = double.PositiveInfinity;
    }

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;

        // Sin sistema térmico no hay temperatura que seguir: la lámpara se
        // quedaría FRÍA para siempre chupando 10×. Si la térmica está apagada
        // (el botón de la barra), lo honesto es asumir el régimen.
        if (!SimuladorTermico.Habilitada) return Resistencia;

        double r = ResistenciaFria * (1 + CoefTemperatura
            * (Temperatura - SimuladorTermico.AmbienteActual));

        // COTA a la realimentación R(T)→I→P→T→R: el filamento se funde mucho
        // antes de que su resistencia pase de ~1.5× la nominal, así que
        // acotarla ahí es física, no un parche numérico — y de paso mata
        // cualquier oscilación del lazo con ticks lentos.
        return Math.Clamp(r, ResistenciaFria, Resistencia * 1.5);
    }

    /// <summary>
    /// Deja la térmica consistente con la eléctrica: con PotenciaNominal, el
    /// filamento tiene que quedar EXACTAMENTE en TemperaturaNominal, que es
    /// donde su resistencia vale la del catálogo.
    /// </summary>
    private void Calibrar(double deltaTime)
    {
        // Los límites se fijan UNA sola vez. Antes se recalculaban cada vez que
        // cambiaba el deltaTime — o sea cada vez que el usuario movía el slider
        // de ticks/s — y eso le borraba en silencio los límites que hubiera
        // editado en el inspector.
        if (!_limitesFijados)
        {
            ResistenciaTermica = (TemperaturaNominal - CircuitConstants.TemperaturaAmbienteDefault)
                / Math.Max(PotenciaNominal * FraccionCalorDisipado, 1e-6);
            MaxTemperature = TemperaturaNominal * 1.4;   // margen antes de fundirse
            _limitesFijados = true;
        }

        // Esto SÍ depende del tick y se recalcula siempre: τ = C·R_termica, y
        // se pide τ ≥ 3 ticks para que el integrador térmico tenga con qué
        // resolver el transitorio del filamento.
        double tau = Math.Max(SegundosCalentamiento, 3 * deltaTime);
        CapacidadTermica = Math.Max(tau / Math.Max(ResistenciaTermica, 1e-6), 1e-4);
        _dtCalibrado = deltaTime;
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        if (_dtCalibrado != deltaTime) Calibrar(deltaTime);
        bool cumpleMinimos = EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
        Brillo = !cumpleMinimos || Power < PotenciaMinima
            ? 0
            : Math.Clamp(Power / Math.Max(PotenciaNominal, 1e-6), 0, 1);
    }

    protected override void Quemar()
    {
        base.Quemar();
        Brillo = 0;
    }

    protected override void Reparado() => Brillo = 0;
}
