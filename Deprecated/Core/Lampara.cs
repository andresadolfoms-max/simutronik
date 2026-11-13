namespace ElectricSim.Core.Components;

/// <summary>
/// Lámpara incandescente chica: eléctricamente es una resistencia fija, pero
/// con dos comportamientos propios que la hacen la estrella del sistema
/// térmico:
///
///  - Necesita una POTENCIA MÍNIMA para emitir luz ("necesita un mínimo para
///    funcionar"): por debajo de PotenciaMinima conduce igual, pero no brilla.
///  - Casi toda su potencia se vuelve CALOR (FraccionCalorDisipado alta):
///    es el componente ideal para ver cómo un objeto caliente le contagia
///    temperatura a los vecinos cercanos y cómo se enfría al cortarle la
///    corriente.
///
/// A diferencia del LED, funciona igual con continua o alterna (un filamento
/// no distingue el sentido de la corriente) — el contraste educativo entre
/// LED y lámpara frente a una FuenteAC es a propósito.
/// </summary>
public class Lampara : Component
{
    /// <summary>Resistencia del filamento (Ohms). Fija — no modelamos el cambio con la temperatura, para mantenerlo liviano.</summary>
    public double Resistencia { get; set; }

    /// <summary>Potencia (Watts) a la que la lámpara brilla al máximo (Brillo = 1).</summary>
    public double PotenciaNominal { get; set; } = 2.4;

    /// <summary>Potencia mínima (Watts) para que el filamento empiece a dar luz visible.</summary>
    public double PotenciaMinima { get; set; } = 0.05;

    /// <summary>0 = apagada, 1 = brillo máximo.</summary>
    public double Brillo { get; private set; }

    public bool EstaEncendida => Brillo > 0;

    public Lampara(string name, double resistenciaOhms = 60) : base(name)
    {
        Resistencia = resistenciaOhms;
        FraccionCalorDisipado = 0.9;  // una incandescente es 90% estufa, 10% luz
        CapacidadTermica = 0.2;       // filamento + ampolla chica: calienta rápido y en serio
        CoeficienteEnfriamiento = 0.06;
        MaxTemperature = 300;
        MaxPower = 5;
    }

    public override double GetResistance() => Resistencia;

    protected override void ActualizarEstado(double deltaTime)
    {
        Brillo = Power < PotenciaMinima
            ? 0
            : Math.Clamp(Power / Math.Max(PotenciaNominal, 1e-6), 0, 1);
    }

    protected override void Quemar()
    {
        base.Quemar();
        Brillo = 0;
    }

    protected override void Reparado() => Brillo = 0;
}
