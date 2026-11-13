using Godot;
using ElectricSim.Game.UI;

namespace ElectricSim.Game;

/// <summary>
/// La escena raíz. Acá se ARMA todo el juego por código (no hay nodos
/// pre-armados en el .tscn más allá de este script): el piso de madera, la
/// luz y el cielo, la cámara, la simulación, el controlador de construcción
/// y la interfaz — y se CABLEAN los eventos entre las partes. Leer este
/// archivo de punta a punta es leer el mapa completo del proyecto.
/// </summary>
public partial class Main : Node3D
{
    private Simulacion _sim = null!;
    private ControladorConstruccion _controlador = null!;
    private CamaraOrbital _camara = null!;
    private BarraSuperior _barra = null!;
    private PanelPaleta _paleta = null!;
    private PanelInspector _inspector = null!;
    private GrabadorVideo _grabador = null!;

    // La mesa: el material y su textura de madera original, para poder volver a ella.
    private StandardMaterial3D _materialPiso = null!;
    private Texture2D _texturaPisoBase = null!;
    private SelectorMesa _selectorMesa = null!;
    private PantallaInicio _pantallaInicio = null!;

    public override void _Ready()
    {
        // --- Simulación (el corazón) ---
        _sim = new Simulacion { Name = "Simulacion" };
        AddChild(_sim);

        // --- Mundo: piso, luz, cielo ---
        ConstruirPiso();
        ConstruirIluminacion();

        // La mesa que se eligió la última vez (si todavía existe esa imagen).
        _selectorMesa = new SelectorMesa();
        _selectorMesa.Init(_materialPiso, _texturaPisoBase);
        _selectorMesa.CargarPreferencia();

        // --- Cámara ---
        _camara = new CamaraOrbital { Name = "Camara" };
        AddChild(_camara);

        // --- Controlador de construcción ---
        _controlador = new ControladorConstruccion { Name = "Controlador" };
        AddChild(_controlador);
        _controlador.Init(_sim, _camara.Camara, this);

        // --- Grabador de video (necesita la cámara ya construida) ---
        _grabador = new GrabadorVideo { Name = "Grabador" };
        AddChild(_grabador);
        _grabador.Init(_sim, _camara.Camara);

        // --- Interfaz ---
        ConstruirInterfaz();

        // --- Cableado de eventos entre las partes ---
        _paleta.VarianteElegida += def => _controlador.IniciarColocacion(def);
        _barra.PaletaAlternada += visible => _paleta.Alternar(visible);
        _barra.SnapCambiado += activado => _controlador.SnapActivado = activado;
        _barra.ModoBornesCambiado += modo => BorneVisual.CambiarModo(modo);
        // Prender el contacto tiene que buscar los que YA están pegados;
        // apagarlo tiene que soltarlos.
        _barra.ContactoAlternado += _ => _controlador.GeometriaCambiada();
        _barra.TermicaAlternada += _ => AlCompletarTick();
        _barra.GuardarPedido += () => GestorGuardado.AbrirGuardar(_controlador, this);
        _barra.CargarPedido += () => GestorGuardado.AbrirCargar(_controlador, this);
        _barra.GrabarPedido += () => _grabador.IniciarGrabacion();
        _barra.MesaPedido += () => _selectorMesa.Mostrar(this);
        _barra.MenuPrincipalPedido += MostrarInicio;

        // --- Pantalla de inicio: lo PRIMERO que se ve al abrir el simulador ---
        _pantallaInicio = new PantallaInicio();
        AddChild(_pantallaInicio);
        _pantallaInicio.CrearNuevoPedido += () =>
        {
            _controlador.LimpiarCircuito(); // arranca con la mesa vacía
            OcultarInicio();
        };
        _pantallaInicio.CargarPedido += () => GestorGuardado.AbrirCargar(_controlador, this);
        // Si el circuito se carga bien, el menú se va solo. Si se cancela el
        // diálogo, no pasa nada y el menú sigue ahí.
        GestorGuardado.ProyectoCargado += OcultarInicio;
        MostrarInicio();

        // Los ajustes globales viajan en el proyecto. Este es el ejemplo vivo
        // de cómo sumar CUALQUIER subsistema nuevo al guardado sin tocar el
        // guardador: se registra un aporte con su propia sección del archivo.
        RegistroGuardado.Registrar("mundo",
            estado =>
            {
                estado.Poner("ticksPorSegundo", _sim.TicksPorSegundo);
                estado.Poner("temperaturaAmbiente", _sim.TemperaturaAmbiente);
            },
            estado =>
            {
                _sim.TicksPorSegundo = estado.Numero("ticksPorSegundo", _sim.TicksPorSegundo);
                _sim.TemperaturaAmbiente = estado.Numero("temperaturaAmbiente", _sim.TemperaturaAmbiente);
            });

        // Al terminar de cargar, los controles releen los valores nuevos.
        // OJO: += y no =. Con = se pisaba el OcultarInicio de arriba y, al
        // cargar desde la pantalla de inicio, el menú quedaba tapando todo.
        GestorGuardado.ProyectoCargado += () => _barra.Sincronizar();
        _controlador.SeleccionCambiada += objeto => _inspector.Mostrar(objeto);
        _controlador.ModoCambiado += texto => _barra.MostrarModo(texto);

        _sim.TickCompletado += AlCompletarTick;
        _sim.PasoVisual += AlPasoVisual;
    }

