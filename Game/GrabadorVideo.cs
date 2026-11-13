using System.Globalization;
using Godot;
using ElectricSim.Game.UI;

namespace ElectricSim.Game;

/// <summary>
/// Estudio de grabación de video offline con FFmpeg.
/// Soporta:
///  - Escala de tiempo (cámara lenta, tiempo real 1:1 y time-lapse térmico).
///  - Movimientos cinematográficos de cámara 3D (estática, paneo orbital,
///    giro 360° turntable, zoom-in y zoom-out).
///  - Captura de escena 3D limpia (SubViewport) o de la aplicación completa con UI.
///  - Exportación a MP4 (H.264), MP4 + GIF animado con paleta optimizada, y WebM (VP9).
///  - Post-procesado con filtros nativos de FFmpeg (realce de color eq, fundido
///    fade in/out, bucle ida y vuelta boomerang) y envío directo al menú de inicio.
/// </summary>
public partial class GrabadorVideo : Node
{
    private const string CarpetaCuadros = "user://grabacion_tmp";
    private const string CarpetaVideos = "user://grabaciones";
    private const string FfmpegEnProyecto = "res://ffmpeg/ffmpeg.exe";

    private Simulacion _sim = null!;
    private Camera3D _camaraViva = null!;
    private bool _grabando;
    private bool _cancelado;

    private CanvasLayer _overlay = null!;
    private ProgressBar _progreso = null!;
    private Label _detalle = null!;
    private Button _btnCancelar = null!;

    private AcceptDialog? _cartel;
    private DialogoGrabacion? _dialogo;

