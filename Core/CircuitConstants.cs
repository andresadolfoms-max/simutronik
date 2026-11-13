namespace ElectricSim.Core;

/// <summary>
/// Valores constantes que usa todo el motor. Centralizarlos acá evita
/// "números mágicos" repetidos en cada componente.
/// </summary>
public static class CircuitConstants
{
    /// <summary>Resistencia de un cable "perfecto". Nunca 0 exacto, para evitar división por cero.</summary>
    public const double WireResistance = 0.001;

    /// <summary>Resistencia que representa un circuito abierto (interruptor abierto, componente quemado, etc).</summary>
    public const double OpenResistance = 1_000_000;

    /// <summary>Piso mínimo de resistencia que se usa al calcular conductancias, por seguridad numérica.</summary>
    public const double MinResistance = 1e-6;

    /// <summary>
    /// Un cable con resistencia MENOR O IGUAL a este umbral se trata como
    /// "conexión perfecta" y sus dos puntas se fusionan en un solo nodo
    /// (Union-Find). Si el usuario le sube la resistencia por encima de esto,
    /// el cable pasa a ser una arista real del solver: cae voltaje en él,
    /// disipa potencia y genera calor, como un cable finito de verdad.
    /// </summary>
    public const double UmbralConexionPerfecta = 0.002;

    /// <summary>
    /// Temperatura ambiente por defecto, en grados Celsius. El usuario puede
    /// cambiarla en vivo desde la interfaz (SimuladorTermico.TemperaturaAmbiente).
    /// </summary>
    public const double TemperaturaAmbienteDefault = 20;

    /// <summary>
    /// Límite por defecto de los cables para voltaje / corriente / potencia /
    /// temperatura. Es alto a propósito ("999 para todo") para que los cables
    /// no molesten mientras se arma un circuito; igual son editables uno por
    /// uno desde el inspector, como cualquier otra estadística.
    /// </summary>
    public const double LimiteCableDefault = 999;
}
