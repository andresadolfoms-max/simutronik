namespace ElectricSim.Core;

/// <summary>
/// Qué tipo de corriente está recibiendo un componente en este momento.
/// Se calcula cada tick en CircuitGraph mirando qué fuentes alimentan la red
/// a la que están conectados sus bornes.
///
/// Los valores están elegidos como BITS a propósito: DC=1, AC=2, y Mixta es
/// la combinación de ambos (1|2 = 3). Así, "propagar el tipo por la red" es
/// simplemente hacer OR de números enteros, sin estructuras raras.
///
/// Decisión de diseño del proyecto (ver README): la corriente alterna NO se
/// simula onda por onda (sería carísimo en CPU y no aporta nada al gameplay).
/// Una fuente AC empuja su valor RMS como si fuera un voltaje continuo, y lo
/// que distingue AC de DC es esta etiqueta, que los componentes usan para
/// saber si son compatibles con lo que les llega.
/// </summary>
[Flags]
public enum TipoCorriente
{
    /// <summary>El componente no está conectado a ninguna fuente activa.</summary>
    Ninguna = 0,

    /// <summary>Corriente continua (batería, pila).</summary>
    DC = 1,

    /// <summary>Corriente alterna (fuente de pared, generador). Se trabaja con su valor RMS.</summary>
    AC = 2,

    /// <summary>La red recibe DC y AC a la vez (dos fuentes de distinto tipo conectadas entre sí). Casi siempre es un error de cableado.</summary>
    Mixta = DC | AC,
}

/// <summary>
/// Con qué tipo de corriente puede trabajar un componente. Si le llega un
/// tipo incompatible, el componente se comporta como circuito abierto (no
/// conduce, no funciona) y la interfaz muestra la advertencia. Es la manera
/// simple y barata de representar "este componente no es para esa corriente".
/// </summary>
public enum CompatibilidadCorriente
{
    /// <summary>Funciona igual con continua o alterna (resistencias, lámparas incandescentes, cables, interruptores...).</summary>
    ACyDC = 0,

    /// <summary>Solo corriente continua (LED, capacitor electrolítico...). Con AC no conduce.</summary>
    SoloDC = 1,

    /// <summary>Solo corriente alterna (a futuro: transformadores, motores de inducción...).</summary>
    SoloAC = 2,
}