    public void Init(Simulacion sim, Camera3D camaraViva)
    {
        _sim = sim;
        _camaraViva = camaraViva;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _overlay = new CanvasLayer { Name = "OverlayGrabacion", Layer = 30, Visible = false };
        AddChild(_overlay);

        var fondo = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        fondo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(fondo);

        var centro = new CenterContainer();
        centro.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(centro);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", TemaUI.EstiloPanelFlotante(radioEsquina: 10, margenH: 28, margenV: 20));
        centro.AddChild(panel);

        var columna = new VBoxContainer();
        columna.AddThemeConstantOverride("separation", 12);
        panel.AddChild(columna);

        var titulo = new Label
        {
            Text = "Estudio FFmpeg · Generando video…",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titulo.AddThemeFontSizeOverride("font_size", 17);
        titulo.AddThemeColorOverride("font_color", TemaUI.AcentoCian);
        columna.AddChild(titulo);

        _progreso = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            ShowPercentage = true,
            CustomMinimumSize = new Vector2(380, 26),
        };
        columna.AddChild(_progreso);

        _detalle = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _detalle.AddThemeFontSizeOverride("font_size", 12);
        _detalle.AddThemeColorOverride("font_color", TemaUI.TextoSecundario);
        columna.AddChild(_detalle);

        _btnCancelar = new Button
        {
            Text = "Cancelar grabación (Esc)",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        TemaUI.AplicarEstiloBoton(_btnCancelar, peligro: true);
        _btnCancelar.Pressed += () => _cancelado = true;
        columna.AddChild(_btnCancelar);
    }

    public void IniciarGrabacion()
    {
        if (_grabando) return;
        if (!_sim.EnPausa) return;

        if (ResolverRutaFfmpeg() == null)
        {
            AvisarFaltaFfmpeg();
            return;
        }

        if (_dialogo == null || !GodotObject.IsInstanceValid(_dialogo))
        {
            _dialogo = new DialogoGrabacion();
            _dialogo.Confirmado += Generar;
            GetTree().Root.AddChild(_dialogo);
        }
        _dialogo.PopupCentered();
    }

    private async void Generar(OpcionesGrabacion op)
    {
        if (_grabando) return;
        if (!_sim.EnPausa) return;

        string? rutaFfmpeg = ResolverRutaFfmpeg();
        if (rutaFfmpeg == null)
        {
            AvisarFaltaFfmpeg();
            return;
        }

        _grabando = true;
        _cancelado = false;
        GetTree().Paused = true;

        Transform3D poseOriginalCamara = _camaraViva.GlobalTransform;
        string tituloVentanaOriginal = GetTree().Root.Title;
        SubViewport? subViewport = null;
        Camera3D? camaraSub = null;

        string dirCuadros = ProjectSettings.GlobalizePath(CarpetaCuadros);
        string dirVideos = ProjectSettings.GlobalizePath(CarpetaVideos);

        try
        {
            if (!op.IncluirInterfaz)
            {
                subViewport = new SubViewport
                {
                    Size = new Vector2I(op.Ancho, op.Alto),
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                    World3D = GetTree().Root.World3D,
                    Msaa3D = GetTree().Root.Msaa3D,
                };
                AddChild(subViewport);

                camaraSub = new Camera3D
                {
                    Transform = poseOriginalCamara,
                    Fov = _camaraViva.Fov,
                    Near = _camaraViva.Near,
                    Far = _camaraViva.Far,
                    KeepAspect = _camaraViva.KeepAspect,
                    Current = true,
                };
                subViewport.AddChild(camaraSub);
            }

            if (Directory.Exists(dirCuadros))
                Directory.Delete(dirCuadros, recursive: true);
            Directory.CreateDirectory(dirCuadros);
            Directory.CreateDirectory(dirVideos);

            int totalCuadros = op.TotalCuadros;
            _overlay.Visible = !op.IncluirInterfaz;
            _progreso.MaxValue = totalCuadros;
            _btnCancelar.Disabled = false;

            for (int cuadro = 0; cuadro < totalCuadros; cuadro++)
            {
                if (_cancelado || Input.IsKeyPressed(Key.Escape))
                {
                    _cancelado = true;
                    break;
                }

                if (cuadro > 0) _sim.AvanzarOffline(op.SegundosPorCuadro);

                // Actualizar pose de cámara según el modo cinematográfico elegido
                Transform3D poseCuadro = CalcularPoseCamara(op, cuadro, totalCuadros, poseOriginalCamara);
                if (camaraSub != null)
                {
                    camaraSub.Transform = poseCuadro;
                }
                else
                {
                    _camaraViva.GlobalTransform = poseCuadro;
                }

                _progreso.Value = cuadro + 1;
                double tSim = cuadro * op.SegundosPorCuadro;
                _detalle.Text = $"Cuadro {cuadro + 1} de {totalCuadros}  ·  Tiempo simulado: {tSim:F2} s ({op.EscalaTiempoEfectiva:0.##}x)";

                if (op.IncluirInterfaz)
                {
                    DisplayServer.WindowSetTitle($"[Grabando {cuadro + 1}/{totalCuadros} · ESC para cancelar] {tituloVentanaOriginal}");
                }

                await ToSignal(RenderingServer.Singleton, "frame_post_draw");

                var imagen = subViewport != null
                    ? subViewport.GetTexture().GetImage()
                    : GetTree().Root.GetTexture().GetImage();

                var error = imagen.SavePng($"{CarpetaCuadros}/cuadro_{cuadro:D5}.png");
                if (error != Error.Ok)
                {
                    MostrarCartel("Error al guardar cuadros",
                        $"No se pudo escribir el cuadro {cuadro} en:\n{dirCuadros}\n\nError de Godot: {error}");
                    return;
                }
            }

            if (_cancelado)
            {
                if (Directory.Exists(dirCuadros))
                    Directory.Delete(dirCuadros, recursive: true);
                return;
            }

            // --- Codificación con FFmpeg ---
            _overlay.Visible = true;
            _btnCancelar.Disabled = true;
            _detalle.Text = "Procesando y codificando video con FFmpeg…";
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");

            string prefijo = LimpiarNombreArchivo(op.NombreProyecto);
            string marcaTiempo = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string extPrincipal = op.Formato == FormatoVideoSalida.WebM ? "webm" : "mp4";
            string nombreSalida = $"{prefijo}_{marcaTiempo}.{extPrincipal}";
            string rutaSalida = Path.Combine(dirVideos, nombreSalida);

            string[] argumentosPrincipales = ConstruirArgumentosFfmpeg(op, dirCuadros, rutaSalida);
            GD.Print($"[Grabador] Ejecutando: {rutaFfmpeg} {string.Join(' ', argumentosPrincipales)}");

            var salidaFfmpeg = new Godot.Collections.Array();
            int codigo = await Task.Run(
                () => OS.Execute(rutaFfmpeg, argumentosPrincipales, salidaFfmpeg, readStderr: true));

            if (codigo != 0)
            {
                MostrarCartel("Error de FFmpeg",
                    $"FFmpeg terminó con código {codigo}.\n\n" +
                    $"Los cuadros PNG quedaron en:\n{dirCuadros}\n\n" +
                    $"Salida de FFmpeg:\n{UltimasLineas(salidaFfmpeg, 15)}");
                return;
            }

            // Si pidió MP4 + GIF Animado, generamos el GIF con paleta de 256 colores optimizada por FFmpeg
            string? rutaGif = null;
            if (op.Formato == FormatoVideoSalida.Mp4MasGif)
            {
                _detalle.Text = "Generando GIF animado con paleta optimizada (FFmpeg)…";
                await ToSignal(RenderingServer.Singleton, "frame_post_draw");

                rutaGif = Path.Combine(dirVideos, $"{prefijo}_{marcaTiempo}.gif");
                int fpsGif = Math.Min(op.Fps, 15);
                int anchoGif = Math.Min(op.Ancho, 640);
                string[] argsGif =
                {
                    "-y",
                    "-i", rutaSalida,
                    "-vf", $"fps={fpsGif},scale={anchoGif}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
                    rutaGif,
                };
                var salidaGif = new Godot.Collections.Array();
                await Task.Run(() => OS.Execute(rutaFfmpeg, argsGif, salidaGif, readStderr: true));
            }

            // Si pidió enviar copia al menú de inicio, la copiamos a user://inicio_videos
            bool copiadoAInicio = false;
            if (op.EnviarAFondoInicio && File.Exists(rutaSalida))
            {
                string dirInicio = PantallaInicio.RutaCarpeta;
                Directory.CreateDirectory(dirInicio);
                string destinoInicio = Path.Combine(dirInicio, Path.GetFileName(rutaSalida));
                File.Copy(rutaSalida, destinoInicio, overwrite: true);
                copiadoAInicio = true;
            }

            if (Directory.Exists(dirCuadros))
                Directory.Delete(dirCuadros, recursive: true);

            GD.Print($"[Grabador] Video listo: {rutaSalida}");
            MostrarCartelFinal(rutaSalida, rutaGif, copiadoAInicio, op);
        }
        catch (Exception e)
        {
            MostrarCartel("Error inesperado al grabar", e.Message);
            GD.PushError($"[Grabador] {e}");
        }
        finally
        {
            _camaraViva.GlobalTransform = poseOriginalCamara;
            DisplayServer.WindowSetTitle(tituloVentanaOriginal);
            _overlay.Visible = false;
            GetTree().Paused = false;
            subViewport?.QueueFree();
            _grabando = false;
        }
    }

    private Transform3D CalcularPoseCamara(OpcionesGrabacion op, int cuadro, int totalCuadros, Transform3D poseInicial)
    {
        if (op.ModoCamara == ModoCamaraGrabacion.Estatica)
            return poseInicial;

        if (_camaraViva.GetParent() is not CamaraOrbital orbital)
            return poseInicial;

        float tLineal = totalCuadros > 1 ? cuadro / (float)(totalCuadros - 1) : 0f;
        float tSuave = tLineal * tLineal * (3f - 2f * tLineal); // smoothstep

        return op.ModoCamara switch
        {
            ModoCamaraGrabacion.OrbitaSuave => orbital.CalcularTransformOrbital((tSuave - 0.5f) * 60f, 1.0f),
            ModoCamaraGrabacion.Vuelta360 => orbital.CalcularTransformOrbital((cuadro / (float)Math.Max(1, totalCuadros)) * 360f, 1.0f),
            ModoCamaraGrabacion.Acercamiento => orbital.CalcularTransformOrbital(0f, Mathf.Lerp(1.15f, 0.72f, tSuave)),
            ModoCamaraGrabacion.Alejamiento => orbital.CalcularTransformOrbital(0f, Mathf.Lerp(0.72f, 1.18f, tSuave)),
            _ => poseInicial,
        };
    }

    private static string[] ConstruirArgumentosFfmpeg(OpcionesGrabacion op, string dirCuadros, string rutaSalida)
    {
        var args = new List<string>
        {
            "-y",
            "-framerate", op.Fps.ToString(CultureInfo.InvariantCulture),
            "-start_number", "0",
            "-i", Path.Combine(dirCuadros, "cuadro_%05d.png"),
        };

        string cadenaFiltros = ConstruirCadenaFiltros(op);
        if (!string.IsNullOrEmpty(cadenaFiltros))
        {
            args.Add("-vf");
            args.Add(cadenaFiltros);
        }

        if (op.Formato == FormatoVideoSalida.WebM)
        {
            args.AddRange(new[]
            {
                "-c:v", "libvpx-vp9",
                "-crf", op.Crf.ToString(CultureInfo.InvariantCulture),
                "-b:v", "0",
                "-pix_fmt", "yuv420p",
            });
        }
        else
        {
            args.AddRange(new[]
            {
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-crf", op.Crf.ToString(CultureInfo.InvariantCulture),
                "-movflags", "+faststart",
            });
        }

        args.Add(rutaSalida);
        return args.ToArray();
    }

    private static string ConstruirCadenaFiltros(OpcionesGrabacion op)
    {
        var etapas = new List<string>();

        // Si capturamos el viewport principal con UI, aseguramos la resolución exacta elegida
        if (op.IncluirInterfaz)
        {
            etapas.Add($"scale={op.Ancho}:{op.Alto}:force_original_aspect_ratio=decrease,pad={op.Ancho}:{op.Alto}:(ow-iw)/2:(oh-ih)/2,setsar=1");
        }

        // Realce de contraste y saturación para presentaciones
        if (op.RealceColor)
        {
            etapas.Add("eq=contrast=1.05:saturation=1.10");
        }

        // Bucle ida y vuelta (Boomerang): duplica la secuencia en reversa
        if (op.BucleIdaYVuelta)
        {
            string previo = etapas.Count > 0 ? string.Join(",", etapas) + "," : "";
            etapas.Clear();
            etapas.Add($"{previo}split[f0][f1];[f1]reverse[r];[f0][r]concat=n=2:v=1");
        }

        // Fundido cinematográfico de entrada y salida
        double duracionTotal = op.BucleIdaYVuelta ? op.DuracionSegundos * 2.0 : op.DuracionSegundos;
        if (op.FundidoEntradaSalida && duracionTotal >= 1.2)
        {
            string dFade = "0.45";
            string inicioOut = Math.Max(0.1, duracionTotal - 0.45).ToString("F2", CultureInfo.InvariantCulture);
            etapas.Add($"fade=t=in:st=0:d={dFade},fade=t=out:st={inicioOut}:d={dFade}");
        }

        return string.Join(",", etapas);
    }

    private static string LimpiarNombreArchivo(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "ElectricSim";
        char[] invalidos = Path.GetInvalidFileNameChars();
        var limpio = new System.Text.StringBuilder();
        foreach (char c in nombre.Trim())
        {
            if (Array.IndexOf(invalidos, c) < 0 && c != ' ')
                limpio.Append(c);
            else if (c == ' ')
                limpio.Append('_');
        }
        return limpio.Length > 0 ? limpio.ToString() : "ElectricSim";
    }

    // =====================================================================
    //  FFmpeg: resolución de ruta
    // =====================================================================

    internal static string? ResolverRutaFfmpeg()
    {
        string juntoAlExe = Path.Combine(DirectorioDelEjecutable(), "ffmpeg.exe");
        if (File.Exists(juntoAlExe)) return juntoAlExe;

        string enProyecto = ProjectSettings.GlobalizePath(FfmpegEnProyecto);
        if (File.Exists(enProyecto)) return enProyecto;

        return null;
    }

    private void AvisarFaltaFfmpeg()
    {
        MostrarCartel("Falta FFmpeg",
            "No se encontró ffmpeg.exe en las rutas esperadas:\n\n" +
            $"1) {Path.Combine(DirectorioDelEjecutable(), "ffmpeg.exe")}\n" +
            "   (junto al ejecutable)\n\n" +
            $"2) {ProjectSettings.GlobalizePath(FfmpegEnProyecto)}\n" +
            "   (dentro del proyecto)\n\n" +
            "Copie ffmpeg.exe en cualquiera de estas ubicaciones e inténtelo nuevamente.");
    }

    private static string DirectorioDelEjecutable() =>
        Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";

    private static string UltimasLineas(Godot.Collections.Array salida, int cantidad)
    {
        var todo = new System.Text.StringBuilder();
        foreach (Variant item in salida) todo.AppendLine(item.AsString());

        string[] lineas = todo.ToString().Replace("\r", "").Split('\n');
        int desde = Math.Max(0, lineas.Length - cantidad);
        return string.Join('\n', lineas, desde, lineas.Length - desde).Trim();
    }

    // =====================================================================
    //  Carteles
    // =====================================================================

    private void MostrarCartel(string titulo, string mensaje)
    {
        if (_cartel == null)
        {
            _cartel = new AcceptDialog { ProcessMode = ProcessModeEnum.Always };
            GetTree().Root.AddChild(_cartel);
        }
        _cartel.Title = titulo;
        _cartel.DialogText = mensaje;
        _cartel.PopupCentered();
    }

    private void MostrarCartelFinal(string rutaPrincipal, string? rutaGif, bool copiadoAInicio, OpcionesGrabacion op)
    {
        double duracionFinal = op.BucleIdaYVuelta ? op.DuracionSegundos * 2.0 : op.DuracionSegundos;
        string textoExtraGif = rutaGif != null && File.Exists(rutaGif)
            ? $"\nGIF animado generado en:\n{rutaGif}\n"
            : "";
        string textoExtraInicio = copiadoAInicio
            ? "\n• Copiado a la carpeta de videos de fondo de la Pantalla de Inicio.\n"
            : "";

        var cartel = new AcceptDialog
        {
            Title = "Grabación completada",
            DialogText =
                $"Video guardado en:\n{rutaPrincipal}\n" +
                textoExtraGif +
                textoExtraInicio +
                $"\nDetalles: {duracionFinal:0.#} s ({op.TiempoSimuladoTotal:0.##} s simulados a {op.EscalaTiempoEfectiva:0.##}x) · {op.Fps} fps · {op.Ancho}×{op.Alto} · CRF {op.Crf}",
            ProcessMode = ProcessModeEnum.Always,
        };
        cartel.AddButton("Reproducir video", true, "reproducir");
        cartel.AddButton("Abrir carpeta", true, "abrir");
        cartel.CustomAction += accion =>
        {
            if (accion == "reproducir") OS.ShellOpen(rutaPrincipal);
            else if (accion == "abrir") OS.ShellOpen(Path.GetDirectoryName(rutaPrincipal) ?? rutaPrincipal);
        };
        cartel.VisibilityChanged += () => { if (!cartel.Visible) cartel.QueueFree(); };
        GetTree().Root.AddChild(cartel);
        cartel.PopupCentered();
    }
}
