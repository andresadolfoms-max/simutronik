namespace ElectricSim.Core.Components;

/// <summary>
/// Resistencia. Tiene dos modos de trabajo:
///
/// 1. MODO CLÁSICO (default): el usuario fija los Ohms y la corriente sale
///    de la Ley de Ohm, I = V / R. La resistencia NO "frena amperios que le
///    llegan": decide cuánta corriente circula por el LAZO ENTERO (la
///    corriente que entra por una pata es EXACTAMENTE la que sale por la
///    otra — Ley de Kirchhoff, y hay un test que lo vigila).
///
/// 2. MODO CORRIENTE OBJETIVO: el usuario dice "quiero que pasen 20mA" y la
///    resistencia se auto-ajusta sola, tick a tick, hasta que por el circuito
///    circule esa corriente. Es la "perilla directa en amperios" que uno
///    intuitivamente espera — pero implementada con la física bien: por
///    dentro sigue siendo una resistencia obedeciendo Ohm, que busca el valor
///    de R que produce la I pedida.
///
///    CÓMO CONVERGE (mismo truco que el LED): con la corriente medida en el
///    tick anterior, el próximo valor es
///
///        R_siguiente = V_medido / I_objetivo  =  (I_medida × R_actual) / I_objetivo
///
///    En el punto fijo, I_medida == I_objetivo y R deja de moverse. Si la
///    fuente es ideal y no hay nada más en serie, converge EN UN SOLO TICK
///    (V sobre la resistencia es constante). Con más cosas en serie converge
///    geométricamente en unos pocos ticks. Nunca oscila: el factor de
///    contracción es R_externa / (R_externa + R_final) &lt; 1.
///
///    Si el objetivo es INALCANZABLE (pedís más corriente de la que la
///    fuente puede dar ni con la resistencia al mínimo, o el circuito está
///    abierto), la resistencia se clava en su tope y prende la bandera
///    ObjetivoAlcanzado=false en vez de mentir.
/// </summary>
public class Resistor : Component
{
    /// <summary>Ohms actuales. En modo objetivo, este valor lo maneja la propia resistencia.</summary>
    public double Resistance { get; set; }

    // ------------------------------------------------------------------
    //  Modo corriente objetivo
    // ------------------------------------------------------------------

    /// <summary>Si está activo, la resistencia ignora los Ohms fijados a mano y se auto-ajusta para que circule CorrienteObjetivo.</summary>
    public bool ModoCorrienteObjetivo { get; set; }

    /// <summary>Corriente deseada, en Amperes (0.02 = 20mA). Solo importa con el modo activo.</summary>
    public double CorrienteObjetivo { get; set; } = 0.02;

    /// <summary>Lectura para el inspector: ¿la corriente real ya está pegada al objetivo?</summary>
    public bool ObjetivoAlcanzado { get; private set; }

    /// <summary>
    /// Piso del auto-ajuste. No es MinResistance del solver (1µΩ) a propósito:
    /// una resistencia real de potencia no baja de fracciones de Ohm, y un piso
    /// tan bajo haría que "objetivo inalcanzable" degenere en un cuasi-corto.
    /// </summary>
    public const double ResistenciaMinimaObjetivo = 0.1;

    /// <summary>Techo del auto-ajuste: por encima de esto ya es indistinguible de "abierto".</summary>
    public const double ResistenciaMaximaObjetivo = 500_000;

    /// <summary>
    /// Con menos corriente que esto, el circuito está efectivamente abierto:
    /// no hay nada que regular. Son 10µA — el mismo umbral anti-fuga del
    /// capacitor, y por el mismo motivo: una llave abierta es "1MΩ", no
    /// infinito, y a 9V se filtran ~9µA que NO son un circuito real.
    /// (Mismo artefacto documentado: con fuentes de ~15V o más detrás de
    /// una llave abierta, la fuga supera el umbral y la resistencia va a
    /// intentar regular contra ella. Inofensivo: en cuanto el circuito se
    /// cierra de verdad, re-converge en un par de ticks.)
    /// </summary>
    private const double CorrienteMinimaParaRegular = 1e-5; // 10 µA

    public Resistor(string name, double resistanceOhms) : base(name)
    {
        Resistance = resistanceOhms;
        ResistenciaTermica = 250; // resistencia de ½W al aire: cuerpo chiquito, disipa poco
    }

    public override double GetResistance() => IsBurnt ? CircuitConstants.OpenResistance : Resistance;

    protected override void ActualizarEstado(double deltaTime)
    {
        if (!ModoCorrienteObjetivo || IsBurnt)
        {
            ObjetivoAlcanzado = false;
            return;
        }

        double objetivo = Math.Max(CorrienteObjetivo, 1e-9); // blindaje contra objetivo 0 o negativo
        double medida = Math.Abs(Current);

        // Circuito abierto (o sin fuente): la única "corriente" es la
        // microfuga de los 1MΩ de algo abierto. Ajustar contra eso haría
        // saltar R a cualquier lado. Nos quedamos quietos y avisamos.
        if (medida < CorrienteMinimaParaRegular)
        {
            ObjetivoAlcanzado = false;
            return;
        }

        // Un paso de convergencia POR TICK (regla de oro del solver: la
        // resistencia jamás cambia DENTRO del tick, solo entre ticks).
        double vMedido = medida * Resistance; // = |V| que me cayó este tick
        double rSiguiente = vMedido / objetivo;
        Resistance = Math.Clamp(rSiguiente, ResistenciaMinimaObjetivo, ResistenciaMaximaObjetivo);

        // ¿Ya estamos? Tolerancia del 2% (suficiente para que el inspector
        // muestre el tilde sin parpadear por redondeos del solver).
        ObjetivoAlcanzado = Math.Abs(medida - objetivo) <= objetivo * 0.02;
    }
}
