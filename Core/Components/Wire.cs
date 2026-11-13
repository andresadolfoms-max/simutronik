namespace ElectricSim.Core.Components;

/// <summary>
/// Un cable conecta dos bornes que YA EXISTEN (de otros componentes, o puntos
/// de unión libres). No tiene bornes propios: a diferencia de una Resistencia
/// o un LED, el cable no "es" un objeto físico nuevo, solo une dos puntos.
///
/// Por defecto es una "conexión perfecta": sus dos puntas se fusionan en un
/// solo nodo eléctrico (Union-Find) y no cae voltaje en él. PERO su
/// resistencia es editable desde el inspector: si el usuario la sube por
/// encima del umbral (CircuitConstants.UmbralConexionPerfecta), el cable pasa
/// a ser una arista real del solver — cae voltaje, disipa potencia y genera
/// calor, como un cable finito de verdad.
///
/// Sus límites arrancan en 999 para todo (decisión del proyecto: que los
/// cables no molesten mientras armás el circuito), pero son editables igual
/// que cualquier estadística.
/// </summary>
public class Wire : Component
{
    /// <summary>Resistencia del cable (Ohms). Por debajo del umbral se comporta como conexión perfecta.</summary>
    public double Resistencia { get; set; } = CircuitConstants.WireResistance;

    /// <summary>Largo aproximado del cable en el mundo (metros). Lo escribe la capa visual; es informativo.</summary>
    public double Longitud { get; set; }

    /// <summary>
    /// True = alambre PELADO, sin aislante. Hoy es solo la marca (y el look
    /// que tendrá en la capa visual); la mecánica de "conduce AL CONTACTO"
    /// (dos alambres desnudos que se tocan quedan conectados, con sus
    /// cortocircuitos accidentales y todo) es física de colisiones de la
    /// capa Game — está en el PLAN, y el motor ya está listo para recibirla:
    /// alcanza con llamar CircuitGraph.Conectar entre los puntos de contacto.
    /// </summary>
    public bool SinAislante { get; set; }

    /// <summary>
    /// Grosor (diámetro) del conductor en milímetros. Solo lo usa el CABLE
    /// FÍSICO de la capa visual: de acá salen su resistencia (ρ·L/A), su
    /// ampacidad (cuánta corriente aguanta antes de quemarse) y su masa
    /// térmica. En el cable simple es decorativo.
    /// </summary>
    public double GrosorMm { get; set; } = 1.5;

    /// <summary>Resistividad del cobre a temperatura ambiente (Ω·m).</summary>
    public const double ResistividadCobre = 1.68e-8;

    /// <summary>
    /// Resistencia REAL de un tramo de cobre: R = ρ·L/A. Con los grosores y
    /// largos típicos de la mesa da valores minúsculos (un 1.5mm de medio
    /// metro ronda 0.005Ω) — por eso el cable físico redondea a "conexión
    /// perfecta" todo lo que quede por debajo de ResistenciaDespreciable:
    /// esos tramos fusionan (corriente exacta por Kirchhoff, protegidos por
    /// su ampacidad) y solo los tramos FINOS o LARGOS pasan a ser aristas
    /// resistivas de verdad, que roban voltaje y se calientan solas (I²·R).
    /// </summary>
    public static double ResistenciaDeCobre(double longitudMetros, double grosorMm)
    {
        double radio = Math.Max(grosorMm, 0.05) * 0.001 * 0.5;
        double area = Math.PI * radio * radio;
        return ResistividadCobre * Math.Max(longitudMetros, 0.01) / area;
    }

    /// <summary>Por debajo de esto, el cobre calculado se trata como conexión perfecta (ver ResistenciaDeCobre).</summary>
    public const double ResistenciaDespreciable = 0.006;

    /// <summary>
    /// Cuántos amperes aguanta un cobre de ese grosor antes de que el
    /// aislante empiece a sufrir (curva aproximada a las tablas de
    /// instalación real: 0.5mm→~4A, 1.5mm→~15A, 2.5mm→~27A, 4mm→~48A).
    /// </summary>
    public static double AmpacidadDeCobre(double grosorMm) =>
        9.0 * Math.Pow(Math.Max(grosorMm, 0.1), 1.2);

    /// <summary>Masa térmica de un cable: crece con la sección (grosor²) y el largo.</summary>
    public static double CapacidadTermicaDeCable(double longitudMetros, double grosorMm) =>
        Math.Max(0.05, 0.6 * grosorMm * grosorMm * Math.Max(longitudMetros, 0.05));

    public Wire(Pin a, Pin b, string name = "Cable") : base(name, a, b)
    {
        MaxVoltage = CircuitConstants.LimiteCableDefault;
        MaxCurrent = CircuitConstants.LimiteCableDefault;
        MaxPower = CircuitConstants.LimiteCableDefault;
        MaxTemperature = CircuitConstants.LimiteCableDefault;
        CoeficienteEnfriamiento = 0.08; // un cable al aire ventila bastante bien
    }

    public override bool EsConexionPerfecta() => Resistencia <= CircuitConstants.UmbralConexionPerfecta;

    public override double GetResistance() => Math.Max(Resistencia, CircuitConstants.MinResistance);
}
