namespace ElectricSim.Core.Components;

/// <summary>
/// Motor de corriente continua, modelo pedagógico:
///
///  - Eléctricamente es la resistencia de su bobinado (consume potencia).
///  - Esa potencia se convierte en GIRO: la velocidad sube con inercia
///    (no arranca de golpe, como un motor real) hasta unas RPM
///    proporcionales a la potencia recibida respecto de la nominal.
///  - EL SENTIDO LO DECIDE LA POLARIDAD: corriente al revés = gira al
///    revés. Dar vuelta la batería invierte el motor — la lección clásica.
///
/// (Sin FCEM ni torque de carga: la resistencia es fija. El día que haga
/// falta realismo, acá está anotado dónde meterlo.)
/// </summary>
public class Motor : Component
{
    /// <summary>Resistencia del bobinado (Ohms): cuánta corriente traga del circuito.</summary>
    public double ResistenciaBobinado { get; set; } = 25;

    /// <summary>A esta potencia, el motor gira a sus RPM máximas.</summary>
    public double PotenciaNominal { get; set; } = 3;

    /// <summary>Velocidad tope, alcanzada a potencia nominal.</summary>
    public double RPMMaximas { get; set; } = 3000;

    /// <summary>Por debajo de esta potencia el rotor ni se despega (fricción estática).</summary>
    public double PotenciaMinimaArranque { get; set; } = 0.05;

    /// <summary>Velocidad actual CON SIGNO: positiva = horario (corriente de A a B), negativa = antihorario.</summary>
    public double RPM { get; private set; }

    /// <summary>Qué tan rápido responde a los cambios (1/s). Más alto = menos inercia.</summary>
    public double Reactividad { get; set; } = 3.0;

    public bool EstaGirando => Math.Abs(RPM) > 1;

    public Motor(string name, double resistenciaBobinado = 25) : base(name)
    {
        ResistenciaBobinado = resistenciaBobinado;
        // Es un motor de CONTINUA (PMDC): con alterna el campo del imán le
        // invierte el par 50 veces por segundo — vibra y se cocina, NO gira.
        // Sin esta marca, un motorcito DC giraba feliz enchufado a 220V~.
        Compatibilidad = CompatibilidadCorriente.SoloDC;
        MaxPower = 12;          // el doble y pico de lo nominal: forzarlo lo quema
        CapacidadTermica = 2.5; // masa de metal: tarda en calentar y en enfriar
    }

    public override double GetResistance() =>
        IsBurnt ? CircuitConstants.OpenResistance : Math.Max(ResistenciaBobinado, CircuitConstants.MinResistance);

    protected override void ActualizarEstado(double deltaTime)
    {
        // Objetivo de velocidad según la potencia real de este tick, con el
        // signo de la corriente (A→B positivo = horario).
        double objetivo = 0;
        if (!IsBurnt && Power >= PotenciaMinimaArranque)
        {
            double fraccion = Math.Min(Power / Math.Max(PotenciaNominal, 0.001), 1.2); // hasta 20% de sobre-régimen
            objetivo = RPMMaximas * fraccion * Math.Sign(Current);
        }

        // Inercia: acercarse al objetivo de a poco (suavizado exponencial).
        double avance = Math.Clamp(Reactividad * deltaTime, 0, 1);
        RPM += (objetivo - RPM) * avance;
        if (Math.Abs(RPM) < 1 && objetivo == 0) RPM = 0; // que no quede "temblando" en 0.3 RPM
    }
}
