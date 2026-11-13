using Godot;
// Con los using implicitos de .NET (System.IO) 'FileAccess' es ambiguo con la de Godot.
using FileAccess = Godot.FileAccess;

namespace ElectricSim.Game;

/// <summary>
/// La pantalla de inicio: degradado oscuro horizontal, tres botones a la izquierda
/// (CARGAR PROYECTO, NUEVO PROYECTO, OPCIONES), un visor de videos ajustado a la derecha
/// y el logotipo de SIMUTRONIK en la esquina inferior derecha.
///
/// DE DÓNDE SALEN LOS VIDEOS. De la carpeta `user://inicio_videos` (en Windows:
/// %APPDATA%\Godot\app_userdata\ElectricSim\inicio_videos). Se meten ahí a
/// mano. Se reproducen uno tras otro, en orden aleatorio o alfabético, y
/// cuando termina el último vuelve a empezar.
///
/// EL FORMATO. Godot sólo sabe reproducir video en Ogg Theora (.ogv): no
/// existe forma de que lea MP4, y los videos que graba el propio simulador
/// salen en MP4. Por eso hay dos caminos:
///  1) Un `.ogv` se usa directo.
///  2) Cualquier MP4/MOV/MKV/WEBM/AVI... se CONVIERTE SOLO a .ogv la primera
///     vez, en segundo plano, con el mismo FFmpeg que usa el grabador de
///     video. La conversión se guarda en `_convertidos/` y no se repite
///     mientras el original no cambie. Se escala preservando relación de aspecto
///     dentro de 1280×720 a 30 cuadros, que es lo que un decodificador por CPU
///     como el de Theora aguanta sin trabarse.
///
/// MIENTRAS ESTÁ EL MENÚ, el simulador de abajo se queda quieto: sin ticks,
/// sin cámara, sin atajos de teclado y sin dibujar el 3D. Main se encarga
/// de eso (MostrarInicio / OcultarInicio); esta clase sólo dibuja el menú.
/// </summary>
public partial class PantallaInicio : CanvasLayer
{
    /// <summary>Carpeta (virtual de Godot) donde se meten los videos.</summary>
    public const string CarpetaVirtual = "user://inicio_videos";

    private const string CarpetaConvertidos = "user://inicio_videos/_convertidos";
    private const string ArchivoAjustes = "user://ajustes_inicio.cfg";
    private const string RutaLogo = "res://assets/logo_simutronik.png";

    /// <summary>Formatos que se convierten solos. (.ogv no está acá: ése se usa tal cual.)</summary>
    private static readonly string[] ExtensionesConvertibles =
        { "mp4", "mov", "mkv", "webm", "avi", "m4v", "wmv", "flv", "mpg", "mpeg" };

    private static readonly Color ColorFondoIzquierda = Colors.Black;                                   // #000000
    private static readonly Color ColorFondoDerecha = new(10f / 255f, 8f / 255f, 81f / 255f);           // #0A0851
    private static readonly Color ColorBotonNormal = new(15f / 255f, 6f / 255f, 72f / 255f);            // #0F0648
    private static readonly Color ColorBotonHover = new(27f / 255f, 16f / 255f, 110f / 255f);
    private static readonly Color ColorBotonPresionado = new(43f / 255f, 30f / 255f, 150f / 255f);
    private static readonly Color ColorTexto = Colors.White;
    private static readonly Color ColorBorde = Colors.White;

    public event Action? CrearNuevoPedido;
    public event Action? CargarPedido;

    public bool EstaAbierta => Visible;

    // --- interfaz ---
    private Control _raiz = null!;
    private Panel _marcoVideo = null!;
    private AspectRatioContainer _contenedorVideo = null!;
    private VideoStreamPlayer _reproductor = null!;
    private VBoxContainer _menu = null!;
    private readonly List<Button> _botones = new();
    private StyleBoxFlat _estiloNormal = null!;
    private StyleBoxFlat _estiloHover = null!;
    private StyleBoxFlat _estiloPresionado = null!;
    private StyleBoxFlat _estiloFoco = null!;
    private Font _fuenteMenu = null!;
    private Label _aviso = null!;
    private AcceptDialog? _dialogoOpciones;

