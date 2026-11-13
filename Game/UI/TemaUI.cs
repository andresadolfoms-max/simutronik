using Godot;

namespace ElectricSim.Game.UI;

/// <summary>
/// Sistema de diseño visual tipo CAD / Simulador profesional (Dark Glassmorphism).
/// Centraliza la paleta de colores, estilos de paneles con bordes sutiles,
/// estados hover/active de botones, grupos tipo cápsula y generación procedural
/// de íconos vectoriales monocromáticos de 16x16 píxeles (sin emojis).
/// </summary>
public static class TemaUI
{
    // ---- Paleta Dark CAD ----
    public static readonly Color FondoPanel = new(0.067f, 0.082f, 0.114f, 0.90f);        // #11151D translúcido
    public static readonly Color FondoSubPanel = new(0.098f, 0.122f, 0.168f, 0.85f);     // #191F2B
    public static readonly Color FondoCapsula = new(0.114f, 0.141f, 0.192f, 0.78f);      // #1D2431
    public static readonly Color BordeSutil = new(0.22f, 0.28f, 0.38f, 0.55f);           // Borde 1px cristal
    public static readonly Color BordeActivo = new(0.24f, 0.56f, 0.98f, 0.85f);          // Azul CAD eléctrico

    public static readonly Color BotonNormal = new(0.13f, 0.16f, 0.22f, 0.88f);
    public static readonly Color BotonHover = new(0.19f, 0.24f, 0.33f, 0.96f);
    public static readonly Color BotonPresionado = new(0.16f, 0.36f, 0.66f, 0.95f);
    public static readonly Color BotonDeshabilitado = new(0.10f, 0.12f, 0.16f, 0.45f);

    public static readonly Color BotonPeligroNormal = new(0.28f, 0.12f, 0.14f, 0.90f);
    public static readonly Color BotonPeligroHover = new(0.42f, 0.16f, 0.18f, 0.96f);

    public static readonly Color TextoPrincipal = new(0.93f, 0.95f, 0.98f, 1.0f);
    public static readonly Color TextoSecundario = new(0.64f, 0.70f, 0.80f, 1.0f);
    public static readonly Color TextoTenue = new(0.48f, 0.54f, 0.64f, 1.0f);
    public static readonly Color AcentoCian = new(0.32f, 0.72f, 0.98f, 1.0f);
    public static readonly Color AcentoDorado = new(0.98f, 0.80f, 0.32f, 1.0f);
    public static readonly Color AcentoVerde = new(0.30f, 0.85f, 0.54f, 1.0f);
    public static readonly Color AcentoAlerta = new(0.98f, 0.52f, 0.24f, 1.0f);

    /// <summary>Estilo principal para paneles flotantes (barra superior, paleta, inspector, modal de ayuda).</summary>
    public static StyleBoxFlat EstiloPanelFlotante(int radioEsquina = 8, int margenH = 12, int margenV = 10)
    {
        var s = new StyleBoxFlat
        {
            BgColor = FondoPanel,
            BorderColor = BordeSutil,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ShadowColor = new Color(0f, 0f, 0f, 0.38f),
            ShadowSize = 10,
            ShadowOffset = new Vector2(0, 4),
            ContentMarginLeft = margenH,
            ContentMarginRight = margenH,
            ContentMarginTop = margenV,
            ContentMarginBottom = margenV,
        };
        s.SetCornerRadiusAll(radioEsquina);
        return s;
    }

