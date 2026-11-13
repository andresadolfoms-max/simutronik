using Godot;

namespace ElectricSim.Game.UI;

/// <summary>
/// Estudio de configuración de grabación de video con FFmpeg.
/// Permite ajustar nombre de proyecto, duración, FPS, resolución, escala de tiempo
/// (cámara lenta / time-lapse), movimiento cinematográfico de cámara 3D, captura
/// limpia o con interfaz UI, formato de exportación (MP4, MP4 + GIF con paleta, WebM)
/// y filtros de post-procesado de FFmpeg.
/// </summary>
public partial class DialogoGrabacion : ConfirmationDialog
{
    private static readonly Vector2I[] Resoluciones =
    {
        new(854, 480),
        new(1280, 720),
        new(1920, 1080),
    };

    private static readonly int[] Fps = { 24, 30, 60 };

    private static readonly double[] EscalasTiempo = { 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };

    private const int CuadrosParaAdvertir = 5000;

    /// <summary>El usuario confirmó: iniciar grabación con estas opciones.</summary>
    public event Action<OpcionesGrabacion>? Confirmado;

    private LineEdit _nombre = null!;
    private SpinBox _duracion = null!;
    private OptionButton _fps = null!;
    private OptionButton _resolucion = null!;
    private OptionButton _escalaTiempo = null!;
    private OptionButton _modoCamara = null!;
    private OptionButton _modoCaptura = null!;
    private OptionButton _formato = null!;
    private OptionButton _calidad = null!;

    private CheckBox _realceColor = null!;
    private CheckBox _fundido = null!;
    private CheckBox _bucleBoomerang = null!;
    private CheckBox _enviarInicio = null!;

    private Label _estimacion = null!;
    private Label _advertencia = null!;

    public override void _Ready()
    {
        Title = "Estudio de Grabación de Video (FFmpeg)";
        OkButtonText = "Iniciar grabación";
        CancelButtonText = "Cancelar";
        Unresizable = true;
        ProcessMode = ProcessModeEnum.Always;

        AddButton("Abrir carpeta de videos", false, "abrir_carpeta");
        CustomAction += accion =>
        {
            if (accion == "abrir_carpeta")
            {
                string dir = ProjectSettings.GlobalizePath("user://grabaciones");
                System.IO.Directory.CreateDirectory(dir);
                OS.ShellOpen(dir);
            }
        };

        var opcionesIniciales = OpcionesGrabacion.PorDefecto;

        var columna = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        columna.AddThemeConstantOverride("separation", 8);
        AddChild(columna);

        var intro = new Label
        {
            Text = "Genera videos fluidos cuadro a cuadro desde el estado actual de la simulación " +
                   "y los codifica con FFmpeg (sin caídas de FPS aunque el circuito sea complejo).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        intro.AddThemeFontSizeOverride("font_size", 12);
        intro.AddThemeColorOverride("font_color", TemaUI.TextoSecundario);
        columna.AddChild(intro);

        var grilla = new GridContainer { Columns = 2 };
        grilla.AddThemeConstantOverride("h_separation", 14);
        grilla.AddThemeConstantOverride("v_separation", 7);
        columna.AddChild(grilla);

        // --- Nombre del archivo ---
        grilla.AddChild(new Label { Text = "Nombre del video:" });
        _nombre = new LineEdit
        {
            Text = "Proyecto_Circuito",
            PlaceholderText = "Ej: DivisorTension_LED",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Prefijo con el que se guardará el archivo de video en la carpeta de grabaciones.",
        };
        grilla.AddChild(_nombre);

        // --- Duración ---
        grilla.AddChild(new Label { Text = "Duración del video:" });
        _duracion = new SpinBox
        {
            MinValue = 1,
            MaxValue = 180,
            Step = 1,
            Value = opcionesIniciales.DuracionSegundos,
            Suffix = "s",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Duración en segundos del video final reproducido.",
        };
        _duracion.ValueChanged += _ => Recalcular();
        grilla.AddChild(_duracion);

        // --- Velocidad de simulación (Escala de tiempo) ---
        grilla.AddChild(new Label { Text = "Velocidad simulada:" });
        _escalaTiempo = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Desacopla el tiempo del circuito de la duración del video:\n" +
                          "• Cámara lenta (< 1x): ideal para ver carga de capacitores o chispazos.\n" +
                          "• Tiempo real (1x): 1 s de video = 1 s de circuito.\n" +
                          "• Time-Lapse (> 1x): ideal para ver calentamiento térmico o descarga de pilas.",
        };
        _escalaTiempo.AddItem("0.1x  (Cámara ultra lenta)", 0);
        _escalaTiempo.AddItem("0.25x (Cámara lenta)", 1);
        _escalaTiempo.AddItem("0.5x  (Media velocidad)", 2);
        _escalaTiempo.AddItem("1.0x  (Tiempo real 1:1)", 3);
        _escalaTiempo.AddItem("2.0x  (Cámara rápida)", 4);
        _escalaTiempo.AddItem("5.0x  (Time-Lapse térmico)", 5);
        _escalaTiempo.AddItem("10.0x (Time-Lapse rápido)", 6);
        _escalaTiempo.Selected = 3;
        _escalaTiempo.ItemSelected += _ => Recalcular();
        grilla.AddChild(_escalaTiempo);

        // --- Cuadros por segundo (FPS) ---
        grilla.AddChild(new Label { Text = "Cuadros por seg. (FPS):" });
        _fps = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Fluidez temporal del archivo final.",
        };
        _fps.AddItem("24 fps (Estándar cine / liviano)", 0);
        _fps.AddItem("30 fps (Estándar fluido)", 1);
        _fps.AddItem("60 fps (Ultra fluido)", 2);
        _fps.Selected = 1;
        _fps.ItemSelected += _ => Recalcular();
        grilla.AddChild(_fps);

