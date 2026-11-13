namespace ElectricSim.Core.Components;

/// <summary>
/// Inductor (bobina), modelo pedagógico ESPEJO del capacitor:
///
///  - El capacitor deja pasar al principio y BLOQUEA cuando se carga.
///  - El inductor BLOQUEA al principio (odia los cambios bruscos de
///    corriente) y deja pasar cuando su campo magnético se establece.
///
/// Implementación: una resistencia que arranca alta (ResistenciaInicial) y
/// baja en rampa hasta la del alambre a medida que el flujo se establece.
/// "SegundosEstablecimiento" hace de constante de tiempo — igual que la
/// rampa lineal del capacitor, es una aproximación honesta y anotada (la
/// exponencial real de L/R llega con el solver de fasores, junto con la
/// reactancia en AC y la chispa al abrir el circuito — TODO documentado).
/// </summary>
public class Inductor : Component
{
    /// <summary>Cuánto tarda el campo en establecerse del todo (la "constante de tiempo" pedagógica).</summary>
    public double SegundosEstablecimiento { get; set; }

    /// <summary>Resistencia aparente con el campo sin formar (el "freno" inicial al cambio).</summary>
    public double ResistenciaInicial { get; set; } = 4000;

    /// <summary>Resistencia real del alambre de la bobina (a donde llega cuando el campo está formado).</summary>
    public double ResistenciaBobinado { get; set; } = 0.8;

    /// <summary>0 = campo sin formar (bloquea) · 1 = campo establecido (conduce).</summary>
    public double NivelFlujo { get; private set; }

    /// <summary>Con menos corriente que esto, el campo se DESARMA (el circuito se abrió o apagó).</summary>
    private const double CorrienteMinima = 1e-5; // 10 µA: el mismo umbral anti-fuga del capacitor

    public Inductor(string name, double segundosEstablecimiento = 0.6) : base(name)
    {
        SegundosEstablecimiento = segundosEstablecimiento;
    }

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;
        // Rampa entre el freno inicial y el alambre desnudo según el flujo.
        double r = ResistenciaInicial + (ResistenciaBobinado - ResistenciaInicial) * NivelFlujo;
        return Math.Max(r, CircuitConstants.MinResistance);
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        double paso = deltaTime / Math.Max(SegundosEstablecimiento, 0.01);
        if (Math.Abs(Current) > CorrienteMinima)
        {
            NivelFlujo = Math.Min(1, NivelFlujo + paso);      // el campo se forma
        }
        else
        {
            // Sin corriente el campo colapsa (más rápido de lo que se formó:
            // en la vida real es un colapso violento — la chispa del TODO).
            NivelFlujo = Math.Max(0, NivelFlujo - paso * 3);
        }
    }
}
