namespace ElectricSim.Core;

/// <summary>
/// Las fórmulas fundamentales de electricidad en corriente continua:
/// Ley de Ohm y Ley de Watt (potencia). Esta clase es la ÚNICA fuente de
/// verdad para estos cálculos en todo el proyecto. Tanto el motor (Component,
/// CircuitGraph) como cualquier componente que armes después en su propia
/// carpeta deberían llamar a estos métodos en vez de reescribir las fórmulas
/// a mano — así, si el día de mañana necesitás ajustar algo (por ejemplo,
/// cómo se evita la división por cero), lo cambiás en un solo lugar.
///
///   V = Voltaje / Tensión  (Volts)
///   I = Intensidad de Corriente (Amps)
///   R = Resistencia (Ohms)
///   P = Potencia (Watts)
/// </summary>
public static class LeyesElectricas
{
    // ===================== LEY DE OHM: V = I × R =====================

    /// <summary>V = I × R</summary>
    public static double Voltaje(double corriente, double resistencia) => corriente * resistencia;

    /// <summary>I = V / R</summary>
    public static double Corriente(double voltaje, double resistencia) =>
        voltaje / Math.Max(resistencia, CircuitConstants.MinResistance);

    /// <summary>R = V / I. Si la corriente es ~0, se considera circuito abierto (resistencia "infinita").</summary>
    public static double Resistencia(double voltaje, double corriente)
    {
        if (Math.Abs(corriente) < 1e-12) return CircuitConstants.OpenResistance;
        return voltaje / corriente;
    }

    /// <summary>
    /// G = 1 / R — la conductancia. La usa el solver de relajación para
    /// "pesar" cuánto tira cada vecino del voltaje de un nodo: a menor
    /// resistencia, mayor conductancia, más peso en el promedio.
    /// </summary>
    public static double Conductancia(double resistencia) =>
        1.0 / Math.Max(resistencia, CircuitConstants.MinResistance);

    // ===================== LEY DE WATT: P = V × I =====================

    /// <summary>P = V × I</summary>
    public static double PotenciaDesdeVI(double voltaje, double corriente) => Math.Abs(voltaje * corriente);

    /// <summary>P = I² × R — útil cuando conocés la corriente y la resistencia, no el voltaje directamente.</summary>
    public static double PotenciaDesdeIR(double corriente, double resistencia) =>
        corriente * corriente * resistencia;

    /// <summary>P = V² / R — útil cuando conocés el voltaje y la resistencia, no la corriente directamente.</summary>
    public static double PotenciaDesdeVR(double voltaje, double resistencia) =>
        (voltaje * voltaje) / Math.Max(resistencia, CircuitConstants.MinResistance);
}
