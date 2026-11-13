namespace ElectricSim.Core;

/// <summary>
/// Rol eléctrico de un borne. Todo componente tiene asignado su positivo (A)
/// y su negativo (B); las fuentes de alterna usan Fase y Neutro en su lugar.
/// La capa visual usa esto para colorear los bornes y los cables
/// (rojo = positivo, negro = negativo, marrón = fase, celeste = neutro).
/// </summary>
public enum Polaridad
{
    Ninguna,
    Positivo,
    Negativo,
    Fase,
    Neutro,

    /// <summary>Borne de señal de un microcontrolador (pin digital/analógico del Arduino): puede ser entrada o salida según el programa.</summary>
    Senal,
}

/// <summary>
/// Representa un punto de conexión eléctrica: un borne de un componente
/// (ej. la pata positiva de un LED) o un simple punto de unión entre cables.
/// Es el "vértice" del grafo eléctrico — acá vive el voltaje que calcula el solver.
/// </summary>
public class Pin
{
    private static int _nextId;

    public int Id { get; }
    public string Label { get; }

    /// <summary>Rol de este borne dentro de su componente (positivo, negativo, fase, neutro).</summary>
    public Polaridad Polaridad { get; set; } = Polaridad.Ninguna;

    /// <summary>Voltaje actual de este punto (en Volts). El solver lo va actualizando cada iteración.</summary>
    public double Voltage { get; set; }

    /// <summary>
    /// Si es true, este pin es una fuente fija de voltaje (ej. un borne de batería)
    /// y el motor de relajación NO debe recalcularlo — solo lo respeta como dato.
    /// </summary>
    public bool IsFixed { get; set; }

    /// <summary>Componente al que pertenece este pin (puede ser null si es un punto de unión libre).</summary>
    public Component? Owner { get; }

    public Pin(Component? owner, string label)
    {
        Id = _nextId++;
        Owner = owner;
        Label = label;
    }

    public override string ToString() => $"Pin#{Id}[{Label}]={Voltage:F3}V{(IsFixed ? " (fijo)" : "")}";
}
