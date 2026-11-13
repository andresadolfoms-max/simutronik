namespace ElectricSim.Core.Components;

/// <summary>
/// Capacitor ELECTROLÍTICO: el cilindro con la franja. Es el Capacitor de
/// siempre (misma carga, misma entrega, mismo modelo) más LA regla que todo
/// electrónico aprendió con un susto: TIENE POLARIDAD. La pata larga (+, el
/// PinA) va al positivo; si lo conectás al revés, el electrolito de adentro
/// se descompone, genera gas, y el capacitor REVIENTA — rápido y con drama,
/// como el "pum" clásico del taller.
///
/// El chequeo: si la POLARIDAD SE INVIERTE más allá de un umbral de
/// tolerancia (~1.5V: un poquito de inversa lo aguanta cualquiera) durante
/// unas décimas de segundo, muere con motivo "polaridad invertida". Más
/// rápido que un quemado normal, porque así es.
///
/// Se juzga por la PEOR de dos medidas (InversionEfectiva): el voltaje en
/// bornes y el voltaje ALMACENADO. Mirar sólo los bornes era el bug A6c —
/// un electrolítico que quedó cargado al revés puede marcar ~0 V afuera
/// (toda la caída se la come su ESR) mientras adentro el electrolito se
/// descompone igual.
///
/// A cambio de esa fragilidad, los electrolíticos existen porque meten MUCHA
/// capacidad en poco espacio: acá vienen de fábrica con valores grandes
/// (cientos a miles de µF) y el voltaje máximo impreso en el cuerpo (16V,
/// 25V...), que sigue siendo el MaxVoltage editable de siempre.
/// </summary>
public class CapacitorElectrolitico : Capacitor
{
    /// <summary>Cuánta polarización inversa tolera antes de empezar a descomponerse (~1-2V, la "zona de perdón").</summary>
    public double ToleranciaInversa { get; set; } = 1.5;

    /// <summary>Segundos de polaridad invertida sostenida hasta el estallido. Cortito: el electrolito no perdona.</summary>
    public double SegundosParaReventar { get; set; } = 0.3;

    /// <summary>Voltaje firmado PinA − PinB del último tick (negativo = está al revés). Lectura para el inspector.</summary>
    public double VoltajePolarizado { get; private set; }

    /// <summary>
    /// La inversión que de verdad sufre el electrolito: la PEOR de las dos
    /// medidas, la de bornes y la de la carga guardada. Mirar sólo los bornes
    /// (que era lo que se hacía) deja pasar el caso feo: un cap cargado al
    /// revés cuya caída se la come su propia ESR marca ~0 V afuera mientras
    /// adentro sigue invertido a varios volts. VoltajeAlmacenado ES la fem
    /// interna con signo, así que ése es el número honesto.
    /// </summary>
    public double InversionEfectiva => Math.Min(VoltajePolarizado, VoltajeAlmacenado);

    private double _segundosInvertido;

    public CapacitorElectrolitico(string name, double capacitanceFarads)
        : base(name, capacitanceFarads)
    {
        // Polarizado: en alterna se destruye de verdad, así que acá queda inerte.
        Compatibilidad = CompatibilidadCorriente.SoloDC;
        // Polaridad REAL: conectarlo al revés cambia lo que hace.
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;
        // El Capacitor base ya es SoloDC y ya trae MaxVoltage=25 (el número
        // impreso en el cuerpo). Acá solo se agrega la polaridad asesina.
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        base.ActualizarEstado(deltaTime); // toda la carga/descarga de siempre

        // El signo importa: PinA (la pata larga, +) tiene que quedar ARRIBA.
        VoltajePolarizado = PinA.Voltage - PinB.Voltage;

        if (!IsBurnt && InversionEfectiva < -ToleranciaInversa)
        {
            _segundosInvertido += deltaTime;
            if (_segundosInvertido >= SegundosParaReventar)
            {
                Quemar("polaridad invertida");
            }
        }
        else
        {
            _segundosInvertido = 0;
        }
    }

    protected override void Reparado()
    {
        base.Reparado();
        _segundosInvertido = 0;
    }
}
