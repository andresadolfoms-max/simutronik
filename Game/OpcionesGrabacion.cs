namespace ElectricSim.Game;

/// <summary>Movimiento automático de la cámara durante la grabación offline.</summary>
public enum ModoCamaraGrabacion
{
    Estatica,
    OrbitaSuave,
    Vuelta360,
    Acercamiento,
    Alejamiento,
}

/// <summary>Formato de contenedor/archivo generado por FFmpeg.</summary>
public enum FormatoVideoSalida
{
    Mp4,
    Mp4MasGif,
    WebM,
}

/// <summary>Nivel de compresión y nitidez de codificación en FFmpeg.</summary>
public enum CalidadVideo
{
    Presentacion,
    Estandar,
    Compacta,
}

/// <summary>
/// Parámetros de configuración del estudio de grabación de video con FFmpeg.
/// Incluye resolución, FPS, duración, escala de tiempo (cámara lenta / time-lapse),
/// movimiento cinematográfico de cámara, captura con o sin interfaz UI y filtros FFmpeg.
/// </summary>
public readonly struct OpcionesGrabacion
{
    /// <summary>Duración del video resultante en segundos reales de reproducción.</summary>
    public double DuracionSegundos { get; init; }

    /// <summary>Cuadros por segundo del archivo de salida.</summary>
    public int Fps { get; init; }

    /// <summary>Ancho en píxeles (relación 16:9).</summary>
    public int Ancho { get; init; }

    /// <summary>Alto en píxeles (relación 16:9).</summary>
    public int Alto { get; init; }

    /// <summary>Prefijo o nombre del proyecto para el archivo de salida.</summary>
    public string NombreProyecto { get; init; }

    /// <summary>
    /// Factor de velocidad de la simulación respecto al video:
    /// 1.0 = tiempo real, &lt; 1.0 = cámara lenta (ej. 0.25x), &gt; 1.0 = time-lapse (ej. 5x).
    /// </summary>
    public double EscalaTiempo { get; init; }

    /// <summary>Trayectoria de la cámara durante la grabación.</summary>
    public ModoCamaraGrabacion ModoCamara { get; init; }

    /// <summary>
    /// True = captura toda la ventana de la aplicación (3D + paneles de interfaz UI).
    /// False = captura únicamente la escena 3D limpia en un SubViewport dedicado.
    /// </summary>
    public bool IncluirInterfaz { get; init; }

    /// <summary>Formato de exportación con FFmpeg (MP4, MP4 + GIF animado con paleta, o WebM).</summary>
    public FormatoVideoSalida Formato { get; init; }

    /// <summary>Calidad de codificación (CRF 14 presentación, CRF 18 estándar, CRF 24 compacto).</summary>
    public CalidadVideo Calidad { get; init; }

    /// <summary>Aplica filtro FFmpeg de fundido suave al inicio y al final del video (fade in/out).</summary>
    public bool FundidoEntradaSalida { get; init; }

    /// <summary>Aplica filtro complejo FFmpeg para duplicar el video en reversa (efecto bucle boomerang).</summary>
    public bool BucleIdaYVuelta { get; init; }

    /// <summary>Aplica filtro FFmpeg de ecualización visual (contraste y saturación mejorados para presentación).</summary>
    public bool RealceColor { get; init; }

    /// <summary>Copia automáticamente el video terminado a la carpeta de videos de fondo de la pantalla de inicio.</summary>
    public bool EnviarAFondoInicio { get; init; }

    public OpcionesGrabacion(double duracionSegundos, int fps, int ancho, int alto)
    {
        DuracionSegundos = duracionSegundos;
        Fps = fps;
        Ancho = ancho;
        Alto = alto;
        NombreProyecto = "ElectricSim";
        EscalaTiempo = 1.0;
        ModoCamara = ModoCamaraGrabacion.Estatica;
        IncluirInterfaz = false;
        Formato = FormatoVideoSalida.Mp4;
        Calidad = CalidadVideo.Estandar;
        FundidoEntradaSalida = false;
        BucleIdaYVuelta = false;
        RealceColor = true;
        EnviarAFondoInicio = false;
    }

    /// <summary>Configuración inicial equilibrada (10 s, 30 fps, 720p HD).</summary>
    public static OpcionesGrabacion PorDefecto => new(10, 30, 1280, 720);

    /// <summary>Factor de velocidad efectivo (1.0 si no se especificó).</summary>
    public double EscalaTiempoEfectiva => EscalaTiempo > 0 ? EscalaTiempo : 1.0;

    /// <summary>Cantidad total de cuadros PNG a renderizar.</summary>
    public int TotalCuadros => Math.Max(1, (int)Math.Round(DuracionSegundos * Fps));

    /// <summary>Tiempo físico que avanza el solver eléctrico en cada cuadro capturado.</summary>
    public double SegundosPorCuadro => (1.0 / Fps) * EscalaTiempoEfectiva;

    /// <summary>Segundos totales que habrán transcurrido en el circuito al finalizar el video.</summary>
    public double TiempoSimuladoTotal => DuracionSegundos * EscalaTiempoEfectiva;

    /// <summary>Factor CRF para libx264 / libvpx-vp9.</summary>
    public int Crf => Calidad switch
    {
        CalidadVideo.Presentacion => 14,
        CalidadVideo.Compacta => 24,
        _ => 18,
    };

    /// <summary>Peso aproximado de los cuadros PNG temporales en disco (MB).</summary>
    public double MegabytesTemporales =>
        TotalCuadros * (Ancho * (double)Alto * 0.5) / (1024 * 1024);

    /// <summary>Tiempo estimado de renderizado cuadro a cuadro en segundos.</summary>
    public double SegundosEstimados =>
        TotalCuadros * (1.0 / 60.0 + Ancho * (double)Alto * 40e-9);
}