    /// <summary>Avanza animaciones visuales continuas (rotores de motores) en vivo y durante grabaciones.</summary>
    private void AlPasoVisual(double delta)
    {
        foreach (var visual in _controlador.Visuales)
        {
            if (IsInstanceValid(visual)) visual.AvanzarAnimacion(delta);
        }
    }

    /// <summary>Después de cada tick del motor: sincronizar todo lo visible.</summary>
    private void AlCompletarTick()
    {
        // IsInstanceValid: un componente recién borrado (Supr) puede seguir en
        // las listas durante el frame en que su QueueFree todavía no ejecutó.
        foreach (var visual in _controlador.Visuales)
        {
            if (IsInstanceValid(visual)) visual.ActualizarDesdeLogica();
        }
        foreach (var cable in _controlador.Cables)
        {
            if (IsInstanceValid(cable)) cable.ActualizarDesdeLogica();
        }
        _inspector.RefrescarLecturas();
    }

    // =====================================================================
    //  Mundo
    // =====================================================================

    /// <summary>
    /// Muestra el menú de inicio y deja el simulador de abajo quieto: sin ticks,
    /// sin cámara, sin atajos de teclado y sin dibujar el 3D (que no se ve).
    /// No se pausa el árbol entero porque los diálogos de guardado/carga se
    /// cuelgan de la raíz y quedarían congelados.
    /// </summary>
    private void MostrarInicio()
    {
        _inspector.Mostrar(null);
        _sim.ProcessMode = ProcessModeEnum.Disabled;
        _controlador.ProcessMode = ProcessModeEnum.Disabled;
        _camara.ProcessMode = ProcessModeEnum.Disabled;
        GetViewport().Disable3D = true;
        _pantallaInicio.Abrir();
    }

    /// <summary>Cierra el menú y devuelve la vida al simulador. Es seguro llamarlo de más (cada carga de proyecto lo llama).</summary>
    private void OcultarInicio()
    {
        if (!_pantallaInicio.EstaAbierta) return;

        _pantallaInicio.Cerrar();
        _sim.ProcessMode = ProcessModeEnum.Inherit;
        _controlador.ProcessMode = ProcessModeEnum.Inherit;
        _camara.ProcessMode = ProcessModeEnum.Inherit;
        GetViewport().Disable3D = false;
    }

