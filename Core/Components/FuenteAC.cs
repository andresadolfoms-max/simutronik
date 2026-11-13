namespace ElectricSim.Core.Components;

/// <summary>
/// Fuente de corriente ALTERNA. Decisión de diseño del proyecto: NO se
/// simula la onda senoidal en tiempo real (sería carísimo y no aporta al
/// gameplay) — la fuente empuja su valor RMS al solver como si fuera un
/// voltaje continuo. Para cargas resistivas (lámparas, resistencias), el RMS
/// da exactamente los mismos Watts que la onda real, así que los números que
/// ves son físicamente correctos donde importa.
///
/// Lo que SÍ distingue a esta fuente de una batería:
/// - Sus bornes son FASE (PinA, marrón) y NEUTRO (PinB, celeste), no + y −.
///   El neutro es la referencia de 0V, como en una instalación real.
/// - Etiqueta la red con TipoCorriente.AC: los componentes solo-DC (LED,
///   capacitor electrolítico) detectan que les llega alterna y no conducen.
/// - Frecuencia es informativa por ahora; va a importar el día que sumemos
///   el solver de fasores (congelado — ver Deprecated/ALCANCE_CONGELADO.md).
/// </summary>
public class FuenteAC : FuenteBase
{
    /// <summary>Frecuencia de la red en Hz (50 en Argentina). Define la reactancia de los capacitores (Xc=1/2πfC) e inductores (XL=2πfL) de su red; en circuitos puramente resistivos no cambia ningún número, que es lo correcto.</summary>
    public double Frecuencia { get; set; }

    /// <summary>Alias legible del voltaje: en una fuente AC, Voltage ES el valor RMS.</summary>
    public double VoltajeRMS { get => Voltage; set => Voltage = value; }

    public override TipoCorriente Tipo => TipoCorriente.AC;

    public FuenteAC(string name, double voltajeRms = 12, double frecuenciaHz = 50) : base(name, voltajeRms)
    {
        Frecuencia = frecuenciaHz;
        PinA.Polaridad = Polaridad.Fase;
        PinB.Polaridad = Polaridad.Neutro;
        MaxCurrent = 5;
        ResistenciaInternaNominal = 0.5;
    }
}
