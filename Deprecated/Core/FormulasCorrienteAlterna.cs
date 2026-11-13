namespace ElectricSim.Core;

/// <summary>
/// Fórmulas de corriente ALTERNA (AC): Impedancia, Factor de Potencia,
/// Reactancia, Frecuencia.
///
/// ESTADO ACTUAL: la AC ya está integrada al simulador, pero por la vía
/// PRAGMÁTICA que definimos para el proyecto — una FuenteAC empuja su valor
/// RMS al solver como si fuera un voltaje continuo (no se simula la onda en
/// tiempo real, para no gastar recursos), y lo que distingue AC de DC es la
/// etiqueta TipoCorriente que se propaga por la red y decide qué componentes
/// son compatibles. Para circuitos resistivos (lámparas, resistencias,
/// cables) el RMS da EXACTAMENTE los mismos números de potencia que la onda
/// real, así que no se pierde precisión donde importa.
///
/// Las fórmulas de abajo (reactancia, impedancia, fase) son las que va a
/// necesitar el día que sumemos componentes donde la FRECUENCIA sí importa
/// (transformadores, motores, capacitores en AC). Ese solver de fasores
/// necesita números complejos y es un cambio de fondo en CircuitGraph — está
/// anotado como trabajo futuro en el README. Nada de lo de abajo es trabajo
/// perdido: ese solver va a usar exactamente estos métodos por debajo.
/// </summary>
public static class FormulasCorrienteAlterna
{
    /// <summary>
    /// Reactancia capacitiva (Ohms): cuánto "frena" un capacitor a una
    /// frecuencia dada. A mayor frecuencia, menor reactancia (deja pasar
    /// más). Xc = 1 / (2π × f × C)
    /// </summary>
    public static double ReactanciaCapacitiva(double frecuenciaHz, double capacitanciaFaradios)
    {
        double denominador = 2 * Math.PI * frecuenciaHz * capacitanciaFaradios;
        return denominador > 0 ? 1.0 / denominador : CircuitConstants.OpenResistance;
    }

    /// <summary>
    /// Reactancia inductiva (Ohms): cuánto "frena" un inductor (bobina) a
    /// una frecuencia dada. A mayor frecuencia, mayor reactancia (frena
    /// más). XL = 2π × f × L
    /// </summary>
    public static double ReactanciaInductiva(double frecuenciaHz, double inductanciaHenrios) =>
        2 * Math.PI * frecuenciaHz * inductanciaHenrios;

    /// <summary>
    /// Magnitud de la impedancia (Ohms), combinando resistencia óhmica pura
    /// y reactancia neta (inductiva menos capacitiva). Z = √(R² + X²)
    /// </summary>
    public static double MagnitudImpedancia(double resistencia, double reactanciaNeta) =>
        Math.Sqrt(resistencia * resistencia + reactanciaNeta * reactanciaNeta);

    /// <summary>Ángulo de fase φ (radianes) entre voltaje y corriente. φ = atan(X / R)</summary>
    public static double AnguloDeFase(double resistencia, double reactanciaNeta) =>
        Math.Atan2(reactanciaNeta, resistencia);

    /// <summary>
    /// Factor de potencia: cos(φ). 1.0 = toda la potencia entregada es útil
    /// (circuito puramente resistivo). Cerca de 0 = mayormente reactiva
    /// (se "devuelve" casi toda la energía, como en un motor sin carga).
    /// </summary>
    public static double FactorDePotencia(double resistencia, double reactanciaNeta) =>
        Math.Cos(AnguloDeFase(resistencia, reactanciaNeta));

    /// <summary>Potencia real/útil (Watts) en un circuito AC: P = V_rms × I_rms × cos(φ)</summary>
    public static double PotenciaReal(double voltajeRms, double corrienteRms, double factorDePotencia) =>
        voltajeRms * corrienteRms * factorDePotencia;
}