    private void ConstruirPiso()
    {
        // Textura de madera procedural: ruido estirado en una dirección, como
        // las vetas de un tablón. Sin archivos de imagen externos.
        var ruido = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.045f,
        };
        var textura = new NoiseTexture2D
        {
            Noise = ruido,
            Width = 256,
            Height = 256,
            Seamless = true,
        };

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.52f, 0.36f, 0.22f), // madera clara
            AlbedoTexture = textura,
            Uv1Scale = new Vector3(7f, 1.6f, 1f),          // vetas alargadas
            Roughness = 0.85f,
        };

        // Guardados para el selector de mesa (Mesa): cambiar la imagen es
        // cambiar la textura de ESTE material, y volver a la madera es
        // restaurar estos valores.
        _materialPiso = material;
        _texturaPisoBase = textura;

        var piso = new MeshInstance3D
        {
            Name = "PisoMadera",
            Mesh = new PlaneMesh { Size = new Vector2(14, 14) },
            MaterialOverride = material,
        };
        AddChild(piso);

        // El colisionador del piso (capa Piso): es donde se apoyan los
        // componentes. NO es un componente: no conduce electricidad ni calor.
        var cuerpo = new StaticBody3D
        {
            Name = "PisoColision",
            CollisionLayer = CapasFisicas.Piso,
            CollisionMask = 0,
            Position = new Vector3(0, -0.05f, 0),
        };
        cuerpo.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(14, 0.1f, 14) } });
        AddChild(cuerpo);
    }

    private void ConstruirIluminacion()
    {
        var sol = new DirectionalLight3D
        {
            Name = "Sol",
            RotationDegrees = new Vector3(-52, -38, 0),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sol);

        var cielo = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.35f, 0.52f, 0.78f),
            SkyHorizonColor = new Color(0.72f, 0.78f, 0.85f),
            GroundBottomColor = new Color(0.22f, 0.2f, 0.18f),
            GroundHorizonColor = new Color(0.62f, 0.6f, 0.55f),
        };

        // OJO: Godot.Environment, con nombre completo — si no, choca con
        // System.Environment (clásico tropiezo de C# en Godot).
        var entorno = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = cielo },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
        };
        AddChild(new WorldEnvironment { Name = "Ambiente", Environment = entorno });
    }

    // =====================================================================
    //  Interfaz
    // =====================================================================

    private void ConstruirInterfaz()
    {
        var capa = new CanvasLayer { Name = "UI" };
        AddChild(capa);

        _barra = new BarraSuperior();
        _barra.Construir(_sim);
        capa.AddChild(_barra);

        _paleta = new PanelPaleta();
        _paleta.Construir();
        capa.AddChild(_paleta);

        _inspector = new PanelInspector();
        _inspector.Construir(_sim);
        capa.AddChild(_inspector);
        _inspector.EliminarPedido += () => _controlador.EliminarSeleccion();
        _inspector.RotarPedido += () => _controlador.RotarSeleccion();
        // Mover o rotar a mano desde los campos del inspector también puede
        // pegar (o despegar) bornes: hay que rever los contactos.
        _inspector.GeometriaEditada += () => _controlador.GeometriaCambiada();

        // Sincronizar el borde superior de los paneles laterales cuando la
        // barra superior hace wrap responsivo en pantallas angostas.
        void AjustarPanelesLaterales()
        {
            float top = _barra.Position.Y + _barra.Size.Y + 8f;
            _paleta.AjustarMargenSuperior(top);
            _inspector.AjustarMargenSuperior(top);
        }
        _barra.Resized += AjustarPanelesLaterales;
        CallDeferred(MethodName.SincronizarMargenInicialPaneles);

        ConstruirModalAyudaCentrado(capa);
    }

    private void SincronizarMargenInicialPaneles()
    {
        float top = _barra.Position.Y + _barra.Size.Y + 8f;
        _paleta.AjustarMargenSuperior(top);
        _inspector.AjustarMargenSuperior(top);
    }

    private void ConstruirModalAyudaCentrado(CanvasLayer capa)
    {
        // Contenedor de pantalla completa con velo oscuro semitransparente para
        // que la ventana de controles aparezca en el CENTRO exacto y se lea con
        // total nitidez sin superponerse con la lista de componentes ni el inspector.
        var velo = new ColorRect
        {
            Name = "ModalAyudaControles",
            Visible = false,
            Color = new Color(0.02f, 0.03f, 0.05f, 0.65f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        velo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        capa.AddChild(velo);

        var centrador = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        centrador.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        velo.AddChild(centrador);

        var tarjeta = new PanelContainer
        {
            CustomMinimumSize = new Vector2(680, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var estiloModal = new StyleBoxFlat
        {
            BgColor = new Color(0.065f, 0.082f, 0.118f, 0.98f),
            BorderColor = TemaUI.BordeActivo,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = 18,
            ShadowOffset = new Vector2(0, 6),
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
        };
        estiloModal.SetCornerRadiusAll(10);
        tarjeta.AddThemeStyleboxOverride("panel", estiloModal);
        centrador.AddChild(tarjeta);

        var columna = new VBoxContainer();
        columna.AddThemeConstantOverride("separation", 12);
        tarjeta.AddChild(columna);

        // --- Encabezado ---
        var encabezado = new HBoxContainer();
        encabezado.AddThemeConstantOverride("separation", 10);
        var icono = new TextureRect
        {
            Texture = TemaUI.Icono(TemaUI.TipoIcono.Ayuda, TemaUI.AcentoCian),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(20, 20),
        };
        encabezado.AddChild(icono);

        var titulo = new Label
        {
            Text = "Controles y Atajos del Simulador",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titulo.AddThemeFontSizeOverride("font_size", 17);
        titulo.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
        encabezado.AddChild(titulo);

        var btnCerrar = new Button { Text = "Cerrar (Esc)" };
        TemaUI.AplicarEstiloBoton(btnCerrar, peligro: true);
        btnCerrar.Pressed += () => velo.Visible = false;
        encabezado.AddChild(btnCerrar);

        columna.AddChild(encabezado);
        columna.AddChild(new HSeparator());

        // --- Secciones organizadas ---
        AgregarSeccionControles(columna, "CONSTRUCCIÓN Y COMPONENTES", new (string Tecla, string Descripcion)[]
        {
            ("Clic izquierdo", "Coloca el componente elegido en la mesa, selecciona un componente existente o acciona interruptores y pulsadores."),
            ("Arrastrar (Clic izq.)", "Mueve el componente seleccionado sobre el plano de la mesa conservando su altura actual."),
            ("Shift + Clic izq.", "Al colocar un componente, mantiene el modo activo para colocar varios seguidos en serie."),
            ("R  /  Shift + R", "Gira el componente sobre la mesa: 45° con R o giro fino de 15° con Shift + R."),
            ("Tecla T", "Vuelca el componente 90° sobre su eje local (acostarlo o elevarlo de canto)."),
            ("RePág / AvPág / Rueda", "Sube o baja la altura del componente (Shift para ajuste fino de 1 cm). Mientras se arrastra, también funciona con la rueda del ratón."),
            ("Tecla Inicio (Home)", "Apoya inmediatamente el componente al ras de la mesa (altura 0 m)."),
            ("Supr (Delete) / Esc", "Supr elimina el componente o cable seleccionado. Esc cancela la colocación o el cableado actual."),
        });

        AgregarSeccionControles(columna, "CABLEADO Y CONEXIONES", new (string Tecla, string Descripcion)[]
        {
            ("Clic en dos bornes", "Tiende un cable entre el primer borne seleccionado y el segundo."),
            ("Tecla C", "Alterna el tipo del próximo cable entre Simple (ideal) y Físico (cobre real con grosor, resistencia, ampacidad y dobleces)."),
            ("Doble clic en cable físico", "Agrega un punto de doblez (esfera amarilla) en ese tramo del cable para darle forma."),
            ("Arrastrar / Clic der. en doblez", "Arrastrar con clic izquierdo mueve el doblez; clic derecho sobre la esfera elimina ese doblez."),
        });

        AgregarSeccionControles(columna, "CÁMARA 3D", new (string Tecla, string Descripcion)[]
        {
            ("Clic derecho + Mover", "Orbita la cámara 3D alrededor del centro de enfoque en la mesa."),
            ("Clic medio  o  W A S D", "Desplaza lateralmente el punto de enfoque por toda la superficie de trabajo."),
            ("Rueda del ratón", "Acerca o aleja el zoom de la cámara (cuando no se está arrastrando un componente)."),
        });

        _barra.AyudaPedido += () => velo.Visible = !velo.Visible;

        // Permitir cerrar también haciendo click fuera de la tarjeta sobre el velo oscuro
        velo.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                velo.Visible = false;
            }
        };
    }

    private static void AgregarSeccionControles(VBoxContainer padre, string nombreSeccion, (string Tecla, string Descripcion)[] items)
    {
        var lblSeccion = new Label { Text = nombreSeccion };
        lblSeccion.AddThemeFontSizeOverride("font_size", 11);
        lblSeccion.AddThemeColorOverride("font_color", TemaUI.AcentoCian);
        padre.AddChild(lblSeccion);

        var cajaSeccion = new PanelContainer();
        cajaSeccion.AddThemeStyleboxOverride("panel", TemaUI.EstiloCapsula(margenH: 12, margenV: 8, radio: 6));
        padre.AddChild(cajaSeccion);

        var grilla = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grilla.AddThemeConstantOverride("h_separation", 14);
        grilla.AddThemeConstantOverride("v_separation", 6);
        cajaSeccion.AddChild(grilla);

        foreach (var (tecla, desc) in items)
        {
            var badge = new PanelContainer
            {
                CustomMinimumSize = new Vector2(190, 0),
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            var estiloBadge = new StyleBoxFlat
            {
                BgColor = new Color(0.14f, 0.18f, 0.26f, 0.95f),
                BorderColor = new Color(0.28f, 0.36f, 0.50f, 0.65f),
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 3,
                ContentMarginBottom = 3,
            };
            estiloBadge.SetCornerRadiusAll(4);
            badge.AddThemeStyleboxOverride("panel", estiloBadge);

            var lblTecla = new Label
            {
                Text = tecla,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            lblTecla.AddThemeFontSizeOverride("font_size", 12);
            lblTecla.AddThemeColorOverride("font_color", TemaUI.AcentoDorado);
            badge.AddChild(lblTecla);
            grilla.AddChild(badge);

            var lblDesc = new Label
            {
                Text = desc,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            lblDesc.AddThemeFontSizeOverride("font_size", 12);
            lblDesc.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
            grilla.AddChild(lblDesc);
        }
    }
}
