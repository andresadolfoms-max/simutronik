using Godot;
// Con los using implicitos de .NET (System.IO) 'FileAccess' es ambiguo con la de Godot.
using FileAccess = Godot.FileAccess;

namespace ElectricSim.Game;

/// <summary>
/// Cómo se acomoda la imagen sobre la mesa (que es cuadrada, 14×14).
/// </summary>
public enum ModoMesa
{
    /// <summary>Llena la mesa SIN deformar la imagen: recorta lo que sobra, al centro.</summary>
    Cubrir = 0,

    /// <summary>La estira para que entre entera: se deforma si no es cuadrada.</summary>
    Estirar = 1,

    /// <summary>Se repite como baldosas. Es lo que conviene para texturas (madera, mármol, tela).</summary>
    Mosaico = 2,
}

/// <summary>
/// Cambiar la imagen de la mesa. Se elige de una LISTA formada por la mesa de
/// madera de siempre más todas las imágenes que haya en una carpeta; para
/// sumar más, el botón "Abrir carpeta" abre esa carpeta en el explorador de
/// archivos y se copian ahí las imágenes que se quieran.
///
/// Dónde vive la carpeta: `user://mesas`, o sea en los datos de usuario de
/// Godot (en Windows: %APPDATA%\Godot\app_userdata\&lt;proyecto&gt;\mesas). Se usa
/// esa y no una carpeta junto al proyecto porque es la única ubicación
/// donde SIEMPRE se puede escribir, también con el juego exportado. El
/// diálogo muestra la ruta real para que no haya que adivinarla.
///
/// Es sólo capa visual: cambia la textura de un material. La mesa sigue
/// siendo el mismo plano con el mismo colisionador, así que no cambia nada de
/// cómo se apoyan los componentes ni de la simulación.
///
/// La elección se RECUERDA entre sesiones (user://ajustes_mesa.cfg) pero NO
/// viaja dentro de los proyectos guardados: cargar un circuito no te cambia
/// la mesa de golpe, y un proyecto que apunte a una imagen que no existe en
/// otra computadora no queda roto.
/// </summary>
public sealed class SelectorMesa
{
    /// <summary>Carpeta (virtual de Godot) de donde se leen las imágenes.</summary>
    public const string CarpetaVirtual = "user://mesas";

    private const string ArchivoAjustes = "user://ajustes_mesa.cfg";

    /// <summary>Imágenes de más de este lado se achican al cargarlas: una foto de 12000 píxeles no tiene sentido en una mesa y gasta memoria de video.</summary>
    private const int LadoMaximo = 4096;

    private static readonly Vector2I TamMiniatura = new(128, 96);

    /// <summary>Formatos que Godot sabe leer de un archivo suelto.</summary>
    public static readonly string[] Extensiones = { "png", "jpg", "jpeg", "webp", "bmp", "tga", "svg" };

    // --- la mesa de siempre (para poder volver a ella) ---
    private StandardMaterial3D _material = null!;
    private Texture2D _texturaBase = null!;
    private Color _colorBase;
    private Vector3 _escalaBase;

    // --- lo que está puesto ahora ---
    private string? _archivoActual;           // null = la mesa de madera por defecto
    private ModoMesa _modo = ModoMesa.Cubrir;
    private float _repeticiones = 4f;
    private Texture2D? _texturaActual;
    private Vector2 _tamanoActual;

    // --- el diálogo ---
    private AcceptDialog? _dialogo;
    private ItemList _lista = null!;
    private OptionButton _selectorModo = null!;
    private SpinBox _campoRepeticiones = null!;
    private Label _estado = null!;
    private readonly Dictionary<string, (ulong Fecha, Texture2D? Miniatura)> _miniaturas = new();

    /// <summary>La ruta REAL de la carpeta en esta computadora (para mostrarla y abrirla).</summary>
    public static string RutaCarpeta => ProjectSettings.GlobalizePath(CarpetaVirtual);

    // =====================================================================
    //  Arranque
    // =====================================================================

