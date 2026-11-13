using Godot;
using System.Text.Json;

namespace ElectricSim.Game;

/// <summary>
/// Una BOLSA DE PROPIEDADES con nombre: el ladrillo con el que todo el
/// sistema de guardado habla. Sirve para las dos direcciones —se le ponen
/// valores al guardar y se le piden al cargar— y esa simetría es lo que
/// permite que agregar algo nuevo sea escribir DOS líneas (una que pone,
/// otra que lee) en el lugar donde vive esa cosa, sin tocar el guardador.
///
/// Por dentro son JsonElement, y eso da gratis la propiedad más importante
/// del formato: las claves que este build NO conoce igual sobreviven. Si
/// abrís un proyecto guardado por una versión más nueva del simulador, sus
/// datos extra viajan intactos y se vuelven a escribir al guardar, en vez de
/// perderse.
///
/// Los getters nunca tiran excepción: si la clave no está, o vino con otro
/// tipo, devuelven el valor por defecto que le pases. Un archivo tocado a
/// mano no puede reventar la carga.
/// </summary>
public class EstadoGuardado
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly Dictionary<string, JsonElement> _datos;

    public EstadoGuardado() => _datos = new Dictionary<string, JsonElement>();

    public EstadoGuardado(Dictionary<string, JsonElement>? datos) =>
        _datos = datos ?? new Dictionary<string, JsonElement>();

    /// <summary>Los datos crudos (los usa el serializador; incluye las claves desconocidas que se preservan).</summary>
    public Dictionary<string, JsonElement> Datos => _datos;

    /// <summary>¿Está vacía? (para no escribir secciones sin nada).</summary>
    public bool Vacia => _datos.Count == 0;

    public bool Tiene(string clave) => _datos.ContainsKey(clave);

    // =====================================================================
    //  Escribir
    // =====================================================================

    /// <summary>Guarda cualquier valor serializable: número, bool, texto, arreglo, lista...</summary>
    public void Poner(string clave, object? valor)
    {
        if (valor == null) return;
        try
        {
            _datos[clave] = JsonSerializer.SerializeToElement(valor, Opciones);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Guardado: no se pudo serializar '{clave}': {e.Message}");
        }
    }

    /// <summary>Guarda un Vector3 como [x, y, z].</summary>
    public void PonerVector(string clave, Vector3 v) => Poner(clave, new[] { v.X, v.Y, v.Z });

    /// <summary>Guarda un color como [r, g, b].</summary>
    public void PonerColor(string clave, Color c) => Poner(clave, new[] { c.R, c.G, c.B });

    // =====================================================================
    //  Leer (nunca tiran: si falta o no calza el tipo, devuelven el default)
    // =====================================================================

    /// <summary>
    /// Lee un número tolerando que venga como texto: los infinitos y NaN se
    /// escriben como "Infinity" / "-Infinity" / "NaN" (así los guarda JSON),
    /// y los límites "sin tope" del simulador son justamente infinitos. Sin
    /// esto, un ∞ guardado volvía convertido en nada.
    /// </summary>
    private static bool TryNumero(JsonElement e, out double valor)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out valor)) return true;

        if (e.ValueKind == JsonValueKind.String)
        {
            string texto = e.GetString() ?? "";
            switch (texto)
            {
                case "Infinity": valor = double.PositiveInfinity; return true;
                case "-Infinity": valor = double.NegativeInfinity; return true;
                case "NaN": valor = double.NaN; return true;
            }
            // Por si alguien editó el archivo a mano.
            if (double.TryParse(texto, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out valor)) return true;
        }

        valor = 0;
        return false;
    }

    public double Numero(string clave, double porDefecto = 0) =>
        _datos.TryGetValue(clave, out var e) && TryNumero(e, out double v) ? v : porDefecto;

    public bool Booleano(string clave, bool porDefecto = false) =>
        _datos.TryGetValue(clave, out var e)
            ? e.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => porDefecto,
            }
            : porDefecto;

    public string Texto(string clave, string porDefecto = "") =>
        _datos.TryGetValue(clave, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? porDefecto
            : porDefecto;

    /// <summary>Lee un arreglo de números (lo usan vectores, colores y listas de límites).</summary>
    public double[] Numeros(string clave)
    {
        if (!_datos.TryGetValue(clave, out var e) || e.ValueKind != JsonValueKind.Array)
            return System.Array.Empty<double>();

        var lista = new List<double>();
        foreach (var item in e.EnumerateArray())
        {
            if (TryNumero(item, out double v)) lista.Add(v);
        }
        return lista.ToArray();
    }

    public Vector3 Vector(string clave, Vector3 porDefecto = default)
    {
        var n = Numeros(clave);
        return n.Length >= 3 ? new Vector3((float)n[0], (float)n[1], (float)n[2]) : porDefecto;
    }

    public Color ColorDe(string clave, Color porDefecto)
    {
        var n = Numeros(clave);
        return n.Length >= 3 ? new Color((float)n[0], (float)n[1], (float)n[2]) : porDefecto;
    }

    /// <summary>Lee una lista de vectores ([[x,y,z], [x,y,z]...]), como los dobleces de un cable.</summary>
    public List<Vector3> Vectores(string clave)
    {
        var salida = new List<Vector3>();
        if (!_datos.TryGetValue(clave, out var e) || e.ValueKind != JsonValueKind.Array) return salida;

        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array) continue;
            var n = new List<double>();
            foreach (var c in item.EnumerateArray())
            {
                if (TryNumero(c, out double v)) n.Add(v);
            }
            if (n.Count >= 3) salida.Add(new Vector3((float)n[0], (float)n[1], (float)n[2]));
        }
        return salida;
    }

    /// <summary>Una sub-bolsa anidada (para secciones dentro de secciones).</summary>
    public EstadoGuardado Sub(string clave)
    {
        if (_datos.TryGetValue(clave, out var e) && e.ValueKind == JsonValueKind.Object)
        {
            var dic = new Dictionary<string, JsonElement>();
            foreach (var prop in e.EnumerateObject()) dic[prop.Name] = prop.Value;
            return new EstadoGuardado(dic);
        }
        return new EstadoGuardado();
    }

    /// <summary>Mete una sub-bolsa como valor de una clave.</summary>
    public void PonerSub(string clave, EstadoGuardado sub)
    {
        if (sub.Vacia) return;
        Poner(clave, sub.Datos);
    }
}

