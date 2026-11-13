namespace ElectricSim.Core.Components;

/// <summary>
/// Potenciómetro de TRES bornes, que es lo que un potenciómetro es:
///
///     PinA ──[ R·(1−f) ]── PinCursor ──[ R·f ]── PinB
///
/// El viejo tenía dos bornes: **era un reóstato**, y encima duplicado con
/// `Reostato.cs` — dos componentes para la misma función y ninguno para la
/// que faltaba (A10).
///
/// Con tres bornes se puede hacer lo que se enseña el 90% de las veces con
/// un potenciómetro, que es el DIVISOR DE TENSIÓN: conectás A y B a la
/// fuente y sacás una fracción del voltaje por el cursor. Control de
/// volumen, entrada analógica del Arduino, sensor de posición. Y sigue
/// sirviendo de reóstato: usás solo A y el cursor y te olvidás de B.
///
/// La estructura es la misma que `TransistorNPN` con su unión base-emisor:
/// el componente ES el tramo A→cursor, y aporta el tramo cursor→B como
/// arista interna. Así no hay ninguna rama fantasma A→B en paralelo con la
/// pista.
/// </summary>
public class Potenciometro : Component, IComponenteCompuesto, IComponenteMultiBorne
{
    /// <summary>
    /// El CURSOR (wiper): el borne del medio. ES el `PinB` heredado, no un pin
    /// aparte — porque la arista propia del componente es PinA↔PinB, y esa
    /// arista tiene que ser el tramo A→CURSOR. Ponerlo al revés (cursor en un
    /// pin nuevo y extremo B en el heredado) deja el cursor colgando de un
    /// extremo en vez de en el medio, y el divisor de tensión NO funciona.
    /// </summary>
    public Pin PinCursor => PinB;

    /// <summary>El otro extremo de la pista. Va conectado al cursor por la arista interna.</summary>
    public Pin PinExtremoB { get; }

    /// <summary>
    /// Dónde está el cursor. Está definida por lo ÚTIL: es directamente qué
    /// porcentaje del voltaje sale por el cursor cuando se usa de divisor.
    /// 0% = cursor pegado al extremo B (saca 0 V) · 100% = pegado al extremo
    /// A (saca todo). Como reóstato: 0% deja toda la pista en serie, 100% la
    /// saca del camino.
    /// </summary>
    public double Perilla { get; set; } = 50;

    /// <summary>Resistencia TOTAL de la pista, de extremo a extremo. Es LA especificación de un potenciómetro ("uno de 10k").</summary>
    public double ResistenciaTotal { get; set; } = 10_000;

    /// <summary>Resistencia de contacto: lo que queda aunque el cursor esté en la punta. Nunca es cero.</summary>
    public double ResistenciaContacto { get; set; } = 1;

    /// <summary>
    /// Pista LOGARÍTMICA (como los de audio) o LINEAL. Para un divisor de
    /// tensión didáctico la lineal es la que se entiende; para un control de
    /// volumen, la log.
    /// </summary>
    public bool EsLogaritmico { get; set; }

    private readonly TramoCursorB _tramoB;

    public Potenciometro(string name, double resistenciaTotal = 10_000) : base(name)
    {
        ResistenciaTotal = resistenciaTotal;
        MaxPower = 0.5; // un potenciómetro chico real: de dimmer de estufa no sirve
        // Los dos extremos de la pista son intercambiables, y el cursor tampoco
        // tiene polaridad: da igual para qué lado lo conectes.
        PinExtremoB = new Pin(this, $"{name}.B") { Polaridad = Polaridad.Ninguna };
        _tramoB = new TramoCursorB(this);
    }

