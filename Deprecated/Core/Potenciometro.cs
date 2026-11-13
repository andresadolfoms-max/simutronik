namespace ElectricSim.Core.Components;

/// <summary>
/// El "dimmer": una resistencia con PERILLA. Girás la ruedita y cambia la
/// resistencia — perilla al máximo, resistencia mínima, pasa más corriente,
/// más energía llega a la carga (la lámpara brilla más). Perilla a cero,
/// resistencia máxima, casi no pasa nada.
///
/// Es el hermano manual del "modo corriente objetivo" del Resistor: acá el
/// que ajusta sos VOS, en vivo, y ves las consecuencias al instante. La
/// interpolación es LOGARÍTMICA (como los potenciómetros de audio reales):
/// con lineal, casi todo el efecto se amontonaba en el último 10% del giro.
/// </summary>
public class Potenciometro : Component
{
    /// <summary>La perilla, de 0 (cerrado: máxima resistencia) a 100 (a fondo: mínima resistencia).</summary>
    public double Perilla { get; set; } = 50;

    /// <summary>Resistencia con la perilla a fondo (el "piso"). Nunca 0: hasta el mejor potenciómetro tiene contactos.</summary>
    public double ResistenciaMinima { get; set; } = 1;

    /// <summary>Resistencia con la perilla en cero (el "techo").</summary>
    public double ResistenciaMaxima { get; set; } = 10_000;

    public Potenciometro(string name, double resistenciaMaxima = 10_000) : base(name)
    {
        ResistenciaMaxima = resistenciaMaxima;
        MaxPower = 0.5; // como un potenciómetro chico real: se quema si lo usás de dimmer de estufa
    }

    /// <summary>La resistencia que la perilla está pidiendo ahora (interpolación logarítmica).</summary>
    public double ResistenciaActual
    {
        get
        {
            double fraccion = Math.Clamp(Perilla / 100.0, 0, 1);
            // Interpolar en el LOGARITMO: cada tramo del giro multiplica la
            // resistencia por el mismo factor (así el brillo cambia parejo).
            double logMin = Math.Log(Math.Max(ResistenciaMinima, CircuitConstants.MinResistance));
            double logMax = Math.Log(Math.Max(ResistenciaMaxima, ResistenciaMinima + 0.001));
            return Math.Exp(logMax + (logMin - logMax) * fraccion);
        }
    }

    public override double GetResistance() => IsBurnt ? CircuitConstants.OpenResistance : ResistenciaActual;
}
