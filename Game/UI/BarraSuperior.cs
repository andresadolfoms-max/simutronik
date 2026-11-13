using Godot;
using ElectricSim.Core;

namespace ElectricSim.Game.UI;

/// <summary>
/// Barra superior responsiva con estilo CAD oscuro.
/// Usa un contenedor HFlowContainer (flexbox con wrap automático) y agrupa los
/// controles en cápsulas temáticas con íconos vectoriales minimalistas para que
/// ningún botón se corte ni salga de la pantalla en ninguna resolución.
/// </summary>
public partial class BarraSuperior : PanelContainer
{
    /// <summary>El usuario abrió/cerró la paleta de componentes.</summary>
    public event Action<bool>? PaletaAlternada;

    /// <summary>El usuario prendió/apagó el snap a la grilla.</summary>
    public event Action<bool>? SnapCambiado;

    /// <summary>El usuario cambió el modo de visibilidad de los bornes.</summary>
    public event Action<ModoBornes>? ModoBornesCambiado;

    /// <summary>El usuario quiere elegir otra imagen para la mesa.</summary>
    public event Action? MesaPedido;

    /// <summary>Se prendió/apagó el contacto entre bornes: hay que recalcular.</summary>
    public event Action<bool>? ContactoAlternado;

    /// <summary>El usuario pidió guardar el circuito a un archivo.</summary>
    public event Action? GuardarPedido;

    /// <summary>El usuario pidió cargar un circuito desde un archivo.</summary>
    public event Action? CargarPedido;

    /// <summary>El usuario pidió grabar un video (solo posible en pausa).</summary>
    public event Action? GrabarPedido;

    /// <summary>El usuario prendió/apagó TODO el sistema térmico.</summary>
    public event Action<bool>? TermicaAlternada;

    /// <summary>El usuario pidió abrir/cerrar la ventana central de controles y ayuda.</summary>
    public event Action? AyudaPedido;

    /// <summary>El usuario pidió volver al menú principal de inicio.</summary>
    public event Action? MenuPrincipalPedido;

    private Label _modo = null!;
    private Simulacion _sim = null!;
    private SpinBox _ticks = null!;
    private SpinBox _temperatura = null!;
    private Button _pausa = null!;
    private Button _grabar = null!;
    private Button _termica = null!;
    private Button _flujo = null!;
    private Button _contacto = null!;

    public void Construir(Simulacion sim)
    {
        _sim = sim;
        Name = "BarraSuperior";

        AnchorLeft = 0;
        AnchorRight = 1;
        AnchorTop = 0;
        AnchorBottom = 0;
        OffsetLeft = 8;
        OffsetRight = -8;
        OffsetTop = 6;
        OffsetBottom = 48;

        AddThemeStyleboxOverride("panel", TemaUI.EstiloPanelFlotante(radioEsquina: 8, margenH: 10, margenV: 6));

        // Contenedor tipo Flexbox con wrap (HFlowContainer): si la ventana es
        // angosta, las cápsulas bajan automáticamente a una segunda fila sin
        // recortarse jamás en el borde derecho.
        var flujo = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        flujo.AddThemeConstantOverride("h_separation", 8);
        flujo.AddThemeConstantOverride("v_separation", 6);
        AddChild(flujo);

        // =================================================================
        //  GRUPO 1: Catálogo y Archivo
        // =================================================================
        var gArchivo = CrearCapsula(flujo);

        var menu = new Button
        {
            Text = "Menú",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Menu, TemaUI.TextoPrincipal),
            TooltipText = "Volver al menú principal de inicio",
        };
        TemaUI.AplicarEstiloBoton(menu);
        menu.Pressed += () => MenuPrincipalPedido?.Invoke();
        gArchivo.AddChild(menu);

