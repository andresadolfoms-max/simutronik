namespace ElectricSim.Core;

/// <summary>
/// Un componente que expone MÁS bornes conectables que los dos clásicos
/// (PinA y PinB). La capa visual los recorre para crear un BorneVisual por
/// cada uno, y el controlador de cables los trata igual que a cualquier
/// borne: se puede enchufar un cable a cada pin del Arduino, a la base de
/// un transistor con nombre propio, etc.
///
/// Es pariente de IComponenteCompuesto pero atiende otra pregunta:
///   - IComponenteCompuesto: "¿qué ARISTAS internas aportás al solver?"
///   - IComponenteMultiBorne: "¿qué BORNES extra puede cablear el usuario?"
/// Un componente puede implementar una, la otra o ambas (el Arduino: ambas).
/// </summary>
public interface IComponenteMultiBorne
{
    /// <summary>Bornes conectables adicionales, con la etiqueta que la capa visual usa para ubicarlos y rotularlos ("D13", "GND", "A0"...).</summary>
    IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra { get; }
}