    // --- ajustes (se recuerdan entre sesiones) ---
    private bool _aleatorio = true;
    private bool _silenciar;
    private bool _convertirAuto = true;
    private bool _pantallaCompleta;

    // --- lista de reproducción ---
    private readonly List<string> _lista = new();
    private int _indice;
    private readonly Random _azar = new();
    private bool _ratioListo;
    private float _segundosSinImagen;

    // --- conversión en segundo plano ---
    private readonly Queue<(string Origen, string Destino)> _cola = new();
    private readonly HashSet<string> _enProceso = new();   // en cola o convirtiéndose
    private readonly HashSet<string> _fallidos = new();    // no se reintentan en cada apertura
    private int _pidConversion = -1;
    private string _origenActual = "";
    private string _destinoActual = "";
    private int _totalAConvertir;
    private int _hechos;
    private bool _faltaFfmpeg;
    private string _ultimoError = "";

    // =====================================================================
    //  Ciclo de vida
    // =====================================================================

    public override void _Ready()
    {
        Layer = 100; // por encima de TODA la interfaz del simulador
        AsegurarCarpetas();
        CargarAjustes();
        ConstruirInterfaz();
        Visible = false;
    }

    public override void _ExitTree()
    {
        // Que no quede un ffmpeg huérfano convirtiendo si se cierra el programa.
        if (_pidConversion != -1 && OS.IsProcessRunning(_pidConversion)) OS.Kill(_pidConversion);
    }

    /// <summary>Muestra el menú y arranca los videos de fondo.</summary>
    public void Abrir()
    {
        Visible = true;
        RelevarVideos();
        AjustarProporciones();
        _indice = _aleatorio && _lista.Count > 0 ? _azar.Next(_lista.Count) : 0;
        ReproducirActual();
        ActualizarAviso();
    }

    /// <summary>Oculta el menú y frena el video (para no gastar CPU en algo que no se ve).</summary>
    public void Cerrar()
    {
        Visible = false; // primero esto: así, si Stop() avisa "terminó", SiguienteVideo no relanza nada
        _reproductor.Stop();
        _reproductor.Visible = false;
    }

    // =====================================================================
    //  Carpetas
    // =====================================================================

    public static string RutaCarpeta => ProjectSettings.GlobalizePath(CarpetaVirtual);

    private static void AsegurarCarpetas()
    {
        DirAccess.MakeDirRecursiveAbsolute(CarpetaConvertidos);

        string leeme = $"{CarpetaVirtual}/LEEME.txt";
        if (FileAccess.FileExists(leeme)) return;

        using var archivo = FileAccess.Open(leeme, FileAccess.ModeFlags.Write);
        archivo?.StoreString(
            "Videos de fondo de la pantalla de inicio de ElectricSim.\n\n" +
            "Coloque aquí los archivos de video que se reproducirán en secuencia.\n\n" +
            "FORMATOS COMPATIBLES\n" +
            " - .ogv (Ogg Theora): reproducción directa (formato nativo de video en Godot).\n" +
            " - MP4, MOV, MKV, WEBM, AVI, etc.: se convierten automáticamente a .ogv la primera\n" +
            "   vez mediante ffmpeg.exe (el mismo codificador del grabador de video). Los videos MP4\n" +
            "   exportados por el simulador pueden copiarse directamente en esta carpeta.\n\n" +
            "CONVERSIÓN MANUAL OPCIONAL:\n" +
            "   ffmpeg -i entrada.mp4 -an -c:v libtheora -q:v 7 -g 1 -pix_fmt yuv420p -r 30 salida.ogv\n\n" +
            "RECOMENDADO: resolución 1280×720 (16:9), 30 FPS, sin pista de audio.\n" +
            "Resoluciones de 1080p o superiores pueden reducir el rendimiento en equipos modestos.\n");
    }