    /// <summary>Estilo de cápsula para agrupar controles relacionados en la barra superior o tarjetas de lectura.</summary>
    public static StyleBoxFlat EstiloCapsula(int margenH = 8, int margenV = 4, int radio = 6)
    {
        var s = new StyleBoxFlat
        {
            BgColor = FondoCapsula,
            BorderColor = new Color(0.20f, 0.26f, 0.35f, 0.45f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = margenH,
            ContentMarginRight = margenH,
            ContentMarginTop = margenV,
            ContentMarginBottom = margenV,
        };
        s.SetCornerRadiusAll(radio);
        return s;
    }

    /// <summary>Estilo de botón CAD moderno con borde sutil y estados hover/pressed claros.</summary>
    public static void AplicarEstiloBoton(Button b, bool destacado = false, bool peligro = false)
    {
        Color bgNormal = peligro ? BotonPeligroNormal : destacado ? new Color(0.15f, 0.28f, 0.48f, 0.92f) : BotonNormal;
        Color bgHover = peligro ? BotonPeligroHover : destacado ? new Color(0.20f, 0.36f, 0.62f, 0.98f) : BotonHover;
        Color bordeNorm = peligro ? new Color(0.55f, 0.22f, 0.25f, 0.65f) : destacado ? BordeActivo : BordeSutil;
        Color bordeHov = peligro ? new Color(0.75f, 0.30f, 0.32f, 0.90f) : BordeActivo;

        b.AddThemeStyleboxOverride("normal", CajaBoton(bgNormal, bordeNorm));
        b.AddThemeStyleboxOverride("hover", CajaBoton(bgHover, bordeHov));
        b.AddThemeStyleboxOverride("pressed", CajaBoton(BotonPresionado, BordeActivo));
        b.AddThemeStyleboxOverride("hover_pressed", CajaBoton(BotonPresionado.Lightened(0.08f), BordeActivo.Lightened(0.15f)));
        b.AddThemeStyleboxOverride("disabled", CajaBoton(BotonDeshabilitado, new Color(0.18f, 0.22f, 0.28f, 0.25f)));
        b.AddThemeStyleboxOverride("focus", CajaBoton(bgHover, BordeActivo));

        b.AddThemeColorOverride("font_color", TextoPrincipal);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.50f, 0.55f, 0.62f, 0.55f));
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeConstantOverride("h_separation", 6);
    }

    private static StyleBoxFlat CajaBoton(Color fondo, Color borde)
    {
        var s = new StyleBoxFlat
        {
            BgColor = fondo,
            BorderColor = borde,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 9,
            ContentMarginRight = 9,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
        };
        s.SetCornerRadiusAll(5);
        return s;
    }

    // =====================================================================
    //  Íconos vectoriales minimalistas (16x16, monocromáticos con color de acento)
    // =====================================================================

    public enum TipoIcono
    {
        Menu,
        Componentes,
        Guardar,
        Cargar,
        Play,
        Pausa,
        Grabar,
        Termica,
        Grilla,
        Bornes,
        Mesa,
        Flujo,
        Contacto,
        Ayuda,
        Rotar,
        Eliminar,
        Reparar,
        Buscar,
    }

    private static readonly Dictionary<(TipoIcono, Color), ImageTexture> _cacheIconos = new();

    public static ImageTexture Icono(TipoIcono tipo, Color? color = null)
    {
        Color c = color ?? AcentoCian;
        if (_cacheIconos.TryGetValue((tipo, c), out var tex)) return tex;

        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        void Px(int x, int y, float alpha = 1f)
        {
            if (x >= 0 && x < 16 && y >= 0 && y < 16)
            {
                var prev = img.GetPixel(x, y);
                float a = Mathf.Max(prev.A, c.A * alpha);
                img.SetPixel(x, y, new Color(c.R, c.G, c.B, a));
            }
        }

        void HLine(int x1, int x2, int y)
        {
            for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++) Px(x, y);
        }

        void VLine(int x, int y1, int y2)
        {
            for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++) Px(x, y);
        }

        void Rect(int x1, int y1, int x2, int y2, bool fill = false)
        {
            if (fill)
            {
                for (int y = y1; y <= y2; y++) HLine(x1, x2, y);
            }
            else
            {
                HLine(x1, x2, y1);
                HLine(x1, x2, y2);
                VLine(x1, y1, y2);
                VLine(x2, y1, y2);
            }
        }

        void Circle(float cx, float cy, float r, bool fill = false)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (fill)
                    {
                        if (d <= r + 0.4f) Px(x, y, Mathf.Clamp(r + 0.6f - d, 0f, 1f));
                    }
                    else
                    {
                        float distBorde = Mathf.Abs(d - r);
                        if (distBorde <= 0.85f) Px(x, y, Mathf.Clamp(1.0f - distBorde * 0.8f, 0.25f, 1f));
                    }
                }
            }
        }

        switch (tipo)
        {
            case TipoIcono.Menu:
                // Ícono de casa / menú principal
                for (int i = 0; i <= 5; i++)
                {
                    Px(7 - i, 2 + i);
                    Px(8 + i, 2 + i);
                    Px(7 - i, 3 + i);
                    Px(8 + i, 3 + i);
                }
                Rect(4, 7, 11, 13);
                Rect(7, 10, 8, 13, fill: true);
                break;

            case TipoIcono.Componentes:
                // Ícono de chip integrado (cuerpo cuadrado + pines laterales)
                Rect(4, 4, 11, 11);
                Rect(6, 6, 9, 9, fill: true);
                foreach (int p in new[] { 5, 8, 10 })
                {
                    HLine(1, 3, p);
                    HLine(12, 14, p);
                    VLine(p, 1, 3);
                    VLine(p, 12, 14);
                }
                break;

            case TipoIcono.Guardar:
                // Bandeja con flecha hacia abajo
                HLine(2, 13, 13);
                VLine(2, 10, 13);
                VLine(13, 10, 13);
                VLine(7, 2, 10);
                VLine(8, 2, 10);
                for (int i = 1; i <= 3; i++)
                {
                    Px(7 - i, 10 - i);
                    Px(8 + i, 10 - i);
                }
                break;

            case TipoIcono.Cargar:
                // Carpeta minimalista
                HLine(2, 6, 3);
                Px(7, 4);
                HLine(8, 13, 4);
                Rect(2, 5, 13, 12);
                HLine(3, 12, 7);
                break;

            case TipoIcono.Pausa:
                // Dos barras verticales limpias
                Rect(4, 3, 6, 12, fill: true);
                Rect(9, 3, 11, 12, fill: true);
                break;

            case TipoIcono.Play:
                // Triángulo de reproducción
                for (int x = 4; x <= 12; x++)
                {
                    int span = (12 - x) / 2;
                    VLine(x, 7 - span, 8 + span);
                }
                break;

            case TipoIcono.Grabar:
                // Círculo exterior con punto central de grabación
                Circle(7.5f, 7.5f, 5.5f, fill: false);
                Circle(7.5f, 7.5f, 2.5f, fill: true);
                break;

            case TipoIcono.Termica:
                // Termómetro minimalista
                VLine(7, 2, 10);
                VLine(8, 2, 10);
                Circle(7.5f, 11.5f, 2.5f, fill: true);
                HLine(10, 12, 4);
                HLine(10, 11, 6);
                HLine(10, 12, 8);
                break;

            case TipoIcono.Grilla:
                // Cuadrícula de 3x3 puntos/líneas
                foreach (int p in new[] { 3, 7, 12 })
                {
                    HLine(2, 13, p);
                    VLine(p, 2, 13);
                }
                break;

            case TipoIcono.Bornes:
                // Nodo de conexión con dos terminales
                HLine(1, 4, 8);
                HLine(11, 14, 8);
                Circle(7.5f, 8f, 3.0f, fill: false);
                Circle(7.5f, 8f, 1.2f, fill: true);
                break;

            case TipoIcono.Mesa:
                // Perspectiva de superficie de trabajo
                HLine(4, 11, 4);
                HLine(2, 13, 11);
                for (int i = 0; i <= 7; i++)
                {
                    Px(4 - i / 3, 4 + i);
                    Px(11 + i / 3, 4 + i);
                }
                VLine(3, 11, 14);
                VLine(12, 11, 14);
                break;

            case TipoIcono.Flujo:
                // Onda de pulso eléctrico
                HLine(1, 3, 8);
                VLine(4, 3, 8);
                Px(5, 3);
                VLine(6, 3, 12);
                Px(7, 12);
                VLine(8, 5, 12);
                Px(9, 5);
                VLine(10, 5, 8);
                HLine(11, 14, 8);
                break;

            case TipoIcono.Contacto:
                // Dos anillos enlazados
                Circle(5.5f, 8f, 3.2f, fill: false);
                Circle(10.0f, 8f, 3.2f, fill: false);
                break;

            case TipoIcono.Ayuda:
                // Signo de interrogación limpio en círculo
                Circle(7.5f, 7.5f, 6.2f, fill: false);
                HLine(6, 9, 4);
                Px(5, 5);
                Px(10, 5);
                Px(10, 6);
                Px(9, 7);
                Px(8, 8);
                Px(7, 9);
                Rect(7, 11, 8, 12, fill: true);
                break;

            case TipoIcono.Rotar:
                // Arco circular con punta de flecha
                Circle(7.5f, 8f, 4.8f, fill: false);
                HLine(10, 13, 5);
                VLine(12, 2, 5);
                break;

            case TipoIcono.Eliminar:
                // Cesto / cruz limpia
                HLine(3, 12, 4);
                HLine(6, 9, 2);
                Rect(4, 5, 11, 13);
                VLine(6, 7, 11);
                VLine(9, 7, 11);
                break;

            case TipoIcono.Reparar:
                // Llave / pulso de restauración
                Circle(5.5f, 5.5f, 2.8f, fill: false);
                for (int i = 0; i < 6; i++)
                {
                    Px(7 + i, 7 + i);
                    Px(8 + i, 7 + i);
                }
                break;

            case TipoIcono.Buscar:
                // Lupa minimalista
                Circle(6.5f, 6.5f, 4.0f, fill: false);
                for (int i = 0; i < 4; i++)
                {
                    Px(10 + i, 10 + i);
                    Px(9 + i, 10 + i);
                }
                break;
        }

        var textura = ImageTexture.CreateFromImage(img);
        _cacheIconos[(tipo, c)] = textura;
        return textura;
    }
}

