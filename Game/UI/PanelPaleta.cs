using Godot;

namespace ElectricSim.Game.UI;

/// <summary>
/// Panel lateral izquierdo de componentes electrónicos con estética CAD oscura,
/// buscador rápido integrado, scroll interno con altura máxima acotada (nunca
/// choca con controles inferiores) y tarjetas desplegables con estados hover claros.
/// </summary>
public partial class PanelPaleta : PanelContainer
{
    /// <summary>El usuario eligió una variante para colocar.</summary>
    public event Action<DefinicionComponente>? VarianteElegida;

    private static readonly Color ColorSinLimites = TemaUI.AcentoDorado;
    private static readonly Color ColorDescripcion = TemaUI.TextoSecundario;
    private static readonly Color ColorFondoTarjeta = new(0.12f, 0.15f, 0.21f, 0.78f);
    private static readonly Color ColorFondoHover = new(0.18f, 0.23f, 0.32f, 0.92f);
    private static readonly Color ColorFondoAbierta = new(0.15f, 0.28f, 0.48f, 0.88f);

    private VBoxContainer _listaTarjetas = null!;
    private LineEdit _buscador = null!;
    private string _filtroActual = "";

    public void Construir()
    {
        Name = "PanelPaleta";

        // Anclado a la izquierda con altura máxima acotada (deja libre la franja
        // inferior para que nunca se superponga con botones flotantes ni avisos).
        AnchorLeft = 0;
        AnchorRight = 0;
        AnchorTop = 0;
        AnchorBottom = 1;
        OffsetLeft = 8;
        OffsetRight = 296;
        OffsetTop = 58;
        OffsetBottom = -12;

        AddThemeStyleboxOverride("panel", TemaUI.EstiloPanelFlotante(radioEsquina: 8, margenH: 12, margenV: 10));

        var raiz = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        raiz.AddThemeConstantOverride("separation", 8);
        AddChild(raiz);

        // --- Encabezado con ícono minimalista ---
        var filaTitulo = new HBoxContainer();
        filaTitulo.AddThemeConstantOverride("separation", 8);
        raiz.AddChild(filaTitulo);

        var iconoTitulo = new TextureRect
        {
            Texture = TemaUI.Icono(TemaUI.TipoIcono.Componentes, TemaUI.AcentoCian),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(18, 18),
        };
        filaTitulo.AddChild(iconoTitulo);

        var titulo = new Label
        {
            Text = "Electrónicos",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titulo.AddThemeFontSizeOverride("font_size", 16);
        titulo.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
        filaTitulo.AddChild(titulo);

        // --- Buscador rápido ---
        _buscador = new LineEdit
        {
            PlaceholderText = "Filtrar componente...",
            ClearButtonEnabled = true,
            RightIcon = TemaUI.Icono(TemaUI.TipoIcono.Buscar, TemaUI.TextoSecundario),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _buscador.AddThemeFontSizeOverride("font_size", 12);
        var estiloBuscador = TemaUI.EstiloCapsula(margenH: 8, margenV: 5, radio: 6);
        _buscador.AddThemeStyleboxOverride("normal", estiloBuscador);
        _buscador.AddThemeStyleboxOverride("focus", new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.17f, 0.24f, 0.95f),
            BorderColor = TemaUI.BordeActivo,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        });
        _buscador.TextChanged += texto =>
        {
            _filtroActual = texto.Trim();
            Reconstruir();
        };
        raiz.AddChild(_buscador);

        raiz.AddChild(new HSeparator());

        // --- Lista con scroll interno propio ---
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        raiz.AddChild(scroll);

        _listaTarjetas = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _listaTarjetas.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(_listaTarjetas);

        Reconstruir();
    }

    /// <summary>Ajusta dinámicamente el tope superior cuando la barra superior cambia de altura (responsive wrap).</summary>
    public void AjustarMargenSuperior(float topPx)
    {
        OffsetTop = Math.Max(54f, topPx);
    }

