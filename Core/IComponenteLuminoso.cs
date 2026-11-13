namespace ElectricSim.Core;

/// <summary>
/// Color RGB normalizado (0..1) independiente de Godot para componentes que emiten luz.
/// </summary>
public readonly record struct ColorLuzRgb(float R, float G, float B);

/// <summary>
/// Contrato común para componentes emisores de luz (LED, Lámpara) cuyo color
/// de luz y de material se puede personalizar. Con <c>ColorPersonalizado == null</c>
/// mantienen exactamente sus colores predeterminados de fábrica.
/// </summary>
public interface IComponenteLuminoso
{
    ColorLuzRgb? ColorPersonalizado { get; set; }
}