    /// <summary>Se llama una vez, con el material y la textura de la mesa ya construidos.</summary>
    public void Init(StandardMaterial3D material, Texture2D texturaBase)
    {
        _material = material;
        _texturaBase = texturaBase;
        _colorBase = material.AlbedoColor;
        _escalaBase = material.Uv1Scale;
        AsegurarCarpeta();
    }

    /// <summary>Crea la carpeta (y un LEEME adentro) si todavía no existe.</summary>
    public static void AsegurarCarpeta()
    {
        DirAccess.MakeDirRecursiveAbsolute(CarpetaVirtual);

        string leeme = $"{CarpetaVirtual}/LEEME.txt";
        if (FileAccess.FileExists(leeme)) return;

        using var archivo = FileAccess.Open(leeme, FileAccess.ModeFlags.Write);
        archivo?.StoreString(
            "Coloque aquí las imágenes para personalizar la superficie de la mesa en ElectricSim.\n\n" +
            "Formatos compatibles: PNG, JPG, WEBP, BMP, TGA y SVG.\n" +
            "Luego, dentro del simulador, seleccione: Mesa → Actualizar lista.\n\n" +
            "Recomendaciones de ajuste:\n" +
            " - Fotografía o imagen completa: utilice el modo \"Cubrir\" o \"Estirar\".\n" +
            " - Textura repetible (madera, mármol, tela): utilice \"Mosaico\" y configure las repeticiones.\n" +
            " - Las imágenes que superen 4096 píxeles de lado se redimensionan automáticamente.\n");
    }

    /// <summary>Abre la carpeta en el explorador de archivos del sistema.</summary>
    public static void AbrirCarpeta()
    {
        AsegurarCarpeta();
        OS.ShellOpen(RutaCarpeta);
    }

    // =====================================================================
    //  Archivos
    // =====================================================================

    /// <summary>Nombres de las imágenes que hay en la carpeta, ordenados.</summary>
    public static List<string> ListarImagenes()
    {
        var resultado = new List<string>();
        using var carpeta = DirAccess.Open(CarpetaVirtual);
        if (carpeta == null) return resultado;

        foreach (string nombre in carpeta.GetFiles())
        {
            string extension = System.IO.Path.GetExtension(nombre).TrimStart('.').ToLowerInvariant();
            if (Array.IndexOf(Extensiones, extension) >= 0) resultado.Add(nombre);
        }
        resultado.Sort(StringComparer.OrdinalIgnoreCase);
        return resultado;
    }

    /// <summary>Lee una imagen de la carpeta y la convierte en textura. Devuelve null (y el motivo) si no se pudo.</summary>
    private static Texture2D? CargarImagen(string archivo, out Vector2 tamano, out string error)
    {
        tamano = Vector2.One;
        error = "";

        var imagen = Image.LoadFromFile($"{CarpetaVirtual}/{archivo}");
        if (imagen == null || imagen.IsEmpty())
        {
            error = $"No se pudo leer \"{archivo}\": el archivo está dañado o no es una imagen válida.";
            return null;
        }

        int ancho = imagen.GetWidth();
        int alto = imagen.GetHeight();
        int lado = Math.Max(ancho, alto);
        if (lado > LadoMaximo)
        {
            float factor = LadoMaximo / (float)lado;
            imagen.Resize(Math.Max(1, (int)(ancho * factor)), Math.Max(1, (int)(alto * factor)), Image.Interpolation.Lanczos);
            ancho = imagen.GetWidth();
            alto = imagen.GetHeight();
        }

        // Con mipmaps la mesa no "brilla" ni se ve granulada cuando la cámara está lejos.
        imagen.GenerateMipmaps();

        tamano = new Vector2(ancho, alto);
        return ImageTexture.CreateFromImage(imagen);
    }