    private void Reconstruir()
    {
        if (!GodotObject.IsInstanceValid(_listaTarjetas)) return;

        for (int i = _listaTarjetas.GetChildCount() - 1; i >= 0; i--)
        {
            var hijo = _listaTarjetas.GetChild(i);
            _listaTarjetas.RemoveChild(hijo);
            hijo.QueueFree();
        }

        bool hayFiltro = !string.IsNullOrEmpty(_filtroActual);

        foreach (var tipo in CatalogoComponentes.Tipos)
        {
            if (hayFiltro &&
                !tipo.Nombre.Contains(_filtroActual, StringComparison.OrdinalIgnoreCase) &&
                !tipo.Descripcion.Contains(_filtroActual, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            ConstruirTarjetaTipo(_listaTarjetas, tipo, expandirPorDefecto: hayFiltro);
        }
    }

    private void ConstruirTarjetaTipo(VBoxContainer padre, DefinicionTipo tipo, bool expandirPorDefecto)
    {
        var encabezado = new Button
        {
            ToggleMode = true,
            ButtonPressed = expandirPorDefecto,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        encabezado.AddThemeFontSizeOverride("font_size", 13);
        encabezado.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
        encabezado.AddThemeColorOverride("font_hover_color", Colors.White);
        encabezado.AddThemeStyleboxOverride("normal", EstiloFila(ColorFondoTarjeta, new Color(0.22f, 0.28f, 0.38f, 0.38f)));
        encabezado.AddThemeStyleboxOverride("hover", EstiloFila(ColorFondoHover, TemaUI.BordeActivo));
        encabezado.AddThemeStyleboxOverride("pressed", EstiloFila(ColorFondoAbierta, TemaUI.BordeActivo));
        encabezado.AddThemeStyleboxOverride("hover_pressed", EstiloFila(ColorFondoAbierta.Lightened(0.06f), TemaUI.BordeActivo));
        encabezado.AddThemeStyleboxOverride("focus", EstiloFila(ColorFondoHover, TemaUI.BordeActivo));
        padre.AddChild(encabezado);

        var margen = new MarginContainer { Visible = expandirPorDefecto };
        margen.AddThemeConstantOverride("margin_left", 10);
        margen.AddThemeConstantOverride("margin_right", 4);
        margen.AddThemeConstantOverride("margin_top", 2);
        margen.AddThemeConstantOverride("margin_bottom", 6);
        padre.AddChild(margen);

        var cajaInterna = new PanelContainer();
        cajaInterna.AddThemeStyleboxOverride("panel", TemaUI.EstiloCapsula(margenH: 8, margenV: 6, radio: 6));
        margen.AddChild(cajaInterna);

        var cuerpo = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cuerpo.AddThemeConstantOverride("separation", 4);
        cajaInterna.AddChild(cuerpo);

        var descripcion = new Label
        {
            Text = tipo.Descripcion,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        descripcion.AddThemeFontSizeOverride("font_size", 11);
        descripcion.AddThemeColorOverride("font_color", ColorDescripcion);
        cuerpo.AddChild(descripcion);

        foreach (var variante in tipo.Variantes)
        {
            var def = new DefinicionComponente(tipo, variante);

            var boton = new Button
            {
                Text = variante.SinLimites ? $"∞  {variante.Nombre}" : $"•  {variante.Nombre}",
                TooltipText = variante.Descripcion,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            TemaUI.AplicarEstiloBoton(boton, destacado: !variante.SinLimites);
            if (variante.SinLimites)
            {
                boton.AddThemeColorOverride("font_color", ColorSinLimites);
                boton.AddThemeColorOverride("font_hover_color", ColorSinLimites.Lightened(0.2f));
            }
            boton.Pressed += () => VarianteElegida?.Invoke(def);
            cuerpo.AddChild(boton);
        }

        void ActualizarFlecha() =>
            encabezado.Text = (encabezado.ButtonPressed ? "▾   " : "▸   ") + tipo.Nombre;
        ActualizarFlecha();
        encabezado.Toggled += abierto =>
        {
            margen.Visible = abierto;
            ActualizarFlecha();
        };
    }

    private static StyleBoxFlat EstiloFila(Color fondo, Color borde)
    {
        var estilo = new StyleBoxFlat
        {
            BgColor = fondo,
            BorderColor = borde,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 7,
            ContentMarginBottom = 7,
        };
        estilo.SetCornerRadiusAll(6);
        return estilo;
    }

    /// <summary>Abre o cierra el panel lateral.</summary>
    public void Alternar(bool visible) => Visible = visible;
}