        // --- Resolución ---
        grilla.AddChild(new Label { Text = "Resolución (16:9):" });
        _resolucion = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Resolución del archivo final.",
        };
        _resolucion.AddItem("854 × 480 (SD liviano)", 0);
        _resolucion.AddItem("1280 × 720 (HD 720p)", 1);
        _resolucion.AddItem("1920 × 1080 (Full HD 1080p)", 2);
        _resolucion.Selected = 1;
        _resolucion.ItemSelected += _ => Recalcular();
        grilla.AddChild(_resolucion);

        // --- Movimiento de Cámara 3D ---
        grilla.AddChild(new Label { Text = "Cámara 3D:" });
        _modoCamara = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Trayectoria automática de la cámara alrededor del punto de enfoque actual.",
        };
        _modoCamara.AddItem("Estática (fija en el ángulo actual)", (int)ModoCamaraGrabacion.Estatica);
        _modoCamara.AddItem("Paneo orbital suave (arco de 60°)", (int)ModoCamaraGrabacion.OrbitaSuave);
        _modoCamara.AddItem("Vuelta completa 360° (Turntable / Showcase)", (int)ModoCamaraGrabacion.Vuelta360);
        _modoCamara.AddItem("Acercamiento progresivo (Dolly Zoom-In)", (int)ModoCamaraGrabacion.Acercamiento);
        _modoCamara.AddItem("Alejamiento progresivo (Dolly Zoom-Out)", (int)ModoCamaraGrabacion.Alejamiento);
        _modoCamara.Selected = 0;
        grilla.AddChild(_modoCamara);

        // --- Qué capturar ---
        grilla.AddChild(new Label { Text = "Contenido a capturar:" });
        _modoCaptura = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Seleccione si desea capturar únicamente la escena 3D o la ventana completa con paneles de interfaz (Inspector, Catálogo y Barra Superior).",
        };
        _modoCaptura.AddItem("Solo escena 3D limpia (sin interfaz UI)", 0);
        _modoCaptura.AddItem("Aplicación completa (Escena 3D + Interfaz UI)", 1);
        _modoCaptura.Selected = 0;
        grilla.AddChild(_modoCaptura);

        // --- Formato de salida FFmpeg ---
        grilla.AddChild(new Label { Text = "Formato (FFmpeg):" });
        _formato = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "MP4 + GIF genera tanto el video MP4 como un GIF animado con paleta optimizada por FFmpeg para insertar en documentos o web.",
        };
        _formato.AddItem("MP4 (H.264 Universal)", (int)FormatoVideoSalida.Mp4);
        _formato.AddItem("MP4 + GIF Animado (con paleta HD)", (int)FormatoVideoSalida.Mp4MasGif);
        _formato.AddItem("WebM (VP9 para web)", (int)FormatoVideoSalida.WebM);
        _formato.Selected = 0;
        grilla.AddChild(_formato);

        // --- Calidad CRF ---
        grilla.AddChild(new Label { Text = "Calidad de codificación:" });
        _calidad = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Controla el factor CRF de FFmpeg: menor CRF = máxima nitidez en textos y pantallas 3D.",
        };
        _calidad.AddItem("Presentación (CRF 14 · máxima nitidez)", (int)CalidadVideo.Presentacion);
        _calidad.AddItem("Estándar (CRF 18 · alta calidad)", (int)CalidadVideo.Estandar);
        _calidad.AddItem("Compacta (CRF 24 · archivo liviano)", (int)CalidadVideo.Compacta);
        _calidad.Selected = 1;
        grilla.AddChild(_calidad);

        columna.AddChild(new HSeparator());

        // --- Post-procesado con filtros de FFmpeg ---
        var lblEfectos = new Label { Text = "Filtros y post-procesado FFmpeg:" };
        lblEfectos.AddThemeFontSizeOverride("font_size", 12);
        lblEfectos.AddThemeColorOverride("font_color", TemaUI.AcentoCian);
        columna.AddChild(lblEfectos);

        _realceColor = new CheckBox
        {
            Text = "Realce visual de presentación (filtro eq: contraste y color mejorados)",
            ButtonPressed = true,
        };
        columna.AddChild(_realceColor);

        _fundido = new CheckBox
        {
            Text = "Fundido cinematográfico de entrada y salida (filtro fade in/out)",
            ButtonPressed = false,
        };
        columna.AddChild(_fundido);

        _bucleBoomerang = new CheckBox
        {
            Text = "Bucle continuo ida y vuelta / Boomerang (duplica en reversa para loops perfectos)",
            ButtonPressed = false,
        };
        _bucleBoomerang.Toggled += _ => Recalcular();
        columna.AddChild(_bucleBoomerang);

        _enviarInicio = new CheckBox
        {
            Text = "Agregar automáticamente al fondo animado de la Pantalla de Inicio",
            ButtonPressed = false,
        };
        columna.AddChild(_enviarInicio);

        columna.AddChild(new HSeparator());

        // --- Estimación en vivo ---
        _estimacion = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _estimacion.AddThemeFontSizeOverride("font_size", 12);
        columna.AddChild(_estimacion);

        _advertencia = new Label
        {
            Text = "",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _advertencia.AddThemeColorOverride("font_color", TemaUI.AcentoAlerta);
        columna.AddChild(_advertencia);

        Confirmed += () => Confirmado?.Invoke(OpcionesActuales());

        Recalcular();
    }

    public OpcionesGrabacion OpcionesActuales()
    {
        Vector2I res = Resoluciones[Math.Clamp(_resolucion.Selected, 0, Resoluciones.Length - 1)];
        int fps = Fps[Math.Clamp(_fps.Selected, 0, Fps.Length - 1)];
        double escala = EscalasTiempo[Math.Clamp(_escalaTiempo.Selected, 0, EscalasTiempo.Length - 1)];
        string nombreLimpio = string.IsNullOrWhiteSpace(_nombre.Text) ? "ElectricSim" : _nombre.Text.Trim();

        return new OpcionesGrabacion(_duracion.Value, fps, res.X, res.Y)
        {
            NombreProyecto = nombreLimpio,
            EscalaTiempo = escala,
            ModoCamara = (ModoCamaraGrabacion)Math.Clamp(_modoCamara.Selected, 0, 4),
            IncluirInterfaz = _modoCaptura.Selected == 1,
            Formato = (FormatoVideoSalida)Math.Clamp(_formato.Selected, 0, 2),
            Calidad = (CalidadVideo)Math.Clamp(_calidad.Selected, 0, 2),
            RealceColor = _realceColor.ButtonPressed,
            FundidoEntradaSalida = _fundido.ButtonPressed,
            BucleIdaYVuelta = _bucleBoomerang.ButtonPressed,
            EnviarAFondoInicio = _enviarInicio.ButtonPressed,
        };
    }

    private void Recalcular()
    {
        var op = OpcionesActuales();
        double duracionFinalVideo = op.BucleIdaYVuelta ? op.DuracionSegundos * 2.0 : op.DuracionSegundos;

        _estimacion.Text =
            $"• Cuadros a renderizar: {op.TotalCuadros} ({duracionFinalVideo:0.#} s de video final)\n" +
            $"• Tiempo simulado en el circuito: {op.TiempoSimuladoTotal:0.##} s (velocidad {op.EscalaTiempoEfectiva:0.##}x)\n" +
            $"• Espacio temporal: ≈ {op.MegabytesTemporales:0} MB en PNG (se limpia al finalizar) · Espera aprox.: {FormatearEspera(op.SegundosEstimados)}";

        bool pesado = op.TotalCuadros > CuadrosParaAdvertir;
        _advertencia.Visible = pesado;
        _advertencia.Text = pesado
            ? $"Atención: {op.TotalCuadros} cuadros requerirán ~{op.MegabytesTemporales / 1024:0.0} GB temporales en disco antes de codificar."
            : "";

        ResetSize();
    }

    private static string FormatearEspera(double segundos) =>
        segundos < 90
            ? $"{segundos:0} s"
            : $"{segundos / 60:0} min";
}