    /// <summary>Una miniatura chiquita para la lista. Se guarda en memoria mientras el archivo no cambie.</summary>
    private Texture2D? Miniatura(string archivo)
    {
        string ruta = $"{CarpetaVirtual}/{archivo}";
        ulong fecha = FileAccess.GetModifiedTime(ruta);

        if (_miniaturas.TryGetValue(archivo, out var guardada) && guardada.Fecha == fecha)
        {
            return guardada.Miniatura;
        }

        Texture2D? miniatura = null;
        var imagen = Image.LoadFromFile(ruta);
        if (imagen != null && !imagen.IsEmpty())
        {
            float factor = Math.Min(TamMiniatura.X / (float)imagen.GetWidth(), TamMiniatura.Y / (float)imagen.GetHeight());
            imagen.Resize(
                Math.Max(1, (int)(imagen.GetWidth() * factor)),
                Math.Max(1, (int)(imagen.GetHeight() * factor)),
                Image.Interpolation.Bilinear);
            miniatura = ImageTexture.CreateFromImage(imagen);
        }

        _miniaturas[archivo] = (fecha, miniatura);
        return miniatura;
    }

    /// <summary>Una miniatura lisa del color de la madera, para el ítem de la mesa por defecto.</summary>
    private Texture2D MiniaturaBase()
    {
        var imagen = Image.CreateEmpty(TamMiniatura.X, TamMiniatura.Y, false, Image.Format.Rgb8);
        imagen.Fill(_colorBase);
        return ImageTexture.CreateFromImage(imagen);
    }

    // =====================================================================
    //  Aplicar
    // =====================================================================

    /// <summary>
    /// Pone una imagen en la mesa (null = la mesa de madera de siempre).
    /// Devuelve false si la imagen no se pudo leer: en ese caso la mesa queda como estaba.
    /// </summary>
    public bool Aplicar(string? archivo, bool recargar = true)
    {
        if (archivo == null)
        {
            _material.AlbedoTexture = _texturaBase;
            _material.AlbedoColor = _colorBase;
            _material.Uv1Scale = _escalaBase;
            _material.Uv1Offset = Vector3.Zero;
            _archivoActual = null;
            _texturaActual = null;
            GuardarPreferencia();
            return true;
        }

        if (recargar || _texturaActual == null || archivo != _archivoActual)
        {
            var textura = CargarImagen(archivo, out var tamano, out var error);
            if (textura == null)
            {
                Informar(error);
                return false;
            }
            _texturaActual = textura;
            _tamanoActual = tamano;
        }

        _archivoActual = archivo;
        _material.AlbedoTexture = _texturaActual;
        _material.AlbedoColor = Colors.White; // la imagen con sus colores reales, sin el tinte marrón de la madera
        AplicarAjuste();
        GuardarPreferencia();
        return true;
    }

    /// <summary>Acomoda la imagen en la mesa según el modo, tocando sólo el escalado de las UV del material.</summary>
    private void AplicarAjuste()
    {
        switch (_modo)
        {
            case ModoMesa.Estirar:
                _material.Uv1Scale = Vector3.One;
                _material.Uv1Offset = Vector3.Zero;
                break;

            case ModoMesa.Mosaico:
                _material.Uv1Scale = new Vector3(_repeticiones, _repeticiones, 1f);
                _material.Uv1Offset = Vector3.Zero;
                break;

            default: // Cubrir: el cuadrado central de la imagen, sin deformarla
            {
                float aspecto = _tamanoActual.Y > 0 ? _tamanoActual.X / _tamanoActual.Y : 1f;
                if (aspecto >= 1f)
                {
                    // más ancha que alta: se ve sólo la franja central de ancho 1/aspecto
                    _material.Uv1Scale = new Vector3(1f / aspecto, 1f, 1f);
                    _material.Uv1Offset = new Vector3((1f - 1f / aspecto) / 2f, 0f, 0f);
                }
                else
                {
                    _material.Uv1Scale = new Vector3(1f, aspecto, 1f);
                    _material.Uv1Offset = new Vector3(0f, (1f - aspecto) / 2f, 0f);
                }
                break;
            }
        }
    }

    // =====================================================================
    //  Recordar la elección
    // =====================================================================

    private void GuardarPreferencia()
    {
        var ajustes = new ConfigFile();
        ajustes.SetValue("mesa", "archivo", _archivoActual ?? "");
        ajustes.SetValue("mesa", "modo", (int)_modo);
        ajustes.SetValue("mesa", "repeticiones", _repeticiones);
        ajustes.Save(ArchivoAjustes);
    }

