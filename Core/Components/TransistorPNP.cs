namespace ElectricSim.Core.Components;

/// <summary>
/// Transistor PNP: el ESPEJO del NPN. Misma llave electrónica de tres
/// bornes, pero al revés en todo:
///
///  - El NPN se enciende TIRANDO LA BASE PARA ARRIBA (V_BE ≥ 0.7): la llave
///    del lado de abajo (low-side).
///  - El PNP se enciende TIRANDO LA BASE PARA ABAJO: conduce cuando el
///    EMISOR queda ~0.7V por ENCIMA de la base (V_EB ≥ 0.7). Por eso es la
///    llave del lado de arriba (high-side): emisor al +, carga del colector
///    a masa, y un cablecito de la base a masa lo enciende.
///
/// Mismos bornes y misma mecánica del NPN (PinA = COLECTOR, PinB = EMISOR,
/// PinBase = BASE; arista interna emisor–base; corte/saturación con
/// histéresis, decidido por tick). La diferencia entera es el SIGNO de la
/// condición de encendido.
/// </summary>
public class TransistorPNP : Component, IComponenteCompuesto, IComponenteMultiBorne
{
    /// <summary>El tercer borne: la base. (PinA = colector, PinB = emisor.)</summary>
    public Pin PinBase { get; }

    /// <summary>Voltaje emisor-base que abre la llave (~0.7V en silicio). El emisor tiene que quedar por ENCIMA de la base.</summary>
    public double VoltajeUmbralBase { get; set; } = 0.7;

    /// <summary>Resistencia emisor-colector saturado (la llave cerrada).</summary>
    public double ResistenciaSaturacion { get; set; } = 1.0;

    /// <summary>
    /// Resistencia ÓHMICA interna de la base (el piso del modelo de unión).
    /// La unión E-B ya NO es un resistor plano: es un DIODO (modelo compañero,
    /// el espejo del NPN) que conduciendo clava V_EB en ~VoltajeUmbralBase.
    /// La limitadora de base va AFUERA, como en la vida real. (Antes era un
    /// resistor fijo de 10k — mismo bug QA6 del NPN.)
    /// </summary>
    public double ResistenciaBase { get; set; } = 100;

    /// <summary>
    /// Resistencia de la unión E-B por debajo del umbral (Ohms): la cola del
    /// diodo, que además hace de PULL-UP y ata la base al emisor cuando queda
    /// suelta. En inversa la unión bloquea de verdad. El espejo del NPN.
    /// </summary>
    public double ResistenciaFugaBase { get; set; } = 10_000;

    /// <summary>
    /// Corriente de base MÍNIMA (Amps) para saturar. Un bipolar se controla
    /// por CORRIENTE: no alcanza con que la unión llegue a 0.7V. Sin este
    /// umbral, la fuga de microamperes de una llave abierta encendía la
    /// llave sola.
    /// </summary>
    public double CorrienteMinimaBase { get; set; } = 0.0001; // 0.1mA

    /// <summary>True si el canal emisor-colector está conduciendo (saturado).</summary>
    public bool Conduciendo { get; private set; }

    /// <summary>Voltaje EMISOR menos BASE del último tick (positivo = encendiéndose). El espejo del V_BE del NPN.</summary>
    public double VoltajeEmisorBase { get; private set; }

    /// <summary>Corriente de la unión emisor-base (lectura para el inspector).</summary>
    public double CorrienteBase => _unionEmisorBase.Current;

    private readonly UnionEmisorBase _unionEmisorBase;

    public IEnumerable<Component> AristasInternas => new[] { (Component)_unionEmisorBase };

    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra => new[] { ("B", PinBase) };

