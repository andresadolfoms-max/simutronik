using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElectricSim.Game;

/// <summary>
/// Guardar y cargar PROYECTOS (circuitos enteros) en un archivo .json.
///
/// ==================== POR QUÉ NO VAS A TENER QUE TOCAR ESTE ARCHIVO ====
///
/// Este guardador no sabe qué es un LED, ni un cable físico, ni una placa
/// Arduino: solo sabe recorrer el catálogo y pedirle a cada cosa que se
/// describa. Por eso agregar cosas nuevas al simulador NO lo obliga a
/// cambiar. Hay tres niveles, de menos a más trabajo:
///
///  1) COMPONENTE O STAT NUEVA → cero trabajo. El tipo (Def.Id), la
///     posición, la rotación y TODAS las StatDef del catálogo se guardan y
///     se restauran solas. Un componente nuevo con diez stats ya se guarda
///     bien antes de que escribas una línea acá.
///
///  2) ESTADO VIVO QUE NO ES UNA STAT (si un interruptor quedó cerrado, qué
///     .hex tiene una placa, la posición de una perilla) → dos lambdas en la
///     entrada del catálogo de ese componente, que es el archivo que ya
///     estabas editando:
///
///         miTipo.GuardarEstado = (c, e) => e.Poner("cerrado", ((Switch)c).IsClosed);
///         miTipo.CargarEstado  = (c, e) => ((Switch)c).IsClosed = e.Booleano("cerrado", true);
///
///     Para los CABLES lo mismo, pero los métodos viven en CableVisual
///     (GuardarEstado / CargarEstado): agregar una propiedad de cable es una
///     línea ahí.
///
///  3) UN SUBSISTEMA ENTERO NUEVO (notas en el tablero, una protoboard,
///     marcadores de cámara) → se registra UNA vez donde nace, y aparece en
///     el archivo con su propia sección:
///
///         RegistroGuardado.Registrar("misNotas", guardar, cargar);
///
/// Y además, lo que este build NO entiende igual sobrevive: las claves
/// desconocidas (de un proyecto guardado por una versión más nueva) se
/// preservan y se vuelven a escribir, en vez de perderse.
///
/// ==================== EL FORMATO ======================================
///
///   {
///     "version": 2,
///     "componentes": [ { "tipo": "led.real", "pos": [x,y,z], "rot": [x,y,z],
///                        "stats": {...}, "estado": {...} } ],
///     "cables":      [ { "compA": 0, "borneA": "A", "compB": 1, "borneB": "B",
///                        "fisico": false, ...propiedades del cable... } ],
///     "extras":      { "mundo": {...}, ...aportes registrados... }
///   }
///
/// Cada borne se nombra con una clave estable dentro de su componente: "A" y
/// "B" para las dos patas, y "X0", "X1"... para los extra (los 22 GPIO del
/// Arduino, la base del transistor). Los cables referencian al componente
/// por su ÍNDICE en la lista, así el archivo es autocontenido.
///
/// Los archivos de la versión 1 se abren sin problema: el formato 2 es un
/// superconjunto (mismos nombres de campo, más secciones nuevas opcionales).
/// </summary>
public static class GestorGuardado
{
    /// <summary>Versión del formato que escribe este build.</summary>
    public const int VersionFormato = 2;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, // los ∞ de los límites
    };

    /// <summary>
    /// Se llama cuando se terminó de cargar un proyecto, para que la interfaz
    /// se resincronice. Es un campo asignable (no un event) a propósito:
    /// asignarlo REEMPLAZA, así recargar la escena no acumula suscripciones
    /// viejas apuntando a nodos muertos.
    /// </summary>
    public static Action? ProyectoCargado;

    /// <summary>Secciones de "extras" que venían en el último archivo abierto y que este build no sabe interpretar. Se reescriben al guardar.</summary>
    private static Dictionary<string, JsonElement> _extrasDesconocidos = new();

    // =====================================================================
    //  Guardar
    // =====================================================================

    /// <summary>Convierte el circuito vivo del controlador en el texto JSON del proyecto.</summary>
    public static string Serializar(ControladorConstruccion controlador)
    {
        var proyecto = new ProyectoDTO { version = VersionFormato };

        // El índice de cada componente en la lista es su "id" dentro del archivo.
        var indice = new Dictionary<ComponenteVisual, int>();
        foreach (var visual in controlador.Visuales)
        {
            if (!GodotObject.IsInstanceValid(visual)) continue;
            indice[visual] = proyecto.componentes.Count;

            var dto = new ComponenteDTO
            {
                tipo = visual.Def.Id,
                pos = Aplanar(visual.GlobalPosition),
                rot = Aplanar(visual.RotationDegrees),
            };

            // (1) Las stats del catálogo: automáticas, sin saber de qué son.
            foreach (var stat in visual.Def.Stats)
            {
                try { dto.stats[stat.Etiqueta] = stat.Leer(visual.Logica); }
                catch (Exception e) { GD.PushWarning($"Guardado: falló la stat '{stat.Etiqueta}' de {visual.Def.Id}: {e.Message}"); }
            }

            // (2) El estado vivo propio del tipo. Arranca de lo que vino en el
            //     archivo original, así las claves desconocidas sobreviven.
            var estado = Clonar(visual.EstadoPreservado);
            try { visual.Def.GuardarEstado?.Invoke(visual.Logica, estado); }
            catch (Exception e) { GD.PushWarning($"Guardado: falló el estado de {visual.Def.Id}: {e.Message}"); }
            dto.estado = estado.Datos;

            proyecto.componentes.Add(dto);
        }

        foreach (var cable in controlador.Cables)
        {
            // Los contactos no se guardan: los regenera ContactoEntreBornes
            // a partir de las posiciones, que sí se guardan.
            if (cable.EsContacto) continue;
            if (!GodotObject.IsInstanceValid(cable)) continue;
            var a = UbicarBorne(cable.BorneA, indice);
            var b = UbicarBorne(cable.BorneB, indice);
            if (a == null || b == null) continue; // cable colgando de algo ya borrado

            var props = Clonar(cable.EstadoPreservado);
            try { cable.GuardarEstado(props); }
            catch (Exception e) { GD.PushWarning($"Guardado: falló un cable: {e.Message}"); }

            proyecto.cables.Add(new CableDTO
            {
                compA = a.Value.comp, borneA = a.Value.borne,
                compB = b.Value.comp, borneB = b.Value.borne,
                fisico = cable.EsFisico,
                props = props.Datos,
            });
        }

        // (3) Los aportes registrados (mundo, y lo que sumes en el futuro),
        //     más las secciones desconocidas que traía el archivo original.
        proyecto.extras = new Dictionary<string, JsonElement>(_extrasDesconocidos);
        foreach (var (clave, aporte) in RegistroGuardado.Aportes)
        {
            var estado = new EstadoGuardado();
            try { aporte.Guardar(estado); }
            catch (Exception e) { GD.PushWarning($"Guardado: falló el aporte '{clave}': {e.Message}"); continue; }
            if (!estado.Vacia)
            {
                proyecto.extras[clave] = JsonSerializer.SerializeToElement(estado.Datos, Opciones);
            }
        }

        return JsonSerializer.Serialize(proyecto, Opciones);
    }

    // =====================================================================
    //  Cargar
    // =====================================================================

    /// <summary>
    /// Borra el circuito actual y lo reemplaza por el del JSON. Tira excepción
    /// si el texto está roto. Devuelve los tipos que este build NO conoce: los
    /// componentes de esos tipos NO se crearon y el que abrió el archivo tiene
    /// que enterarse (antes se perdían con un PushWarning que sólo veía el
    /// depurador).
    /// </summary>
    public static List<string> Aplicar(ControladorConstruccion controlador, string json)
    {
        var tiposFaltantes = new List<string>();
        var proyecto = JsonSerializer.Deserialize<ProyectoDTO>(json, Opciones)
                       ?? throw new Exception("El archivo está vacío o no es un proyecto válido.");

        controlador.LimpiarCircuito();

        // --- Componentes ---
        // Si falta un tipo se deja un hueco (null) para NO desalinear los
        // índices con los que los cables referencian a los componentes.
        var creados = new List<ComponenteVisual?>(proyecto.componentes.Count);
        foreach (var dto in proyecto.componentes)
        {
            var def = CatalogoComponentes.PorId(dto.tipo);
            if (def == null)
            {
                GD.PushWarning($"Proyecto: no existe el componente '{dto.tipo}' en este build, se saltea.");
                if (!tiposFaltantes.Contains(dto.tipo)) tiposFaltantes.Add(dto.tipo);
                creados.Add(null);
                continue;
            }

            var visual = controlador.CrearComponenteCargado(def, Vector(dto.pos), Vector(dto.rot));

            foreach (var (etiqueta, valor) in dto.stats)
            {
                var stat = def.Stats.Find(s => s.Etiqueta == etiqueta);
                if (stat == null) continue; // stat de otra versión: se ignora sin drama
                try { stat.Escribir(visual.Logica, valor); }
                catch (Exception e) { GD.PushWarning($"Proyecto: falló la stat '{etiqueta}': {e.Message}"); }
            }

            var estado = new EstadoGuardado(dto.estado);
            visual.EstadoPreservado = estado;   // para preservar lo que no entendemos
            try { def.CargarEstado?.Invoke(visual.Logica, estado); }
            catch (Exception e) { GD.PushWarning($"Proyecto: falló el estado de '{dto.tipo}': {e.Message}"); }

            visual.ActualizarDesdeLogica();
            creados.Add(visual);
        }

        // --- Cables ---
        foreach (var dto in proyecto.cables)
        {
            if (dto.compA < 0 || dto.compA >= creados.Count) continue;
            if (dto.compB < 0 || dto.compB >= creados.Count) continue;
            if (creados[dto.compA] is not ComponenteVisual va) continue;
            if (creados[dto.compB] is not ComponenteVisual vb) continue;

            var borneA = BornePorClave(va, dto.borneA);
            var borneB = BornePorClave(vb, dto.borneB);
            if (borneA == null || borneB == null)
            {
                GD.PushWarning("Proyecto: un cable apuntaba a un borne que ya no existe, se saltea.");
                continue;
            }

            var cable = controlador.CrearCableCargado(borneA, borneB, dto.fisico);
            var props = new EstadoGuardado(dto.props);
            cable.EstadoPreservado = props;
            try { cable.CargarEstado(props); }
            catch (Exception e) { GD.PushWarning($"Proyecto: falló un cable: {e.Message}"); }
        }

        // --- Extras (aportes registrados) ---
        _extrasDesconocidos = new Dictionary<string, JsonElement>();
        foreach (var (clave, valor) in proyecto.extras)
        {
            if (!RegistroGuardado.TieneAporte(clave))
            {
                _extrasDesconocidos[clave] = valor; // de otra versión: se preserva tal cual
            }
        }
        foreach (var (clave, aporte) in RegistroGuardado.Aportes)
        {
            if (!proyecto.extras.TryGetValue(clave, out var valor) || valor.ValueKind != JsonValueKind.Object) continue;
            var dic = new Dictionary<string, JsonElement>();
            foreach (var prop in valor.EnumerateObject()) dic[prop.Name] = prop.Value;
            try { aporte.Cargar(new EstadoGuardado(dic)); }
            catch (Exception e) { GD.PushWarning($"Proyecto: falló el aporte '{clave}': {e.Message}"); }
        }

        // Los contactos no se guardan: se regeneran de las posiciones, que sí.
        controlador.GeometriaCambiada();

        ProyectoCargado?.Invoke();
        return tiposFaltantes;
    }

    // =====================================================================
    //  Diálogos de archivo (nativos del sistema, como el de .hex)
    // =====================================================================

    private static FileDialog? _dlgGuardar;
    private static FileDialog? _dlgCargar;
    private static AcceptDialog? _cartel;
    private static ControladorConstruccion? _controlador;

    /// <summary>Abre el selector "Guardar como…" para escribir el circuito a un .json.</summary>
    public static void AbrirGuardar(ControladorConstruccion controlador, Node contexto)
    {
        _controlador = controlador;
        if (_dlgGuardar == null || !GodotObject.IsInstanceValid(_dlgGuardar))
        {
            _dlgGuardar = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Title = "Guardar proyecto",
                Filters = new[] { "*.json ; Proyecto ElectricSim" },
                CurrentFile = "circuito.json",
            };
            _dlgGuardar.FileSelected += Guardar;
            contexto.GetTree().Root.AddChild(_dlgGuardar);
        }
        _dlgGuardar.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>Abre el selector para cargar un proyecto .json (reemplaza el circuito actual).</summary>
    public static void AbrirCargar(ControladorConstruccion controlador, Node contexto)
    {
        _controlador = controlador;
        if (_dlgCargar == null || !GodotObject.IsInstanceValid(_dlgCargar))
        {
            _dlgCargar = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Title = "Cargar proyecto",
                Filters = new[] { "*.json ; Proyecto ElectricSim" },
            };
            _dlgCargar.FileSelected += Cargar;
            contexto.GetTree().Root.AddChild(_dlgCargar);
        }
        _dlgCargar.PopupCentered(new Vector2I(900, 600));
    }

    private static void Guardar(string ruta)
    {
        if (_controlador == null) return;
        if (!ruta.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ruta += ".json";
        try
        {
            System.IO.File.WriteAllText(ruta, Serializar(_controlador));
        }
        catch (Exception e)
        {
            Avisar("No se pudo guardar", $"No se pudo guardar el proyecto:\n{e.Message}");
        }
    }

    private static void Cargar(string ruta)
    {
        if (_controlador == null) return;
        try
        {
            var faltantes = Aplicar(_controlador, System.IO.File.ReadAllText(ruta));
            if (faltantes.Count > 0)
            {
                Avisar("Proyecto cargado parcialmente",
                    "El proyecto se abrió, pero esta versión no reconoce los siguientes tipos de componente:\n\n" +
                    "  · " + string.Join("\n  · ", faltantes) +
                    "\n\nEsos componentes no fueron creados y sus cables quedaron desconectados. " +
                    "Si vuelve a guardar sobre el mismo archivo, esas piezas se perderán definitivamente.");
            }
        }
        catch (JsonException e)
        {
            Avisar("No se pudo cargar", $"El archivo seleccionado no corresponde a un proyecto válido.\n\n{e.Message}");
        }
        catch (Exception e)
        {
            Avisar("No se pudo cargar", $"No se pudo cargar el proyecto:\n{e.Message}");
        }
    }

    private static void Avisar(string titulo, string mensaje)
    {
        var raiz = (_dlgCargar ?? _dlgGuardar)?.GetTree()?.Root;
        if (raiz == null) return;
        if (_cartel == null || !GodotObject.IsInstanceValid(_cartel))
        {
            _cartel = new AcceptDialog();
            raiz.AddChild(_cartel);
        }
        _cartel.Title = titulo;
        _cartel.DialogText = mensaje;
        _cartel.PopupCentered();
    }

    // =====================================================================
    //  Ayudantes
    // =====================================================================

    private static float[] Aplanar(Vector3 v) => new[] { v.X, v.Y, v.Z };

    private static Vector3 Vector(float[]? a) =>
        a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    /// <summary>Copia una bolsa (para no pisar la preservada del objeto vivo mientras se serializa).</summary>
    private static EstadoGuardado Clonar(EstadoGuardado origen) =>
        new(new Dictionary<string, JsonElement>(origen.Datos));

    /// <summary>Encuentra el componente dueño de un borne y su clave ("A"/"B"/"X#").</summary>
    private static (int comp, string borne)? UbicarBorne(BorneVisual borne, Dictionary<ComponenteVisual, int> indice)
    {
        if (!GodotObject.IsInstanceValid(borne)) return null;
        foreach (var (visual, i) in indice)
        {
            if (borne == visual.BorneA) return (i, "A");
            if (borne == visual.BorneB) return (i, "B");
            for (int x = 0; x < visual.BornesExtra.Count; x++)
            {
                if (borne == visual.BornesExtra[x]) return (i, "X" + x);
            }
        }
        return null;
    }

    /// <summary>Resuelve una clave de borne ("A"/"B"/"X#") al BorneVisual del componente.</summary>
    private static BorneVisual? BornePorClave(ComponenteVisual visual, string clave)
    {
        if (clave == "A") return visual.BorneA;
        if (clave == "B") return visual.BorneB;
        if (clave.StartsWith('X') && int.TryParse(clave.AsSpan(1), out int i) &&
            i >= 0 && i < visual.BornesExtra.Count)
        {
            return visual.BornesExtra[i];
        }
        return null;
    }

    // =====================================================================
    //  DTOs (la forma del archivo)
    // =====================================================================

    private class ProyectoDTO
    {
        public int version { get; set; } = VersionFormato;
        public List<ComponenteDTO> componentes { get; set; } = new();
        public List<CableDTO> cables { get; set; } = new();
        public Dictionary<string, JsonElement> extras { get; set; } = new();
    }

    private class ComponenteDTO
    {
        public string tipo { get; set; } = "";              // Def.Id, ej "led.real"
        public float[] pos { get; set; } = new float[3];    // posición global
        public float[] rot { get; set; } = new float[3];    // rotación X/Y/Z en grados
        public Dictionary<string, double> stats { get; set; } = new();
        public Dictionary<string, JsonElement> estado { get; set; } = new();
    }

    private class CableDTO
    {
        public int compA { get; set; }
        public string borneA { get; set; } = "A";
        public int compB { get; set; }
        public string borneB { get; set; } = "B";
        public bool fisico { get; set; }

        /// <summary>
        /// Todo lo demás del cable (comba, grosor, color, dobleces, lo que
        /// venga) va suelto en la raíz del objeto: lo escribe y lo lee
        /// CableVisual, no este archivo. JsonExtensionData es justamente lo
        /// que hace que las propiedades nuevas viajen sin tocar el DTO.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> props { get; set; } = new();
    }
}