    /// <summary>Al arrancar: vuelve a poner la última mesa elegida. Si ya no existe el archivo, queda la de madera.</summary>
    public void CargarPreferencia()
    {
        var ajustes = new ConfigFile();
        if (ajustes.Load(ArchivoAjustes) != Error.Ok) return;

        _modo = (ModoMesa)Math.Clamp((int)ajustes.GetValue("mesa", "modo", (int)ModoMesa.Cubrir), 0, 2);
        _repeticiones = Math.Clamp((float)ajustes.GetValue("mesa", "repeticiones", 4.0), 1f, 40f);

        string archivo = (string)ajustes.GetValue("mesa", "archivo", "");
        if (archivo != "" && ListarImagenes().Contains(archivo))
        {
            Aplicar(archivo);
        }
    }

    // =====================================================================
    //  El diálogo
    // =====================================================================

    /// <summary>Abre el selector. `anfitrion` es el nodo del que cuelga la ventana.</summary>
    public void Mostrar(Node anfitrion)
    {
        if (_dialogo == null || !GodotObject.IsInstanceValid(_dialogo))
        {
            ConstruirDialogo(anfitrion);
        }

        RefrescarLista();
        _dialogo!.PopupCentered(new Vector2I(640, 560));
    }

    private void ConstruirDialogo(Node anfitrion)
    {
        _dialogo = new AcceptDialog
        {
            Title = "Personalizar mesa de trabajo",
            OkButtonText = "Listo",
            Exclusive = false, // se puede orbitar la cámara y ver cómo queda mientras está abierto
        };

        var raiz = new VBoxContainer { CustomMinimumSize = new Vector2(600, 460) };
        raiz.AddThemeConstantOverride("separation", 8);

        raiz.AddChild(new Label
        {
            Text = "Seleccione una imagen para la mesa. Para añadir más, haga clic en \"Abrir carpeta de mesas\", copie sus archivos " +
                   "(PNG, JPG, WEBP, BMP, TGA o SVG) y pulse \"Actualizar lista\".",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(580, 0),
        });

        _lista = new ItemList
        {
            IconMode = ItemList.IconModeEnum.Top,
            MaxColumns = 0, // automático: tantas columnas como entren
            FixedIconSize = TamMiniatura,
            SameColumnWidth = true,
            SelectMode = ItemList.SelectModeEnum.Single,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(580, 260),
        };
        // Elegir una imagen la aplica al instante: se ve cómo queda detrás del diálogo.
        _lista.ItemSelected += indice =>
        {
            string nombre = _lista.GetItemMetadata((int)indice).AsString();
            if (!Aplicar(nombre == "" ? null : nombre))
            {
                MarcarActual(); // no se pudo leer: la lista vuelve a mostrar lo que sigue puesto
            }
            else
            {
                Informar(nombre == "" ? "Mesa de madera por defecto." : $"Mesa: {nombre}");
            }
            ActualizarControlesDeAjuste();
        };
        raiz.AddChild(_lista);

        // --- ajuste ---
        var filaAjuste = new HBoxContainer();
        filaAjuste.AddThemeConstantOverride("separation", 8);
        filaAjuste.AddChild(new Label { Text = "Ajuste:" });

        _selectorModo = new OptionButton { TooltipText = "Modo de proyección de la imagen sobre la superficie cuadrada de la mesa" };
        _selectorModo.AddItem("Cubrir (recorta sin deformar)");
        _selectorModo.AddItem("Estirar (ajusta completa; puede deformar)");
        _selectorModo.AddItem("Mosaico (patrón repetido)");
        _selectorModo.ItemSelected += indice =>
        {
            _modo = (ModoMesa)(int)indice;
            ActualizarControlesDeAjuste();
            if (_archivoActual != null)
            {
                Aplicar(_archivoActual, recargar: false);
            }
            else
            {
                GuardarPreferencia();
            }
        };
        filaAjuste.AddChild(_selectorModo);

        filaAjuste.AddChild(new Label { Text = "Repeticiones:" });
        _campoRepeticiones = new SpinBox
        {
            MinValue = 1,
            MaxValue = 40,
            Step = 0.5,
            CustomMinimumSize = new Vector2(90, 0),
            TooltipText = "Número de repeticiones de la textura a lo largo de la mesa (solo en modo Mosaico)",
        };
        _campoRepeticiones.ValueChanged += valor =>
        {
            _repeticiones = (float)valor;
            if (_archivoActual != null && _modo == ModoMesa.Mosaico)
            {
                Aplicar(_archivoActual, recargar: false);
            }
        };
        filaAjuste.AddChild(_campoRepeticiones);
        raiz.AddChild(filaAjuste);

        // --- carpeta ---
        var filaCarpeta = new HBoxContainer();
        filaCarpeta.AddThemeConstantOverride("separation", 8);

        var botonAbrir = new Button
        {
            Text = "Abrir carpeta de mesas",
            TooltipText = "Abre la carpeta en el explorador del sistema para copiar nuevas imágenes de textura",
        };
        botonAbrir.Pressed += AbrirCarpeta;
        filaCarpeta.AddChild(botonAbrir);

        var botonActualizar = new Button
        {
            Text = "Actualizar lista",
            TooltipText = "Recarga el contenido de la carpeta después de agregar o quitar imágenes",
        };
        botonActualizar.Pressed += () =>
        {
            _miniaturas.Clear();
            RefrescarLista();
        };
        filaCarpeta.AddChild(botonActualizar);
        raiz.AddChild(filaCarpeta);

        // --- estado y ruta ---
        _estado = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(580, 0),
        };
        raiz.AddChild(_estado);

