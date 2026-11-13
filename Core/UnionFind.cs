namespace ElectricSim.Core;

/// <summary>
/// Union-Find (Disjoint Set) clásico, con compresión de camino. CircuitGraph
/// lo usa para fusionar pines unidos por "conexiones perfectas" (cables,
/// interruptores cerrados) en un solo nodo eléctrico real, en vez de
/// tratarlos como una resistencia casi-nula dentro de la relajación.
///
/// Por qué esto importa (y no es un detalle menor): si un cable se modela
/// como "una resistencia muy chiquita" y convive en la misma red con una
/// resistencia normal de, digamos, 220Ω, la diferencia de escala entre
/// ambas (cientos de miles de veces) hace que el método de relajación
/// tarde MILLONES de iteraciones en converger — no 10 o 20. Fusionar los
/// pines en vez de "casi fusionarlos" elimina ese problema de raíz: ya no
/// hay ninguna resistencia extrema en la matemática, solo las resistencias
/// reales que vos definas para cada componente.
/// </summary>
internal sealed class UnionFind
{
    private readonly Dictionary<Pin, Pin> _padre = new();

    public void Asegurar(Pin pin)
    {
        if (!_padre.ContainsKey(pin))
        {
            _padre[pin] = pin;
        }
    }

    public Pin Encontrar(Pin pin)
    {
        Asegurar(pin);
        if (_padre[pin] != pin)
        {
            _padre[pin] = Encontrar(_padre[pin]); // compresión de camino
        }
        return _padre[pin];
    }

    public void Unir(Pin a, Pin b)
    {
        var raizA = Encontrar(a);
        var raizB = Encontrar(b);
        if (raizA != raizB)
        {
            _padre[raizA] = raizB;
        }
    }
}