/// <summary>
/// El registro de APORTES al archivo de proyecto: la puerta para que un
/// subsistema ENTERO y nuevo (notas pegadas al tablero, una protoboard,
/// marcadores de cámara, lo que se te ocurra) se guarde y se cargue sin
/// tocar el guardador ni el formato.
///
/// Se usa una sola vez, donde ese subsistema nace (típicamente en Main):
///
///     RegistroGuardado.Registrar("mundo",
///         estado => estado.Poner("ticksPorSegundo", _sim.TicksPorSegundo),
///         estado => _sim.TicksPorSegundo = estado.Numero("ticksPorSegundo", 10));
///
/// Cada aporte escribe en su propia sección de "extras", así que dos
/// subsistemas nunca se pisan. Registrar dos veces la misma clave REEMPLAZA
/// (no duplica): recargar la escena en el editor no rompe nada.
/// </summary>
public static class RegistroGuardado
{
    private static readonly Dictionary<string, (Action<EstadoGuardado> Guardar, Action<EstadoGuardado> Cargar)> _aportes = new();

    /// <summary>Suma (o reemplaza) un aporte al archivo de proyecto.</summary>
    public static void Registrar(string clave, Action<EstadoGuardado> guardar, Action<EstadoGuardado> cargar) =>
        _aportes[clave] = (guardar, cargar);

    /// <summary>Saca un aporte del registro.</summary>
    public static void Quitar(string clave) => _aportes.Remove(clave);

    internal static IEnumerable<KeyValuePair<string, (Action<EstadoGuardado> Guardar, Action<EstadoGuardado> Cargar)>> Aportes => _aportes;

    internal static bool TieneAporte(string clave) => _aportes.ContainsKey(clave);
}