    public TransistorPNP(string name) : base(name)
    {
        // En el PNP el EMISOR va al positivo: es el espejo del NPN.
        PinA.Polaridad = Polaridad.Negativo;   // colector
        PinB.Polaridad = Polaridad.Positivo;   // emisor
        PinBase = new Pin(this, $"{name}.Base") { Polaridad = Polaridad.Senal };
        _unionEmisorBase = new UnionEmisorBase(this);
        MaxPower = 0.6; // TO-92, como su hermano NPN
        ResistenciaTermica = 200; // TO-92: plástico chiquito, disipa mal
    }

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;
        return Conduciendo ? Math.Max(ResistenciaSaturacion, CircuitConstants.MinResistance)
                           : CircuitConstants.OpenResistance;
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        // El espejo exacto del NPN: acá manda V(emisor) − V(base).
        VoltajeEmisorBase = PinB.Voltage - PinBase.Voltage;
        double umbral = Conduciendo ? VoltajeUmbralBase - 0.05 : VoltajeUmbralBase;
        // Doble condición, como el bipolar real: la unión tiene que estar
        // polarizada Y tiene que circular corriente de base suficiente
        // (histéresis del 20% en la corriente, para no titilar en el borde).
        double umbralCorriente = Conduciendo ? CorrienteMinimaBase * 0.8 : CorrienteMinimaBase;
        Conduciendo = !IsBurnt && VoltajeEmisorBase >= umbral
                      && Math.Abs(CorrienteBase) >= umbralCorriente;
    }

    /// <summary>
    /// La arista interna emisor→base: la UNIÓN E-B por la que la corriente de
    /// control SALE del transistor hacia masa. Modelada como diodo (modelo
    /// compañero, el espejo del NPN): conduciendo clava la caída en
    /// VoltajeUmbralBase; en inversa bloquea.
    /// </summary>
    internal class UnionEmisorBase : Component
    {
        private readonly TransistorPNP _dueno;
        private double _resistenciaDinamica = CircuitConstants.OpenResistance;

        /// <summary>¿La unión está en la rama CONDUCENTE? No se deduce de la resistencia: la rama de fuga (10kΩ) también está por debajo del umbral que se usaba antes.</summary>
        private bool _conduciendo;

        public UnionEmisorBase(TransistorPNP dueno)
            : base($"{dueno.Name}.UnionEB", dueno.PinB, dueno.PinBase)
        {
            _dueno = dueno;
        }

        public override double GetResistance() =>
            _dueno.IsBurnt ? CircuitConstants.OpenResistance : _resistenciaDinamica;

        protected override void ActualizarEstado(double deltaTime)
        {
            double deltaV = PinA.Voltage - PinB.Voltage; // V_emisor − V_base
            // El estado de conducción vive en su propio flag y no se deduce
            // de la resistencia: la rama de FUGA (10kΩ) también cae por debajo
            // del 0.5·OpenResistance, así que el viejo cálculo daba
            // "conduciendo = true" en sub-umbral. Se ENTRA por voltaje y se
            // SIGUE por corriente, igual que el LED y el diodo.
            bool entraEnDirecta = !_conduciendo && deltaV >= _dueno.VoltajeUmbralBase;
            bool sigueEnDirecta = _conduciendo && Current > 1e-5;

            if (entraEnDirecta || sigueEnDirecta)
            {
                // ARRANQUE: en el tick de entrada se siembra el piso y se
                // saltea el modelo compañero. Después, compañero de siempre:
                // clava V_EB en el umbral.
                double piso = Math.Max(_dueno.ResistenciaBase, CircuitConstants.MinResistance);
                if (!_conduciendo)
                {
                    _resistenciaDinamica = piso;
                }
                else
                {
                    double corriente = Math.Abs(Current);
                    double objetivo = corriente > 1e-9
                        ? _dueno.VoltajeUmbralBase / corriente
                        : CircuitConstants.OpenResistance;
                    _resistenciaDinamica = Math.Clamp(
                        objetivo, piso, CircuitConstants.OpenResistance);
                }
                _conduciendo = true;
            }
            else if (deltaV >= 0)
            {
                // Sub-umbral (incluida la base suelta): pull-up hacia el emisor.
                _conduciendo = false;
                _resistenciaDinamica = Math.Max(_dueno.ResistenciaFugaBase, CircuitConstants.MinResistance);
            }
            else
            {
                _conduciendo = false;
                _resistenciaDinamica = CircuitConstants.OpenResistance; // INVERSA: bloquea
            }
        }

        protected override void Reparado()
        {
            _resistenciaDinamica = CircuitConstants.OpenResistance;
            _conduciendo = false;
        }
    }
}