        var paleta = new Button
        {
            Text = "Electrónicos",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Componentes),
            ToggleMode = true,
            ButtonPressed = true,
            TooltipText = "Mostrar u ocultar el catálogo lateral de componentes electrónicos",
        };
        TemaUI.AplicarEstiloBoton(paleta, destacado: true);
        paleta.Toggled += visible => PaletaAlternada?.Invoke(visible);
        gArchivo.AddChild(paleta);

        var guardar = new Button
        {
            Text = "Guardar",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Guardar, TemaUI.TextoSecundario),
            TooltipText = "Guardar el circuito actual en un archivo .json",
        };
        TemaUI.AplicarEstiloBoton(guardar);
        guardar.Pressed += () => GuardarPedido?.Invoke();
        gArchivo.AddChild(guardar);

        var cargar = new Button
        {
            Text = "Cargar",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Cargar, TemaUI.TextoSecundario),
            TooltipText = "Cargar un circuito guardado desde un archivo .json",
        };
        TemaUI.AplicarEstiloBoton(cargar);
        cargar.Pressed += () => CargarPedido?.Invoke();
        gArchivo.AddChild(cargar);

        // =================================================================
        //  GRUPO 2: Simulación y Grabación
        // =================================================================
        var gSim = CrearCapsula(flujo);

        _pausa = new Button
        {
            Text = "Pausa",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Pausa, TemaUI.AcentoDorado),
            ToggleMode = true,
            TooltipText = "Pausar o reanudar la simulación eléctrica",
        };
        TemaUI.AplicarEstiloBoton(_pausa);
        _pausa.Toggled += presionado =>
        {
            _sim.EnPausa = presionado;
            _pausa.Text = presionado ? "Reanudar" : "Pausa";
            _pausa.Icon = TemaUI.Icono(
                presionado ? TemaUI.TipoIcono.Play : TemaUI.TipoIcono.Pausa,
                presionado ? TemaUI.AcentoVerde : TemaUI.AcentoDorado);
        };
        gSim.AddChild(_pausa);

        _grabar = new Button
        {
            Text = "Grabar",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Grabar, new Color(0.95f, 0.40f, 0.42f)),
            Disabled = true,
            TooltipText = "Graba un video MP4 determinístico desde la cámara actual.\nDisponible únicamente con la simulación en pausa.",
        };
        TemaUI.AplicarEstiloBoton(_grabar);
        _grabar.Pressed += () => GrabarPedido?.Invoke();
        gSim.AddChild(_grabar);

        gSim.AddChild(EtiquetaCampo("Ticks/s:"));
        _ticks = new SpinBox
        {
            MinValue = 1,
            MaxValue = 120,
            Step = 1,
            Value = _sim.TicksPorSegundo,
            CustomMinimumSize = new Vector2(68, 0),
            TooltipText = "Frecuencia de actualización del solver eléctrico (iteraciones por segundo)",
        };
        _ticks.ValueChanged += v => _sim.TicksPorSegundo = v;
        gSim.AddChild(_ticks);

        // =================================================================
        //  GRUPO 3: Sistema Térmico
        // =================================================================
        var gTermica = CrearCapsula(flujo);

        _termica = new Button
        {
            Text = SimuladorTermico.Habilitada ? "Térmica ON" : "Térmica OFF",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Termica, TemaUI.AcentoAlerta),
            ToggleMode = true,
            ButtonPressed = SimuladorTermico.Habilitada,
            TooltipText = "Activa o desactiva la simulación térmica, disipación de calor y ruptura por temperatura",
        };
        TemaUI.AplicarEstiloBoton(_termica);
        _termica.Toggled += activada =>
        {
            SimuladorTermico.Habilitada = activada;
            _termica.Text = activada ? "Térmica ON" : "Térmica OFF";
            _termica.TooltipText = activada
                ? "Simulación térmica ACTIVA: los componentes disipan calor y pueden quemarse por exceso de temperatura"
                : "Simulación térmica APAGADA: modo puramente eléctrico sin calentamiento";
            _temperatura.Editable = activada;
            TermicaAlternada?.Invoke(activada);
        };
        gTermica.AddChild(_termica);

        gTermica.AddChild(EtiquetaCampo("Amb:"));
        _temperatura = new SpinBox
        {
            MinValue = -20,
            MaxValue = 60,
            Step = 1,
            Value = _sim.TemperaturaAmbiente,
            Suffix = "°C",
            CustomMinimumSize = new Vector2(74, 0),
            TooltipText = "Temperatura ambiente de referencia hacia la cual se enfrían los componentes",
        };
        _temperatura.ValueChanged += v => _sim.TemperaturaAmbiente = v;
        gTermica.AddChild(_temperatura);

        // =================================================================
        //  GRUPO 4: Área de Trabajo y Conexiones
        // =================================================================
        var gVista = CrearCapsula(flujo);

        var snap = new CheckBox
        {
            Text = "Grilla",
            ButtonPressed = true,
            TooltipText = "Alinear la colocación de componentes a pasos de 10 cm",
        };
        snap.AddThemeFontSizeOverride("font_size", 12);
        snap.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
        snap.Toggled += activado => SnapCambiado?.Invoke(activado);
        gVista.AddChild(snap);

        gVista.AddChild(EtiquetaCampo("Bornes:"));
        var bornes = new OptionButton
        {
            TooltipText = "Visibilidad de los nodos de conexión:\n• Siempre visibles\n• Al interactuar (pasar el cursor o cablear)\n• Ocultos (siguen siendo clickeables)",
        };
        bornes.AddThemeFontSizeOverride("font_size", 12);
        bornes.AddItem("Siempre", (int)ModoBornes.Siempre);
        bornes.AddItem("Al interactuar", (int)ModoBornes.AlInteractuar);
        bornes.AddItem("Ocultos", (int)ModoBornes.Ocultos);
        bornes.Selected = 0;
        bornes.ItemSelected += indice => ModoBornesCambiado?.Invoke((ModoBornes)(int)indice);
        gVista.AddChild(bornes);

        var mesa = new Button
        {
            Text = "Mesa",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Mesa, TemaUI.TextoSecundario),
            TooltipText = "Cambiar la textura o superficie de la mesa de trabajo",
        };
        TemaUI.AplicarEstiloBoton(mesa);
        mesa.Pressed += () => MesaPedido?.Invoke();
        gVista.AddChild(mesa);

        _flujo = new Button
        {
            Text = CableVisual.FlujoVisible ? "Flujo ON" : "Flujo OFF",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Flujo, TemaUI.AcentoCian),
            ToggleMode = true,
            ButtonPressed = CableVisual.FlujoVisible,
            TooltipText = "Muestra el pulso visual de corriente circulando por los cables",
        };
        TemaUI.AplicarEstiloBoton(_flujo);
        _flujo.Toggled += activo =>
        {
            CableVisual.FlujoVisible = activo;
            _flujo.Text = activo ? "Flujo ON" : "Flujo OFF";
        };
        gVista.AddChild(_flujo);

        _contacto = new Button
        {
            Text = ContactoEntreBornes.Habilitado ? "Contacto ON" : "Contacto OFF",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Contacto, TemaUI.AcentoVerde),
            ToggleMode = true,
            ButtonPressed = ContactoEntreBornes.Habilitado,
            TooltipText = "Conexión directa por proximidad cuando dos bornes metálicos se tocan",
        };
        TemaUI.AplicarEstiloBoton(_contacto);
        _contacto.Toggled += activo =>
        {
            ContactoEntreBornes.Habilitado = activo;
            _contacto.Text = activo ? "Contacto ON" : "Contacto OFF";
            ContactoAlternado?.Invoke(activo);
        };
        gVista.AddChild(_contacto);

        var ayuda = new Button
        {
            Text = "Controles (?)",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Ayuda, TemaUI.AcentoCian),
            TooltipText = "Abrir la guía central de controles, teclado y ratón (F1)",
        };
        TemaUI.AplicarEstiloBoton(ayuda, destacado: true);
        ayuda.Pressed += () => AyudaPedido?.Invoke();
        gVista.AddChild(ayuda);

        // =================================================================
        //  Indicador de modo contextual
        // =================================================================
        _modo = new Label
        {
            Text = "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _modo.AddThemeFontSizeOverride("font_size", 12);
        _modo.AddThemeColorOverride("font_color", TemaUI.AcentoDorado);
        flujo.AddChild(_modo);
    }

    private static HBoxContainer CrearCapsula(Container padre)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", TemaUI.EstiloCapsula(margenH: 6, margenV: 3, radio: 6));
        padre.AddChild(panel);

        var fila = new HBoxContainer();
        fila.AddThemeConstantOverride("separation", 5);
        panel.AddChild(fila);
        return fila;
    }

    private static Label EtiquetaCampo(string texto)
    {
        var lbl = new Label
        {
            Text = texto,
            VerticalAlignment = VerticalAlignment.Center,
        };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        lbl.AddThemeColorOverride("font_color", TemaUI.TextoSecundario);
        return lbl;
    }

    public override void _Process(double delta)
    {
        _grabar.Disabled = !_sim.EnPausa;
    }

    public void MostrarModo(string texto) => _modo.Text = texto;

    /// <summary>Sincroniza los controles de la barra con los valores actuales de la simulación tras cargar un proyecto.</summary>
    public void Sincronizar()
    {
        _ticks.Value = _sim.TicksPorSegundo;
        _temperatura.Value = _sim.TemperaturaAmbiente;
        _termica.ButtonPressed = SimuladorTermico.Habilitada;
        _termica.Text = SimuladorTermico.Habilitada ? "Térmica ON" : "Térmica OFF";
        _temperatura.Editable = SimuladorTermico.Habilitada;
        _flujo.ButtonPressed = CableVisual.FlujoVisible;
        _flujo.Text = CableVisual.FlujoVisible ? "Flujo ON" : "Flujo OFF";
        _contacto.ButtonPressed = ContactoEntreBornes.Habilitado;
        _contacto.Text = ContactoEntreBornes.Habilitado ? "Contacto ON" : "Contacto OFF";
    }
}