    /// <summary>La fracción del recorrido, 0..1, con la curva de la pista aplicada.</summary>
    public double Fraccion
    {
        get
        {
            double f = Math.Clamp(Perilla / 100.0, 0, 1);
            if (!EsLogaritmico) return f;
            // Log: cada tramo del giro multiplica por el mismo factor, así el
            // volumen (o el brillo) cambia parejo al oído/al ojo.
            const double decadas = 3;
            return f <= 0 ? 0 : Math.Pow(10, decadas * (f - 1));
        }
    }

    /// <summary>Resistencia del tramo A→cursor: R·(1−f).</summary>
    public double ResistenciaTramoA =>
        Math.Max(ResistenciaTotal * (1 - Fraccion), ResistenciaContacto);

    /// <summary>Resistencia del tramo cursor→B: R·f.</summary>
    public double ResistenciaTramoB =>
        Math.Max(ResistenciaTotal * Fraccion, ResistenciaContacto);

    /// <summary>
    /// La fracción del voltaje que el cursor entrega, SI está usado como
    /// divisor. Es el número que el alumno viene a buscar.
    /// </summary>
    public double FraccionDeVoltaje
    {
        get
        {
            double total = ResistenciaTramoA + ResistenciaTramoB;
            return total <= 0 ? 0 : ResistenciaTramoB / total;
        }
    }

    /// <summary>Voltaje que hay en el cursor respecto del extremo B.</summary>
    public double VoltajeCursor => PinCursor.Voltage - PinExtremoB.Voltage;

    public IEnumerable<Component> AristasInternas { get { yield return _tramoB; } }

    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra
    {
        get { yield return ("B", PinExtremoB); }
    }

    public override double GetResistance()
    {
        _tramoB.SincronizarConDueno();
        return IsBurnt ? CircuitConstants.OpenResistance : Math.Max(ResistenciaTramoA, CircuitConstants.MinResistance);
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        _tramoB.SincronizarConDueno();
    }

    protected override void Quemar()
    {
        base.Quemar();
        _tramoB.QuemarPorPista(MotivoQuemado);
    }

    internal void QuemarPorTramoB(string motivo) =>
        Quemar(string.IsNullOrEmpty(motivo) ? "sobrecarga" : motivo);

    protected override void Reparado()
    {
        _tramoB.Reparar(Temperatura);
    }

    /// <summary>
    /// El tramo cursor→B de la pista. Es la MISMA pista de carbón que el
    /// tramo A→cursor, así que comparte límites y muere con ella.
    /// </summary>
    private sealed class TramoCursorB : Component
    {
        private readonly Potenciometro _dueno;

        public TramoCursorB(Potenciometro dueno)
            : base($"{dueno.Name}.W-B", dueno.PinCursor, dueno.PinExtremoB)
        {
            _dueno = dueno;
            SincronizarConDueno();
        }

        internal void SincronizarConDueno()
        {
            MaxVoltage = _dueno.MaxVoltage;
            MaxCurrent = _dueno.MaxCurrent;
            MaxPower = _dueno.MaxPower;
            MaxTemperature = _dueno.MaxTemperature;
            SegundosParaQuemarse = _dueno.SegundosParaQuemarse;
            CapacidadTermica = _dueno.CapacidadTermica;
            ResistenciaTermica = _dueno.ResistenciaTermica;
            PosicionFisica = _dueno.PosicionFisica;
        }

        public override void PostSolve(double deltaTime)
        {
            SincronizarConDueno();
            base.PostSolve(deltaTime);
        }

        public override double GetResistance()
        {
            SincronizarConDueno();
            return _dueno.IsBurnt || IsBurnt
                ? CircuitConstants.OpenResistance
                : Math.Max(_dueno.ResistenciaTramoB, CircuitConstants.MinResistance);
        }

        protected override void Quemar()
        {
            base.Quemar();
            if (!_dueno.IsBurnt) _dueno.QuemarPorTramoB(MotivoQuemado);
        }

        internal void QuemarPorPista(string motivo) =>
            Quemar(string.IsNullOrEmpty(motivo) ? "sobrecarga" : motivo);
    }
}
