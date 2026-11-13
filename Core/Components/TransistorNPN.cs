namespace ElectricSim.Core.Components;

/// <summary>
/// Transistor NPN, modelo de LLAVE ELECTRÓNICA (corte/saturación — el uso
/// digital, el primero que se enseña):
///
///  - Es el PRIMER componente de TRES bornes del proyecto:
///      · PinA  = COLECTOR
///      · PinB  = EMISOR      (la arista principal del solver es C–E)
///      · PinBase = BASE      (el tercer borne, la "palanca")
///  - La base se conecta al grafo mediante una arista interna base→emisor
///    (IComponenteCompuesto): una resistencia fija que hace de unión B-E y
///    nos deja MEDIR el voltaje base-emisor.
///  - Si V(base) − V(emisor) supera el umbral (~0.7V), el canal
///    colector–emisor SE CIERRA (satura, casi un cable). Si no, CORTA
///    (abierto). Con histéresis de 50mV para no titilar en el borde.
///
/// Decidido por tick (regla de oro del solver: la resistencia no cambia
/// DENTRO del tick). Sin zona activa/amplificación: eso es territorio del
/// futuro solver MNA — acá está anotado.
///
/// YA VIVE EN EL CATÁLOGO (categoría Control): ComponenteVisual dibuja sus
/// tres bornes vía IComponenteMultiBorne — colector (PinA, rótulo "C"),
/// emisor (PinB, rótulo "E") y base (PinBase, borne extra rótulo "B"). Fue
/// el Arduino, con sus 22 bornes, el que estrenó ese soporte multi-borne
/// que este transistor ahora reutiliza.
/// </summary>
public class TransistorNPN : Component, IComponenteCompuesto, IComponenteMultiBorne
{
    /// <summary>El tercer borne: la base. (PinA = colector, PinB = emisor.)</summary>
    public Pin PinBase { get; }

    /// <summary>Voltaje base-emisor que abre la llave (~0.7V en silicio real).</summary>
    public double VoltajeUmbralBase { get; set; } = 0.7;

    /// <summary>Resistencia colector-emisor saturado (la llave cerrada).</summary>
    public double ResistenciaSaturacion { get; set; } = 1.0;

    /// <summary>
    /// Resistencia ÓHMICA interna de la base (el piso del modelo de unión).
    /// La unión B-E ya NO es un resistor plano: es un DIODO (modelo compañero,
    /// como el LED) que conduciendo clava V_BE en ~VoltajeUmbralBase. Este
    /// valor solo pone el piso de su resistencia dinámica — la limitadora de
    /// base va AFUERA, como en la vida real. (Antes era un resistor fijo de
    /// 10k: el inspector mostraba V_BE=2.46V, físicamente imposible — QA6.)
    /// </summary>
    public double ResistenciaBase { get; set; } = 100;

    /// <summary>
    /// Resistencia de la unión B-E POR DEBAJO del umbral (Ohms). Un diodo
    /// real bajo el umbral no es infinito: conduce poquito (la cola de la
    /// exponencial). Acá esa fuga cumple además el papel del PULL-DOWN que
    /// los módulos reales traen — sin ella la base queda FLOTANDO y el
    /// transistor se enciende solo con cualquier fuga numérica. En INVERSA
    /// no aplica: ahí la unión bloquea de verdad (bug QA7).
    /// </summary>
    public double ResistenciaFugaBase { get; set; } = 10_000;

    /// <summary>
    /// Corriente de base MÍNIMA (Amps) para saturar. Un bipolar se controla
    /// por CORRIENTE: no alcanza con que la unión llegue a 0.7V, tiene que
    /// entrarle corriente de verdad (I_B ≥ I_C/hFE). Sin este umbral, una
    /// fuga de microamperes —la que deja pasar cualquier llave abierta—
    /// bastaba para clavar la unión en 0.7V y encender la llave sola.
    /// </summary>
    public double CorrienteMinimaBase { get; set; } = 0.0001; // 0.1mA