    public static void AbrirCarpeta()
    {
        AsegurarCarpetas();
        OS.ShellOpen(RutaCarpeta);
    }

    // =====================================================================
    //  Videos: relevar la carpeta, reproducir, convertir
    // =====================================================================

    /// <summary>Vuelve a leer la carpeta: arma la lista de reproducción y encola lo que haya que convertir.</summary>
    private void RelevarVideos()
    {
        _lista.Clear();

        using var carpeta = DirAccess.Open(CarpetaVirtual);
        if (carpeta == null) return;

        var archivos = new List<string>(carpeta.GetFiles());
        archivos.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string nombre in archivos)
        {
            string extension = System.IO.Path.GetExtension(nombre).TrimStart('.').ToLowerInvariant();
            string ruta = $"{CarpetaVirtual}/{nombre}";

            if (extension == "ogv")
            {
                _lista.Add(ruta);
                continue;
            }
            if (!_convertirAuto || Array.IndexOf(ExtensionesConvertibles, extension) < 0) continue;

            // El nombre del convertido lleva la fecha del original y versión del perfil
            // (_v2 = keyframes intra-only -g 1): si lo reemplazás o cambia el perfil,
            // se vuelve a convertir en vez de usar la copia vieja.
            ulong fecha = FileAccess.GetModifiedTime(ruta);
            string destino = $"{CarpetaConvertidos}/{System.IO.Path.GetFileNameWithoutExtension(nombre)}_{fecha}_v2.ogv";

            if (FileAccess.FileExists(destino))
            {
                _lista.Add(destino);
            }
            else if (!_fallidos.Contains(ruta) && _enProceso.Add(ruta))
            {
                _cola.Enqueue((ruta, destino));
                _totalAConvertir++;
            }
        }

