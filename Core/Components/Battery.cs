namespace ElectricSim.Core.Components;

/// <summary>
/// Fuente de voltaje CONTINUO (batería, pila). Su borne negativo (PinB) es
/// siempre la referencia de tierra (0V) del circuito, y el positivo (PinA)
/// queda Voltage volts por encima.
///
/// Qué cambió respecto de la v1 (y qué se mantiene a propósito):
/// - Current/Power ahora SÍ son reales: CircuitGraph le asigna cada tick la
///   corriente que de verdad entrega, por Ley de Kirchhoff en su nodo
///   positivo (antes quedaban en 0 a propósito para no inventar números).
/// - Si la ponés en cortocircuito (cable directo de + a −), lo sufre COMO EN
///   LA VIDA REAL: entrega su corriente de corto (V/R_interna ≈ 5.3A), se
///   convierte en una estufa de ~48W y se cocina hacia los ~65°C — pero NO
///   muere, porque esa corriente no supera lo que aguanta. Ver FuenteBase.
/// - SE HUNDE bajo carga, como una pila de verdad: la resistencia interna
///   participa del solver (Fix 4), así que los bornes marcan V − I·R_int y
///   el resto del circuito lo sufre. En corto el voltaje colapsa a 0.
/// - Varias baterías EN SERIE suman voltajes de verdad (9+9 = 18V), y en
///   PARALELO del mismo voltaje se reparten la corriente. Ver FuenteBase.
/// </summary>
public class Battery : FuenteBase
{
    public override TipoCorriente Tipo => TipoCorriente.DC;

    /// <summary>Voltaje de la pila NUEVA. `Voltage` baja desde acá siguiendo la curva de descarga.</summary>
    public double VoltajeNominal { get; set; }

    /// <summary>Capacidad en mAh. ∞ = la variante sandbox, que no se descarga nunca.</summary>
    public double CargaNominal { get; set; } = 550;      // una 9V alcalina

    /// <summary>Carga que le queda, en mAh. Baja con ∫|I|·dt.</summary>
    public double CargaRestante { get; set; } = 550;

    /// <summary>
    /// Corriente de PICO (A). Es un límite BLANDO: pedirle más no la mata, le
    /// sube la resistencia interna, que es lo que hace una pila de verdad.
    /// Antes esto era MaxCurrent y era un límite DURO: 50 mA en la pila botón
    /// significaba que 50Ω la mataba y 58Ω la dejaba intacta — un filo de
    /// navaja donde debería haber una curva (A1). MaxCurrent queda, pero como
    /// perforación de verdad, en el orden de los amperes.
    /// </summary>
    public double CorrientePico { get; set; } = 6.0;

    /// <summary>
    /// Resistencia interna con la pila NUEVA. Se captura sola la primera vez
    /// que corre un tick, así el código que setea ResistenciaInternaNominal
    /// directo (catálogo, tests) sigue funcionando sin tocarlo.
    /// </summary>
    public double ResistenciaInternaFresca { get; set; } = double.NaN;

    /// <summary>Cuánta batería queda, de 0 a 1.</summary>
    public double FraccionCarga => double.IsPositiveInfinity(CargaNominal)
        ? 1.0
        : Math.Clamp(CargaRestante / Math.Max(CargaNominal, 1e-9), 0, 1);

    /// <summary>True cuando ya no le queda nada. No está rota: está gastada — se puede recargar.</summary>
    public bool Agotada => FraccionCarga <= 0;

    /// <summary>
    /// Horas que aguantaría a la corriente que está entregando AHORA. Sirve
    /// para la lectura del inspector; con corriente ~0 devuelve ∞.
    /// </summary>
    public double AutonomiaHoras
    {
        get
        {
            double mA = Math.Abs(CorrienteEntregada) * 1000;
            return mA < 1e-6 ? double.PositiveInfinity : CargaRestante / mA;
        }
    }

    public Battery(string name, double voltage) : base(name, voltage)
    {
        // Valores REALES de una batería de bloque de 9V alcalina: ~1.7Ω de
        // resistencia interna → su corriente de cortocircuito es 9/1.7 ≈ 5.3A
        // y se convierte en estufa (47W adentro) que se cocina hacia los ~65°C
        // — pero AHORA, además, se va gastando mientras lo hace.
        VoltajeNominal = voltage;
        MaxCurrent = 30.0;              // perforación de verdad, no "se acabó"
        CorrientePico = 6.0;
        ResistenciaInternaNominal = 1.7;
    }

    /// <summary>
    /// La curva de descarga: MESETA larga y después la RODILLA. Una alcalina
    /// se mantiene casi en su voltaje nominal el 80% de su vida y se desploma
    /// al final — no baja linealmente, que es lo que la gente cree.
    /// </summary>
    private static double FactorDescarga(double fraccion) =>
        fraccion >= 0.2
            ? 0.95 + 0.05 * (fraccion - 0.2) / 0.8     // meseta: 1.00 → 0.95
            : 0.95 * (fraccion / 0.2);                 // rodilla: 0.95 → 0

    protected override void ActualizarEstado(double deltaTime)
    {
        if (IsBurnt) return;

        if (double.IsNaN(ResistenciaInternaFresca))
        {
            ResistenciaInternaFresca = ResistenciaInternaNominal;
        }

        // 1) gastar carga: mAh = A × s × 1000/3600
        if (!double.IsPositiveInfinity(CargaNominal))
        {
            CargaRestante = Math.Max(0,
                CargaRestante - Math.Abs(CorrienteEntregada) * deltaTime * 1000.0 / 3600.0);
        }

        double f = FraccionCarga;

        // 2) el voltaje sigue la curva
        Voltage = VoltajeNominal * FactorDescarga(f);

        // 3) la resistencia interna SUBE al descargarse (efecto real: por eso
        //    una pila cansada prende menos el foco aunque marque casi 9V en
        //    el tester, que la mide sin carga)
        double r = ResistenciaInternaFresca * (1 + 4 * (1 - f) * (1 - f));

        // 4) y sube más si le pedís por encima de su corriente de pico: es la
        //    forma correcta de decir "esta pila no puede con esto" — se hunde,
        //    no explota.
        double i = Math.Abs(CorrienteEntregada);
        if (CorrientePico > 0 && i > CorrientePico)
        {
            r *= i / CorrientePico;
        }
        ResistenciaInternaNominal = r;
    }

    /// <summary>Ponerle pilas nuevas. El botón del inspector, al lado del de reparar.</summary>
    public void Recargar()
    {
        CargaRestante = CargaNominal;
        Voltage = VoltajeNominal;
        if (!double.IsNaN(ResistenciaInternaFresca))
        {
            ResistenciaInternaNominal = ResistenciaInternaFresca;
        }
    }
}