    /// <summary>True si el canal colector-emisor está conduciendo (saturado).</summary>
    public bool Conduciendo { get; private set; }

    /// <summary>Voltaje base-emisor medido en el último tick (lectura para el inspector).</summary>
    public double VoltajeBaseEmisor { get; private set; }

    /// <summary>Corriente que entra por la base (lectura para el inspector).</summary>
    public double CorrienteBase => _unionBaseEmisor.Current;

    private readonly UnionBaseEmisor _unionBaseEmisor;

    public IEnumerable<Component> AristasInternas => new[] { (Component)_unionBaseEmisor };

    /// <summary>
    /// El borne extra que el usuario puede cablear: la BASE (los otros dos,
    /// colector y emisor, son PinA y PinB). La capa visual lo ubica y lo
    /// rotula con esta etiqueta ("B").
    /// </summary>
    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra => new[] { ("B", PinBase) };

    public TransistorNPN(string name) : base(name)
    {
        // Colector y emisor NO son intercambiables: la juntura tiene sentido.
        // La base sí es "de señal": no es ni + ni −, es la que manda.
        PinA.Polaridad = Polaridad.Positivo;   // colector
        PinB.Polaridad = Polaridad.Negativo;   // emisor
        PinBase = new Pin(this, $"{name}.Base") { Polaridad = Polaridad.Senal };
        _unionBaseEmisor = new UnionBaseEmisor(this);
        MaxPower = 0.6; // como un TO-92 chiquito: la llave también se quema
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
        // Medir V(base) − V(emisor) con los voltajes YA resueltos del tick,
        // y decidir el estado del canal para el PRÓXIMO (con histéresis).
        VoltajeBaseEmisor = PinBase.Voltage - PinB.Voltage;
        double umbral = Conduciendo ? VoltajeUmbralBase - 0.05 : VoltajeUmbralBase;
        // Doble condición, como el bipolar real: la unión tiene que estar
        // polarizada Y tiene que circular corriente de base suficiente
        // (histéresis del 20% en la corriente, para no titilar en el borde).
        double umbralCorriente = Conduciendo ? CorrienteMinimaBase * 0.8 : CorrienteMinimaBase;
        Conduciendo = !IsBurnt && VoltajeBaseEmisor >= umbral
                      && Math.Abs(CorrienteBase) >= umbralCorriente;
    }

    /// <summary>
    /// La arista interna base→emisor: la UNIÓN B-E, modelada como un diodo
    /// con el mismo modelo compañero probado del LED/Diodo. Conduciendo,
    /// apunta su resistencia dinámica a que la caída quede clavada en
    /// VoltajeUmbralBase (~0.7V, la física de una unión de silicio); en
    /// inversa queda ABIERTA (bloquea, como corresponde). Invisible para el
    /// catálogo; muere junto al transistor.
    /// </summary>
    internal class UnionBaseEmisor : Component
    {
        private readonly TransistorNPN _dueno;
        private double _resistenciaDinamica = CircuitConstants.OpenResistance;

        /// <summary>¿La unión está en la rama CONDUCENTE? No se deduce de la resistencia: la rama de fuga (10kΩ) también está por debajo del umbral que se usaba antes.</summary>
        private bool _conduciendo;

        public UnionBaseEmisor(TransistorNPN dueno)
            : base($"{dueno.Name}.UnionBE", dueno.PinBase, dueno.PinB)
        {
            _dueno = dueno;
        }

        public override double GetResistance() =>
            _dueno.IsBurnt ? CircuitConstants.OpenResistance : _resistenciaDinamica;

        protected override void ActualizarEstado(double deltaTime)
        {
            double deltaV = PinA.Voltage - PinB.Voltage; // V_base − V_emisor
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
                // clava V_BE en el umbral.
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
                // Sub-umbral (incluida la base suelta): la cola del diodo, que
                // hace de pull-down y ata la base al emisor.
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