        IniciarSiguienteConversion();
    }

    private void ReproducirActual()
    {
        if (_lista.Count == 0)
        {
            _reproductor.Stop();
            _reproductor.Stream = null;
            _reproductor.Visible = false;
            return;
        }

        _indice = Math.Clamp(_indice, 0, _lista.Count - 1);
        _contenedorVideo.Ratio = 16f / 9f;
        _reproductor.Stream = new VideoStreamTheora { File = _lista[_indice] };
        _reproductor.Volume = _silenciar ? 0f : 1f;
        _reproductor.Visible = true;
        _ratioListo = false;
        _segundosSinImagen = 0;
        _reproductor.Play();
    }

    /// <summary>Terminó un video (o falló): pasa al siguiente. Con un solo video, lo repite.</summary>
    private void SiguienteVideo()
    {
        if (!Visible) return;
        if (_lista.Count == 0) { ReproducirActual(); return; }

        if (_aleatorio && _lista.Count > 1)
        {
            int nuevo;
            do { nuevo = _azar.Next(_lista.Count); } while (nuevo == _indice);
            _indice = nuevo;
        }
        else
        {
            _indice = (_indice + 1) % _lista.Count;
        }
        ReproducirActual();
    }

    private void IniciarSiguienteConversion()
    {
        if (_pidConversion != -1 || _cola.Count == 0) return;

        string? ffmpeg = GrabadorVideo.ResolverRutaFfmpeg();
        if (ffmpeg == null)
        {
            // Sin FFmpeg no se puede convertir nada: se avisa en pantalla y se deja de intentar.
            _faltaFfmpeg = true;
            foreach (var pendiente in _cola) _enProceso.Remove(pendiente.Origen);
            _cola.Clear();
            ActualizarAviso();
            return;
        }
        _faltaFfmpeg = false;

        var (origen, destino) = _cola.Dequeue();
        _origenActual = origen;
        _destinoActual = destino;

        string entrada = ProjectSettings.GlobalizePath(origen);
        string salida = ProjectSettings.GlobalizePath(destino + ".parte");

        // Escala dentro de 1280x720 conservando la relación de aspecto original (sin recortar ni deformar)
        // y ajusta solo al múltiplo de 16 más cercano para evitar bordes raros en Theora.
        string[] argumentos =
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", entrada,
            "-an",
            "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,setsar=1,pad=ceil(iw/16)*16:ceil(ih/16)*16:(ow-iw)/2:(oh-ih)/2",
            "-r", "30",
            // -g 1 fuerza cuadros intra completos (sin P-frames): la compilación de libtheora
            // incluida en ffmpeg.exe corrompe los P-frames (unpack_block_qpis / bloques
            // negros y colores reventados al decodificar en Godot VideoStreamTheora).
            "-c:v", "libtheora", "-q:v", "7", "-g", "1", "-pix_fmt", "yuv420p",
            "-f", "ogg",
            salida,
        };

        GD.Print($"[Inicio] Convirtiendo: {ffmpeg} {string.Join(' ', argumentos)}");
        _pidConversion = OS.CreateProcess(ffmpeg, argumentos);
        if (_pidConversion == -1)
        {
            TerminarConversion(false, "No se pudo ejecutar ffmpeg.exe.");
            return;
        }
        ActualizarAviso();
    }

    /// <summary>Se llama en cada frame: ¿terminó la conversión que está corriendo?</summary>
    private void ActualizarConversion()
    {
        if (_pidConversion == -1 || OS.IsProcessRunning(_pidConversion)) return;

        // FFmpeg escribe a un archivo ".parte" y sólo si salió bien se renombra: así un
        // .ogv a medio escribir nunca entra a la lista de reproducción.
        string parte = _destinoActual + ".parte";
        bool existe = FileAccess.FileExists(parte);
        long tamano = 0;
        if (existe)
        {
            using var archivo = FileAccess.Open(parte, FileAccess.ModeFlags.Read);
            tamano = archivo != null ? (long)archivo.GetLength() : 0;
        }

        if (existe && tamano > 4096)
        {
            DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(parte), ProjectSettings.GlobalizePath(_destinoActual));
            TerminarConversion(true, "");
        }
        else
        {
            if (existe) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(parte));
            TerminarConversion(false,
                $"No se pudo convertir \"{System.IO.Path.GetFileName(_origenActual)}\". " +
                "Verifique que ffmpeg.exe incluya el codificador libtheora o realice la conversión manual (ver LEEME.txt).");
        }
    }

    private void TerminarConversion(bool bien, string error)
    {
        _pidConversion = -1;
        _enProceso.Remove(_origenActual);
        _hechos++;

        if (bien)
        {
            _lista.Add(_destinoActual);
            // Si todavía no había nada para mostrar, arranca ya con este.
            if (Visible && _lista.Count == 1) ReproducirActual();
        }
        else
        {
            _fallidos.Add(_origenActual);
            _ultimoError = error;
        }

        if (_cola.Count == 0) { _totalAConvertir = 0; _hechos = 0; }
        ActualizarAviso();
        IniciarSiguienteConversion();
    }

    public override void _Process(double delta)
    {
        ActualizarConversion();
        if (!Visible) return;

        // El video se ajusta ENTERO dentro del recuadro sin deformarse ni recortarse.
        // Su proporción real sólo se conoce cuando sale el primer cuadro.
        if (!_ratioListo && _reproductor.IsPlaying())
        {
            var textura = _reproductor.GetVideoTexture();
            if (textura != null && textura.GetWidth() > 0 && textura.GetHeight() > 0)
            {
                _contenedorVideo.Ratio = textura.GetWidth() / (float)textura.GetHeight();
                _ratioListo = true;
            }
            else
            {
                _segundosSinImagen += (float)delta;
                if (_segundosSinImagen > 3f)
                {
                    // Un video que no entrega ni un cuadro en 3 segundos está roto: se saltea.
                    if (_lista.Count > 0 && _indice < _lista.Count)
                    {
                        _ultimoError = $"No se pudo reproducir \"{System.IO.Path.GetFileName(_lista[_indice])}\"; se omitirá de la lista.";
                        _lista.RemoveAt(_indice);
                    }
                    SiguienteVideo();
                    ActualizarAviso();
                }
            }
        }
    }

    // =====================================================================
    //  Interfaz
    // =====================================================================

    private void ConstruirInterfaz()
    {
        _raiz = new Control { MouseFilter = Control.MouseFilterEnum.Stop }; // se come los clics: el simulador de abajo no los ve
        _raiz.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _raiz.Resized += AjustarProporciones;
        AddChild(_raiz);

        // Fondo con degradado horizontal oscuro (#000000 a la izquierda -> #0A0851 a la derecha).
        var gradiente = new Gradient();
        gradiente.SetColor(0, ColorFondoIzquierda);
        gradiente.SetColor(1, ColorFondoDerecha);
        var texturaFondo = new GradientTexture2D
        {
            Gradient = gradiente,
            Width = 512,
            Height = 4,
            FillFrom = Vector2.Zero,
            FillTo = Vector2.Right,
        };
        var fondo = new TextureRect
        {
            Texture = texturaFondo,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fondo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _raiz.AddChild(fondo);

        // --- recuadro de videos a la derecha (sin recortar ni deformar el video) ---
        _marcoVideo = new Panel
        {
            AnchorLeft = 0.381f,
            AnchorRight = 0.934f,
            AnchorTop = 0.149f,
            AnchorBottom = 0.820f,
            OffsetLeft = 0, OffsetRight = 0, OffsetTop = 0, OffsetBottom = 0,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var estiloMarco = new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.008f, 0.03f, 0.90f),
            BorderColor = new Color(0.18f, 0.24f, 0.62f, 0.45f),
        };
        estiloMarco.SetBorderWidthAll(1);
        _marcoVideo.AddThemeStyleboxOverride("panel", estiloMarco);
        _raiz.AddChild(_marcoVideo);

        _contenedorVideo = new AspectRatioContainer
        {
            StretchMode = AspectRatioContainer.StretchModeEnum.Fit, // entra completo sin recortarse ni estirarse
            AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
            AlignmentVertical = AspectRatioContainer.AlignmentMode.Center,
            Ratio = 16f / 9f,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _contenedorVideo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _marcoVideo.AddChild(_contenedorVideo);

        _reproductor = new VideoStreamPlayer
        {
            Expand = true,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _reproductor.Finished += SiguienteVideo;
        _contenedorVideo.AddChild(_reproductor);

        // --- los tres botones a la izquierda, en el orden y proporciones del nuevo diseño ---
        _fuenteMenu = new SystemFont
        {
            FontNames = new[] { "Agency FB", "Bahnschrift", "Segoe UI", "Arial" },
            FontWeight = 700,
            AllowSystemFallback = true,
        };
        _estiloNormal = EstiloBoton(ColorBotonNormal, ColorBorde);
        _estiloHover = EstiloBoton(ColorBotonHover, ColorBorde);
        _estiloPresionado = EstiloBoton(ColorBotonPresionado, new Color(0.75f, 0.85f, 1.0f));
        _estiloFoco = EstiloBoton(ColorBotonNormal, ColorBorde);

        _menu = new VBoxContainer
        {
            AnchorLeft = 0.094f,
            AnchorRight = 0.303f,
            AnchorTop = 0.168f,
            AnchorBottom = 0.802f,
            OffsetLeft = 0, OffsetRight = 0, OffsetTop = 0, OffsetBottom = 0,
        };
        _raiz.AddChild(_menu);

        _menu.AddChild(CrearBoton("CARGAR PROYECTO", () => CargarPedido?.Invoke()));
        _menu.AddChild(CrearBoton("NUEVO PROYECTO", () => CrearNuevoPedido?.Invoke()));
        _menu.AddChild(CrearBoton("OPCIONES", AbrirOpciones));

        // --- logotipo de SIMUTRONIK en la esquina inferior derecha ---
        Texture2D? texturaLogo = CargarTexturaLogo();
        if (texturaLogo != null)
        {
            var logo = new TextureRect
            {
                Texture = texturaLogo,
                AnchorLeft = 0.747f,
                AnchorRight = 0.977f,
                AnchorTop = 0.880f,
                AnchorBottom = 0.964f,
                OffsetLeft = 0, OffsetRight = 0, OffsetTop = 0, OffsetBottom = 0,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _raiz.AddChild(logo);
        }

        // --- avisos (conversión en curso, falta FFmpeg, no hay videos...) ---
        _aviso = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _aviso.AddThemeColorOverride("font_color", new Color(0.78f, 0.84f, 0.97f, 0.88f));
        _aviso.AddThemeColorOverride("font_outline_color", Colors.Black);
        _aviso.AddThemeConstantOverride("outline_size", 4);
        _raiz.AddChild(_aviso);
        UbicarAviso();
    }

    private static Texture2D? CargarTexturaLogo()
    {
        if (FileAccess.FileExists($"{RutaLogo}.import"))
        {
            var importada = ResourceLoader.Load<Texture2D>(RutaLogo);
            if (importada != null) return importada;
        }

        string rutaFisica = ProjectSettings.GlobalizePath(RutaLogo);
        if (!System.IO.File.Exists(rutaFisica)) return null;

        var imagen = Image.LoadFromFile(rutaFisica);
        return imagen != null && !imagen.IsEmpty() ? ImageTexture.CreateFromImage(imagen) : null;
    }

    /// <summary>Botón azul oscuro (#0F0648) con borde y tipografía blancos, fiel al menú diseñado.</summary>
    private Button CrearBoton(string texto, Action alPresionar)
    {
        var boton = new Button
        {
            Text = texto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipText = false,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        boton.AddThemeStyleboxOverride("normal", _estiloNormal);
        boton.AddThemeStyleboxOverride("hover", _estiloHover);
        boton.AddThemeStyleboxOverride("pressed", _estiloPresionado);
        boton.AddThemeStyleboxOverride("focus", _estiloFoco);
        foreach (string nombre in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            boton.AddThemeColorOverride(nombre, ColorTexto);
        }
        boton.AddThemeFontOverride("font", _fuenteMenu);

        boton.Pressed += alPresionar;
        _botones.Add(boton);
        return boton;
    }

    private static StyleBoxFlat EstiloBoton(Color fondo, Color borde)
    {
        var estilo = new StyleBoxFlat
        {
            BgColor = fondo,
            BorderColor = borde,
            AntiAliasing = true,
        };
        estilo.SetBorderWidthAll(4);
        estilo.SetCornerRadiusAll(10);
        return estilo;
    }

    /// <summary>Escala proporciones, bordes y tipografía con el tamaño de ventana (el proyecto no reescala su viewport).</summary>
    private void AjustarProporciones()
    {
        if (_menu == null || _raiz.Size.Y <= 0) return;

        float alto = _raiz.Size.Y;
        float ancho = _raiz.Size.X;

        int separacion = Math.Clamp((int)Mathf.Round(alto * 0.132f), 8, 220);
        int borde = Math.Clamp((int)Mathf.Round(alto * 0.0055f), 2, 8);
        int radio = Math.Clamp((int)Mathf.Round(alto * 0.015f), 4, 22);
        int padH = Math.Clamp((int)Mathf.Round(alto * 0.016f), 8, 28);

        _menu.AddThemeConstantOverride("separation", separacion);
        foreach (var estilo in new[] { _estiloNormal, _estiloHover, _estiloPresionado, _estiloFoco })
        {
            estilo.SetBorderWidthAll(borde);
            estilo.SetCornerRadiusAll(radio);
            estilo.ContentMarginLeft = padH;
            estilo.ContentMarginRight = padH;
        }

        int tamanoLetra = (int)Math.Clamp(alto * 0.052f, 14f, 96f);
        if (ancho > 0)
        {
            float anchoUtil = (_menu.AnchorRight - _menu.AnchorLeft) * ancho - borde * 2f - padH * 2f;
            float anchoTexto = _fuenteMenu.GetStringSize("CARGAR PROYECTO", HorizontalAlignment.Left, -1, tamanoLetra).X;
            if (anchoUtil > 20f && anchoTexto > anchoUtil)
            {
                tamanoLetra = Math.Max(12, (int)Mathf.Floor(tamanoLetra * (anchoUtil / anchoTexto)));
            }
        }
        foreach (var boton in _botones)
        {
            boton.AddThemeFontSizeOverride("font_size", tamanoLetra);
        }

        _aviso.AddThemeFontSizeOverride("font_size", Math.Clamp((int)Mathf.Round(alto * 0.022f), 11, 26));
    }

    private void UbicarAviso()
    {
        if (_aviso == null) return;

        if (_lista.Count == 0)
        {
            // Centrado dentro del recuadro de video cuando todavía no hay videos.
            _aviso.AnchorLeft = 0.405f;
            _aviso.AnchorRight = 0.910f;
            _aviso.AnchorTop = 0.180f;
            _aviso.AnchorBottom = 0.790f;
            _aviso.HorizontalAlignment = HorizontalAlignment.Center;
            _aviso.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            // Debajo del visor y a la izquierda del logotipo para nunca pisarlos.
            _aviso.AnchorLeft = 0.381f;
            _aviso.AnchorRight = 0.725f;
            _aviso.AnchorTop = 0.835f;
            _aviso.AnchorBottom = 0.970f;
            _aviso.HorizontalAlignment = HorizontalAlignment.Left;
            _aviso.VerticalAlignment = VerticalAlignment.Center;
        }
        _aviso.OffsetLeft = 0;
        _aviso.OffsetRight = 0;
        _aviso.OffsetTop = 0;
        _aviso.OffsetBottom = 0;
    }

    private void ActualizarAviso()
    {
        if (_aviso == null) return;

        UbicarAviso();
        string texto;
        if (_pidConversion != -1)
        {
            texto = $"Convirtiendo video {Math.Min(_hechos + 1, _totalAConvertir)} de {_totalAConvertir}: " +
                    $"{System.IO.Path.GetFileName(_origenActual)}…";
        }
        else if (_faltaFfmpeg)
        {
            texto = "Existen videos en formato distinto de .ogv y no se encontró ffmpeg.exe para convertirlos.\n" +
                    "Opciones → Abrir carpeta de videos.";
        }
        else if (_lista.Count == 0)
        {
            texto = "Aún no hay videos configurados para el menú de inicio.\nOpciones → Abrir carpeta de videos.";
        }
        else
        {
            texto = "";
        }

        if (_ultimoError != "") texto = texto == "" ? _ultimoError : $"{texto}\n{_ultimoError}";
        _aviso.Text = texto;
    }

    // =====================================================================
    //  Opciones
    // =====================================================================

    private void AbrirOpciones()
    {
        if (_dialogoOpciones == null || !GodotObject.IsInstanceValid(_dialogoOpciones))
        {
            ConstruirOpciones();
        }
        _dialogoOpciones!.PopupCentered(new Vector2I(560, 530));
    }

    private void ConstruirOpciones()
    {
        _dialogoOpciones = new AcceptDialog { Title = "Opciones", OkButtonText = "Listo" };

        var raiz = new VBoxContainer { CustomMinimumSize = new Vector2(520, 430) };
        raiz.AddThemeConstantOverride("separation", 10);

        var pantalla = new CheckBox { Text = "Pantalla completa", ButtonPressed = _pantallaCompleta };
        pantalla.Toggled += activo =>
        {
            _pantallaCompleta = activo;
            AplicarPantallaCompleta();
            GuardarAjustes();
        };
        raiz.AddChild(pantalla);

        var silencio = new CheckBox { Text = "Silenciar los videos del menú de inicio", ButtonPressed = _silenciar };
        silencio.Toggled += activo =>
        {
            _silenciar = activo;
            _reproductor.Volume = activo ? 0f : 1f;
            GuardarAjustes();
        };
        raiz.AddChild(silencio);

        var filaOrden = new HBoxContainer();
        filaOrden.AddChild(new Label { Text = "Orden de los videos:  " });
        var orden = new OptionButton();
        orden.AddItem("Aleatorio");
        orden.AddItem("Alfabético");
        orden.Selected = _aleatorio ? 0 : 1;
        orden.ItemSelected += indice =>
        {
            _aleatorio = indice == 0;
            GuardarAjustes();
        };
        filaOrden.AddChild(orden);
        raiz.AddChild(filaOrden);

        var convertir = new CheckBox
        {
            Text = "Convertir automáticamente MP4/MOV/WEBM a .ogv (requiere ffmpeg.exe)",
            ButtonPressed = _convertirAuto,
        };
        convertir.Toggled += activo =>
        {
            _convertirAuto = activo;
            GuardarAjustes();
        };
        raiz.AddChild(convertir);

        raiz.AddChild(new HSeparator());

        raiz.AddChild(new Label
        {
            Text = "Videos del menú de inicio: copie los archivos en la carpeta indicada abajo. Formato directo: .ogv (Ogg Theora). " +
                   "Otros formatos (MP4, MOV, WEBM, etc.) se convierten automáticamente al detectarse por primera vez.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(500, 0),
        });

        var filaCarpeta = new HBoxContainer();
        filaCarpeta.AddThemeConstantOverride("separation", 8);
        var abrir = new Button { Text = "Abrir carpeta de videos" };
        abrir.Pressed += AbrirCarpeta;
        filaCarpeta.AddChild(abrir);

        var releer = new Button { Text = "Releer carpeta" };
        releer.Pressed += () =>
        {
            _fallidos.Clear();
            _ultimoError = "";
            RelevarVideos();
            if (!_reproductor.IsPlaying()) ReproducirActual();
            ActualizarAviso();
        };
        filaCarpeta.AddChild(releer);
        raiz.AddChild(filaCarpeta);

        raiz.AddChild(new Label
        {
            Text = $"Carpeta: {RutaCarpeta}",
            AutowrapMode = TextServer.AutowrapMode.Arbitrary,
            CustomMinimumSize = new Vector2(500, 0),
        });

        _dialogoOpciones.AddChild(raiz);
        AddChild(_dialogoOpciones);
    }

    private void AplicarPantallaCompleta()
    {
        DisplayServer.WindowSetMode(_pantallaCompleta
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }

    // =====================================================================
    //  Recordar los ajustes
    // =====================================================================

    private void GuardarAjustes()
    {
        var ajustes = new ConfigFile();
        ajustes.SetValue("inicio", "aleatorio", _aleatorio);
        ajustes.SetValue("inicio", "silenciar", _silenciar);
        ajustes.SetValue("inicio", "convertir_auto", _convertirAuto);
        ajustes.SetValue("inicio", "pantalla_completa", _pantallaCompleta);
        ajustes.Save(ArchivoAjustes);
    }

    private void CargarAjustes()
    {
        var ajustes = new ConfigFile();
        if (ajustes.Load(ArchivoAjustes) != Error.Ok) return;

        _aleatorio = (bool)ajustes.GetValue("inicio", "aleatorio", true);
        _silenciar = (bool)ajustes.GetValue("inicio", "silenciar", false);
        _convertirAuto = (bool)ajustes.GetValue("inicio", "convertir_auto", true);
        _pantallaCompleta = (bool)ajustes.GetValue("inicio", "pantalla_completa", false);

        if (_pantallaCompleta) AplicarPantallaCompleta();
    }
}