        _dialogo.AddChild(raiz);
        anfitrion.AddChild(_dialogo);
    }

    /// <summary>Vuelve a leer la carpeta y arma la lista con miniaturas.</summary>
    private void RefrescarLista()
    {
        _lista.Clear();

        int indiceBase = _lista.AddItem("Mesa de madera\n(por defecto)", MiniaturaBase());
        _lista.SetItemMetadata(indiceBase, "");

        var imagenes = ListarImagenes();
        foreach (string nombre in imagenes)
        {
            int indice = _lista.AddItem(nombre, Miniatura(nombre));
            _lista.SetItemMetadata(indice, nombre);
        }

        // Si la imagen puesta ya no está en la carpeta, no se puede seguir mostrando como elegida.
        if (_archivoActual != null && !imagenes.Contains(_archivoActual))
        {
            Informar($"\"{_archivoActual}\" ya no se encuentra en la carpeta; la mesa conservará la textura actual hasta que seleccione otra.");
        }
        else
        {
            Informar(imagenes.Count == 0
                ? "No hay imágenes personalizadas todavía. Utilice \"Abrir carpeta de mesas\" para agregar archivos."
                : $"{imagenes.Count} imagen(es) en la carpeta.");
        }

        _selectorModo.Selected = (int)_modo;
        _campoRepeticiones.SetValueNoSignal(_repeticiones);
        MarcarActual();
        ActualizarControlesDeAjuste();
    }

    /// <summary>Deja marcado en la lista lo que está puesto ahora.</summary>
    private void MarcarActual()
    {
        for (int i = 0; i < _lista.ItemCount; i++)
        {
            string nombre = _lista.GetItemMetadata(i).AsString();
            if ((nombre == "" && _archivoActual == null) || nombre == _archivoActual)
            {
                _lista.Select(i);
                _lista.EnsureCurrentIsVisible();
                return;
            }
        }
    }

    /// <summary>El ajuste no tiene sentido con la mesa de madera; las repeticiones sólo con Mosaico.</summary>
    private void ActualizarControlesDeAjuste()
    {
        bool conImagen = _archivoActual != null;
        _selectorModo.Disabled = !conImagen;
        _campoRepeticiones.Editable = conImagen && _modo == ModoMesa.Mosaico;
    }

    private void Informar(string texto)
    {
        if (_estado == null || !GodotObject.IsInstanceValid(_estado)) return;
        _estado.Text = $"{texto}\nCarpeta: {RutaCarpeta}";
    }
}
