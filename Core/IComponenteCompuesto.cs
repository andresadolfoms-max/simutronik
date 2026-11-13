namespace ElectricSim.Core;

/// <summary>
/// Un componente que, además de su arista principal (PinA–PinB), aporta
/// ARISTAS INTERNAS extra al grafo. Es la puerta de entrada a los
/// componentes de MÁS de dos bornes: el transistor (colector–emisor como
/// arista principal + base–emisor como arista interna), y los convertidores
/// (transformador, rectificador, inversor: la salida como fuente principal
/// + la etapa de entrada como arista interna).
///
/// CircuitGraph.Agregar/Quitar las suma y saca automáticamente: para el
/// solver son componentes comunes (resuelven voltajes, corriente y calor
/// como cualquiera); para el catálogo y la UI no existen — viven adentro
/// de su dueño.
/// </summary>
public interface IComponenteCompuesto
{
    IEnumerable<Component> AristasInternas { get; }
}
