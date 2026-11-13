namespace ElectricSim.Core.Components;

/// <summary>
/// Fusible: un conductor sacrificable. Si la corriente que lo atraviesa
/// supera su máximo, se corta casi al instante (50ms de exceso) y
/// protege al resto del circuito — la manera clásica de salvar un LED de
/// una batería sin resistencia limitadora.
///
/// Detalle técnico importante (y por qué NO es una "conexión perfecta"):
/// para saber si se pasó de corriente, el fusible necesita MEDIR la
/// corriente que lo atraviesa, y por un componente fusionado no se puede
/// medir nada (sus dos bornes son el mismo nodo, la caída es 0). Por eso es
/// una arista resistiva con una resistencia chiquita fija. Se verificó
/// empíricamente que 0.05Ω convive perfecto con la relajación del solver
/// (error 0.000% contra el análisis nodal exacto, incluso en mallas con
/// resistencias 200.000 veces más grandes al lado) — el problema de
/// convergencia que motivó el Union-Find era de los cables de 0.001Ω, dos
/// órdenes de magnitud más extremos.
/// </summary>
public class Fusible : Component
{
    /// <summary>Resistencia interna del filamento (Ohms). Chica pero medible.</summary>
    public double ResistenciaInterna { get; set; } = 0.05;

    /// <summary>Segundos que aguanta con una sobrecarga JUSTITA (1× su corriente máxima).</summary>
    public double SegundosBase { get; set; } = 0.5;

    /// <summary>
    /// Curva I-t: cuánto más rápido corta cuanto mayor es la sobrecarga. Un
    /// fusible NO tiene un tiempo fijo — al doble de corriente corta mucho
    /// antes que con un 10% de más. Exponente 2 ≈ fusible RÁPIDO (vidrio, tipo
    /// F); exponente 1 ≈ RETARDADO (tipo T, el que se banca el pico de
    /// arranque de un motor sin cortar).
    /// </summary>
    public double ExponenteCurva { get; set; } = 2;

    public Fusible(string name, double corrienteMaxima = 1.0) : base(name)
    {
        MaxCurrent = corrienteMaxima;
        SegundosParaQuemarse = SegundosBase;
        // [P10-4] 999°C era "muere por corriente, no por calor" — pero un
        // fusible metido adentro de un aparato que se cocina TIENE que cortar,
        // que es la mitad de para qué existe. El estaño funde cerca de 230°C.
        MaxTemperature = 230;
        FraccionCalorDisipado = 0.4;
        CapacidadTermica = 0.2;     // filamento finito: si calienta, calienta rápido
    }

    public override double GetResistance() => ResistenciaInterna;

    protected override void ActualizarEstado(double deltaTime)
    {
        // [P10-4] La CURVA I-t: el tiempo que aguanta cae con la sobrecarga.
        //     t_corte = SegundosBase / (I/I_max)^exponente
        // Al doble de corriente, un fusible rápido (exp=2) corta 4× antes.
        // Antes era un tiempo FIJO de 50ms: cortaba igual con un 1% de exceso
        // que con un cortocircuito, y no se podía distinguir un fusible rápido
        // de uno retardado — que es exactamente la elección que un fusible te
        // obliga a hacer en la vida real.
        double corriente = Math.Abs(Current);
        if (IsBurnt || MaxCurrent <= 0 || double.IsPositiveInfinity(MaxCurrent) || corriente <= MaxCurrent)
        {
            SegundosParaQuemarse = SegundosBase;
            return;
        }

        double sobrecarga = corriente / MaxCurrent;
        SegundosParaQuemarse = Math.Max(
            SegundosBase / Math.Pow(sobrecarga, Math.Max(ExponenteCurva, 0.1)),
            1e-4);
    }
}
