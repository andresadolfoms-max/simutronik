using Godot;
using ElectricSim.Core;
using ElectricSim.Core.Components;

namespace ElectricSim.Game.UI;

/// <summary>
/// El panel de la derecha, versión 2.0: la ficha del componente (o cable)
/// seleccionado. Tres zonas:
///
///  - LECTURAS EN VIVO (refrescadas cada tick): las universales (voltaje,
///    corriente — la MISMA en ambas patas, y lo decimos —, potencia,
///    temperatura, tipo de corriente) más las propias de cada tipo, que
///    ahora salen del catálogo (Def.Lecturas): brillo, carga, estado del
///    objetivo de corriente... Y las advertencias (quemado, corto,
///    incompatible).
///
///  - ESTADÍSTICAS EDITABLES: generadas recorriendo las StatDef del
///    catálogo. Cada stat trae su TIPO de control (número, número con
///    check ∞, casilla, opciones), su DESCRIPCIÓN en criollo y su fórmula
///    — el panel explica, no tira números pelados. Los checks ∞ reemplazan
///    al viejo "999" mentiroso: tildado = ese límite no existe.
///
///  - ROTACIÓN LIBRE: tres campos numéricos (X/Y/Z) sincronizados con el
///    componente, además de las teclas (R rota, Shift+R fino, T vuelca).
///
/// Agregar una estadística o una lectura a un componente sigue sin tocar
/// este archivo: es una entrada más en el catálogo.
/// </summary>
public partial class PanelInspector : PanelContainer
{
    public event Action? EliminarPedido;
    public event Action? RotarPedido;

    /// <summary>
    /// El usuario movió o rotó el componente a mano desde los campos X/Y/Z.
    /// Main lo conecta al recálculo de contactos entre bornes: escribir una
    /// posición puede pegar dos bornes igual que arrastrar con el mouse.
    /// </summary>
    public event Action? GeometriaEditada;

    private VBoxContainer _contenido = null!;
    private GodotObject? _objeto;
    private Simulacion _sim = null!;

    // Etiquetas de lectura viva, con su función de refresco (cada tick).
    private readonly List<(Label etiqueta, Func<string> leer)> _lecturas = new();

    // Controles con condición de habilitado (HabilitadoSi): se re-evalúan
    // cada tick y al tocar cualquier casilla (barato: son pocos).
    private readonly List<(Func<bool> condicion, Action<bool> aplicar)> _habilitables = new();

    // Sincronizadores: controles que copian un valor del mundo (ej: la
    // rotación real tras apretar R) SIN pisar al usuario mientras tipea.
    private readonly List<Action> _sincronizadores = new();

    private static readonly Color ColorDescripcion = new(1, 1, 1, 0.5f);
    private static readonly Color ColorFormula = new(0.65f, 0.85f, 1f, 0.8f);
    private static readonly Color ColorAdvertencia = new(1f, 0.55f, 0.25f);

    public void Construir(Simulacion sim)
    {
        _sim = sim;
        Name = "PanelInspector";

        // Anclado al borde derecho, debajo de la barra superior.
        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -376; OffsetRight = -8; OffsetTop = 58; OffsetBottom = -12;

        AddThemeStyleboxOverride("panel", TemaUI.EstiloPanelFlotante(radioEsquina: 8, margenH: 12, margenV: 10));

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        _contenido = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _contenido.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_contenido);

        Visible = false; // aparece cuando hay algo seleccionado
    }

    /// <summary>Ajusta dinámicamente el margen superior cuando la barra superior cambia de altura.</summary>
    public void AjustarMargenSuperior(float topPx)
    {
        OffsetTop = Math.Max(54f, topPx);
    }

    /// <summary>Reconstruye el panel para el objeto seleccionado (o lo esconde con null).</summary>
    public void Mostrar(GodotObject? objeto)
    {
        _objeto = objeto;
        _lecturas.Clear();
        _habilitables.Clear();
        _sincronizadores.Clear();
        foreach (var hijo in _contenido.GetChildren())
        {
            // QueueFree recién libera al final del frame: si no lo sacamos del
            // árbol YA, los controles viejos conviven un frame con los nuevos
            // y el panel "salta" (y algún SpinBox viejo puede robar el foco).
            _contenido.RemoveChild(hijo);
            hijo.QueueFree();
        }

        switch (objeto)
        {
            case ComponenteVisual visual:
                ArmarParaComponente(visual);
                Visible = true;
                break;
            case CableVisual cable:
                ArmarParaCable(cable);
                Visible = true;
                break;
            default:
                Visible = false;
                break;
        }
        ActualizarHabilitados();
    }

    /// <summary>Refresca las lecturas, los habilitados y los sincronizadores (barato; corre en cada tick).</summary>
    public void RefrescarLecturas()
    {
        if (!Visible) return;
        foreach (var (etiqueta, leer) in _lecturas)
        {
            if (GodotObject.IsInstanceValid(etiqueta))
            {
                etiqueta.Text = leer();
            }
        }
        ActualizarHabilitados();
        foreach (var sincronizar in _sincronizadores)
        {
            sincronizar();
        }
    }

    private void ActualizarHabilitados()
    {
        foreach (var (condicion, aplicar) in _habilitables)
        {
            aplicar(condicion());
        }
    }

    // =====================================================================
    //  Armado para COMPONENTE
    // =====================================================================

    private void ArmarParaComponente(ComponenteVisual visual)
    {
        var logica = visual.Logica;

        Titulo(logica.Name);
        SubTitulo(visual.Def.Descripcion);
        Separador();

        // ---- Lecturas universales ----
        Lectura(() => $"Voltaje en bornes:  {logica.VoltajeEnBornes:F2} V");
        // Sí, es a propósito: la corriente es LA MISMA en las dos patas.
        // Es EL error conceptual más común, así que lo recordamos siempre.
        Lectura(() => $"Corriente:  {logica.Current * 1000:F1} mA  (idéntica en ambos terminales)");
        Lectura(() => $"Potencia:  {logica.Power * 1000:F1} mW");
        Lectura(() => SimuladorTermico.Habilitada
        ? $"Temperatura:  {logica.Temperatura:F1} °C"
        : "Temperatura:  —  (simulación térmica desactivada)");
        Lectura(() => $"Recibe:  {NombreTipo(logica.TipoCorrienteRecibida)}");

        // ---- Lecturas propias del tipo (salen del catálogo) ----
        foreach (var lectura in visual.Def.Lecturas)
        {
            var capturada = lectura; // captura para el closure
            Lectura(() => $"{capturada.Etiqueta}:  {capturada.Valor(logica)}");
        }
        if (logica is FuenteBase fuenteViva)
        {
            Lectura(() => $"Entregando:  {fuenteViva.CorrienteEntregada * 1000:F1} mA");
            // La resistencia interna PARTICIPA del solver (Fix 4): lo que se
            // muestra acá no es una estimación paralela, es la caída que el
            // circuito ya sufrió.
            Lectura(() =>
            {
                if (fuenteViva.ResistenciaInternaNominal <= 0 || fuenteViva.CaidaInternaReal < 0.005)
                    return "Bajo carga:  —  (sin caída apreciable)";
                return $"Bajo carga:  {fuenteViva.VoltajeEnBornes:F2} V de {Math.Abs(fuenteViva.Voltage):F2} V nominales " +
                       $"(caída interna de {fuenteViva.CaidaInternaReal:F2} V en {fuenteViva.ResistenciaInternaNominal:F2} Ω)";
            });
        }

        // ---- Advertencias ----
        var advertencia = LecturaColor(ColorAdvertencia, () =>
        {
            if (logica.IsBurnt) return $"QUEMADO ({logica.MotivoQuemado})";
            if (logica is FuenteBase f && f.EnCortocircuito) return "¡EN CORTOCIRCUITO!";
            if (logica is Disyuntor dis && dis.Disparado)
                return $"DISPARADO por {dis.MotivoDisparo} — despejá la falla y rearmá para volver a encender.";
            if (logica.EsIncompatible) return $"Corriente incompatible (recibe {NombreTipo(logica.TipoCorrienteRecibida)})";
            if (logica is ArduinoUno uno)
            {
                if (uno.Regulador3V3Quemado && uno.PinesDanadosTotal > 0)
                    return $"Regulador de 3.3 V quemado y {uno.PinesDanadosTotal} pin(es) dañado(s) por sobrecorriente.";
                if (uno.Regulador3V3Quemado)
                    return $"Regulador de 3.3 V quemado ({uno.MotivoRegulador3V3}): el pin 3V3 dejó de entregar voltaje.";
                if (uno.PinesDanadosTotal > 0)
                    return $"{uno.PinesDanadosTotal} pin(es) de E/S dañado(s) por sobrecorriente (quedaron abiertos).";
                if (uno.ResetSostenido)
                    return "CPU en reset por hardware (el pin RESET está conectado a GND).";
                if (uno.CpuDetenida)
                    return $"CPU detenida ({uno.MotivoDetencionCpu}).";
            }
            if (logica.BajoMinimoOperacion)
                return $"BAJO MÍNIMO para funcionar: {logica.MotivoBajoMinimo}";
            return "";
        });
        advertencia.AddThemeFontSizeOverride("font_size", 14);
        if (logica.IsBurnt) BotonReparar(() => visual.Reparar(_sim.TemperaturaAmbiente));
        if (logica is Disyuntor disyuntor && !logica.IsBurnt)
        {
            BotonDisyuntor(visual, disyuntor);
        }
        if (logica is ArduinoUno arduinoParcial)
        {
            var repararParcial = new Button
            {
                Text = "Reparar pines / regulador 3.3 V",
                Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoVerde),
                TooltipText = "Reemplaza los pines de E/S dañados y el regulador de 3.3 V sin borrar el programa cargado.",
                Visible = arduinoParcial.TieneDanosParciales,
            };
            TemaUI.AplicarEstiloBoton(repararParcial);
            repararParcial.AddThemeColorOverride("font_color", TemaUI.AcentoVerde);
            repararParcial.Pressed += () =>
            {
                arduinoParcial.RepararDanosParciales();
                Mostrar(_objeto);
            };
            _contenido.AddChild(repararParcial);
            _sincronizadores.Add(() =>
            {
                if (GodotObject.IsInstanceValid(repararParcial))
                    repararParcial.Visible = arduinoParcial.TieneDanosParciales;
            });
        }
        // [P6-3] Una pila gastada NO está rota: está gastada. Se le ponen
        // pilas nuevas, no se "arregla".
        if (logica is Battery pila && !double.IsPositiveInfinity(pila.CargaNominal) && pila.FraccionCarga < 0.999)
        {
            BotonRecargar(pila);
        }

        Separador();

        // ---- Configuración eléctrica simplificada (para todos excepto Arduino UNO) ----
        bool esArduino = logica is ArduinoUno;
        if (!esArduino)
        {
            ArmarConfiguracionElectrica(logica, () => visual.ActualizarDesdeLogica());
            Separador();
        }

        // ---- Estadísticas editables, directo del catálogo ----
        var statsAMostrar = esArduino
            ? visual.Def.Stats
            : visual.Def.Stats.FindAll(s => s.Grupo != GrupoStat.ConfiguracionElectrica);

        if (statsAMostrar.Count > 0)
        {
            SubTitulo(esArduino
                ? "PARÁMETROS (editables en vivo · ∞ = sin límite)"
                : "PARÁMETROS DEL COMPONENTE (editables en vivo · ∞ = sin límite)");
            foreach (var stat in statsAMostrar)
            {
                ArmarStat(stat, logica, () => visual.ActualizarDesdeLogica());
            }
        }

        // ---- Color de luz y material (LED / Lámpara) ----
        if (logica is IComponenteLuminoso luminoso)
        {
            Separador();
            ArmarSeccionColorLuz(visual, luminoso);
        }

        // ---- Sección Arduino (programar, monitor serie, pausa) ----
        if (logica is ArduinoUno placa)
        {
            Separador();
            ArmarSeccionArduino(placa);
        }

        // ---- Posición y rotación libres ----
        Separador();
        SubTitulo("POSICIÓN (RePág/AvPág sube y baja · Inicio apoya · rueda durante el arrastre)");
        ArmarPosicion(visual);
        SubTitulo("ROTACIÓN (R rota · Shift+R fino · T vuelca)");
        ArmarRotacion(visual);

        Separador();
        Botones();
    }

    /// <summary>
    /// Tarjeta simplificada para configurar en un solo lugar los 6 límites
    /// eléctricos del componente: mínimos para funcionar (con watts mínimos
    /// calculados automáticamente como V_min × A_min) y máximos antes de quemarse.
    /// </summary>
    private void ArmarConfiguracionElectrica(Component logica, Action alCambiar)
    {
        var estiloTarjeta = new StyleBoxFlat
        {
            BgColor = TemaUI.FondoSubPanel,
            BorderColor = TemaUI.BordeSutil,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        estiloTarjeta.SetCornerRadiusAll(8);

        var tarjeta = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        tarjeta.AddThemeStyleboxOverride("panel", estiloTarjeta);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 6);
        tarjeta.AddChild(vbox);

        // Encabezado de tarjeta
        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var titulo = new Label
        {
            Text = "CONFIGURACIÓN ELÉCTRICA",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titulo.AddThemeFontSizeOverride("font_size", 12);
        titulo.AddThemeColorOverride("font_color", TemaUI.TextoPrincipal);
        header.AddChild(titulo);

        var badge = new Label { Text = "Mín. / Máx." };
        badge.AddThemeFontSizeOverride("font_size", 10);
        badge.AddThemeColorOverride("font_color", TemaUI.TextoSecundario);
        header.AddChild(badge);
        vbox.AddChild(header);

        var ayuda = new Label
        {
            Text = "Mínimos en 0 = sin restricción · Máximos con ∞ = no se quema por ese límite.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        ayuda.AddThemeFontSizeOverride("font_size", 10);
        ayuda.AddThemeColorOverride("font_color", TemaUI.TextoSecundario);
        vbox.AddChild(ayuda);

        // ---- Bloque 1: Para funcionar (mínimos) ----
        var subMin = new Label { Text = "PARA FUNCIONAR (MÍNIMOS)" };
        subMin.AddThemeFontSizeOverride("font_size", 11);
        subMin.AddThemeColorOverride("font_color", TemaUI.AcentoCian);
        vbox.AddChild(subMin);

        SpinBox spinVMin = null!;
        SpinBox spinIMin = null!;
        Label valorWattsMin = null!;
        Label formulaWattsMin = null!;

        double LeerPreview(SpinBox sb, double respaldo)
        {
            var le = sb.GetLineEdit();
            if (le.HasFocus() && double.TryParse(le.Text.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed >= 0)
            {
                return parsed;
            }
            return Math.Max(0, sb.Value);
        }

        void ActualizarWattsMin(bool desdePreview = false)
        {
            double v = desdePreview ? LeerPreview(spinVMin, logica.MinVoltage) : Math.Max(0, logica.MinVoltage);
            double a = desdePreview ? LeerPreview(spinIMin, logica.MinCurrent) : Math.Max(0, logica.MinCurrent);
            double watts = v * a;

            valorWattsMin.Text = watts <= 1e-9
                ? "0 W"
                : watts >= 10.0
                    ? $"{watts:F2} W"
                    : $"{watts:F3} W";
            valorWattsMin.AddThemeColorOverride("font_color", watts > 1e-9 ? TemaUI.AcentoCian : TemaUI.TextoSecundario);
            formulaWattsMin.Text = watts <= 1e-9
                ? "Auto: V mín. × A mín."
                : watts < 1.0
                    ? $"Auto (= {watts * 1000:F1} mW)"
                    : "Auto: V mín. × A mín.";
        }

        spinVMin = AgregarCampoElectrico(vbox,
            "Voltaje mínimo para funcionar", "V",
            min: 0, maxSugerido: 1000, paso: 0.01, pasoFlecha: 0.25,
            fallbackSiInfinito: 0, conInfinito: false,
            tooltip: "Voltaje mínimo necesario para operar (0 V = sin mínimo).",
            leer: () => logica.MinVoltage,
            escribir: v => { logica.MinVoltage = Math.Max(0, v); alCambiar(); },
            onUiCambio: () => ActualizarWattsMin(desdePreview: true));

        spinIMin = AgregarCampoElectrico(vbox,
            "Amperaje mínimo para funcionar", "A",
            min: 0, maxSugerido: 100, paso: 0.001, pasoFlecha: 0.01,
            fallbackSiInfinito: 0, conInfinito: false,
            tooltip: "Corriente mínima en Amperios necesaria para operar (ej. 0.02 A = 20 mA; 0 A = sin mínimo).",
            leer: () => logica.MinCurrent,
            escribir: v => { logica.MinCurrent = Math.Max(0, v); alCambiar(); },
            onUiCambio: () => ActualizarWattsMin(desdePreview: true));

        // Fila auto-calculada: Watts mínimos para funcionar
        var filaWattsMin = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        filaWattsMin.AddThemeConstantOverride("separation", 6);
        var infoWatts = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        infoWatts.AddThemeConstantOverride("separation", 0);
        var lblWatts = new Label
        {
            Text = "Watts mínimos para funcionar",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = "Calculado automáticamente multiplicando Voltaje mínimo × Amperaje mínimo.",
        };
        lblWatts.AddThemeFontSizeOverride("font_size", 12);
        infoWatts.AddChild(lblWatts);

        formulaWattsMin = new Label { Text = "Auto: V mín. × A mín." };
        formulaWattsMin.AddThemeFontSizeOverride("font_size", 10);
        formulaWattsMin.AddThemeColorOverride("font_color", TemaUI.AcentoCian);
        infoWatts.AddChild(formulaWattsMin);
        filaWattsMin.AddChild(infoWatts);

        var pillWatts = new PanelContainer
        {
            CustomMinimumSize = new Vector2(124, 0),
            TooltipText = "Calculado automáticamente: Voltaje mínimo × Amperaje mínimo.",
        };
        pillWatts.AddThemeStyleboxOverride("panel", TemaUI.EstiloCapsula(margenH: 8, margenV: 4, radio: 5));
        valorWattsMin = new Label
        {
            Text = "0 W",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        valorWattsMin.AddThemeFontSizeOverride("font_size", 13);
        pillWatts.AddChild(valorWattsMin);
        filaWattsMin.AddChild(pillWatts);
        vbox.AddChild(filaWattsMin);

        ActualizarWattsMin();
        _sincronizadores.Add(() =>
        {
            if (GodotObject.IsInstanceValid(valorWattsMin)
                && !spinVMin.GetLineEdit().HasFocus()
                && !spinIMin.GetLineEdit().HasFocus())
            {
                ActualizarWattsMin(desdePreview: false);
            }
        });

        vbox.AddChild(new HSeparator());

        // ---- Bloque 2: Antes de quemarse (máximos) ----
        var subMax = new Label { Text = "ANTES DE QUEMARSE (MÁXIMOS)" };
        subMax.AddThemeFontSizeOverride("font_size", 11);
        subMax.AddThemeColorOverride("font_color", TemaUI.AcentoAlerta);
        vbox.AddChild(subMax);

        AgregarCampoElectrico(vbox,
            "Voltaje máximo antes de quemarse", "V",
            min: 0.01, maxSugerido: 1000, paso: 0.01, pasoFlecha: 0.5,
            fallbackSiInfinito: 30, conInfinito: true,
            tooltip: "Voltaje máximo soportado entre bornes antes de quemarse.",
            leer: () => logica.MaxVoltage,
            escribir: v => { logica.MaxVoltage = v; alCambiar(); });

        AgregarCampoElectrico(vbox,
            "Amperaje máximo antes de quemarse", "A",
            min: 0.001, maxSugerido: 100, paso: 0.001, pasoFlecha: 0.05,
            fallbackSiInfinito: 1.0, conInfinito: true,
            tooltip: "Corriente máxima en Amperios antes de quemarse (o fundirse si es fusible).",
            leer: () => logica.MaxCurrent,
            escribir: v => { logica.MaxCurrent = v; alCambiar(); });

        AgregarCampoElectrico(vbox,
            "Watts máximos para quemarse", "W",
            min: 0.001, maxSugerido: 2000, paso: 0.01, pasoFlecha: 0.25,
            fallbackSiInfinito: 10.0, conInfinito: true,
            tooltip: "Potencia eléctrica máxima en Watts tolerada antes de quemarse.",
            leer: () => logica.MaxPower,
            escribir: v => { logica.MaxPower = v; alCambiar(); });

        _contenido.AddChild(tarjeta);
    }

    private SpinBox AgregarCampoElectrico(
        VBoxContainer destino,
        string etiquetaTexto,
        string unidad,
        double min,
        double maxSugerido,
        double paso,
        double pasoFlecha,
        double fallbackSiInfinito,
        bool conInfinito,
        string tooltip,
        Func<double> leer,
        Action<double> escribir,
        Action? onUiCambio = null)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fila.AddThemeConstantOverride("separation", 6);

        var etiqueta = new Label
        {
            Text = etiquetaTexto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = tooltip,
        };
        etiqueta.AddThemeFontSizeOverride("font_size", 12);
        fila.AddChild(etiqueta);

        double leido = leer();
        bool esInfinito = conInfinito && double.IsInfinity(leido);
        double ultimoFinito = esInfinito ? fallbackSiInfinito : Math.Max(leido, min);

        var campo = new SpinBox
        {
            MinValue = min,
            MaxValue = Math.Max(maxSugerido, esInfinito ? maxSugerido : Math.Max(leido, min)),
            Step = paso,
            CustomArrowStep = pasoFlecha,
            Value = ultimoFinito,
            Suffix = " " + unidad,
            AllowGreater = true,
            Editable = !esInfinito,
            CustomMinimumSize = new Vector2(108, 0),
            TooltipText = tooltip,
        };

        campo.ValueChanged += v =>
        {
            ultimoFinito = v;
            escribir(v);
            onUiCambio?.Invoke();
        };
        if (onUiCambio != null)
        {
            campo.GetLineEdit().TextChanged += _ => onUiCambio();
        }
        fila.AddChild(campo);

        if (conInfinito)
        {
            var check = new CheckBox
            {
                Text = "∞",
                ButtonPressed = esInfinito,
                TooltipText = "Activado: sin límite (no se quema por este parámetro).",
            };
            check.Toggled += activo =>
            {
                if (activo)
                {
                    ultimoFinito = campo.Value;
                    escribir(double.PositiveInfinity);
                    campo.Editable = false;
                    campo.Modulate = new Color(1, 1, 1, 0.45f);
                }
                else
                {
                    escribir(ultimoFinito);
                    campo.SetValueNoSignal(ultimoFinito);
                    campo.Editable = true;
                    campo.Modulate = Colors.White;
                }
                onUiCambio?.Invoke();
            };
            if (esInfinito) campo.Modulate = new Color(1, 1, 1, 0.45f);
            fila.AddChild(check);
        }

        destino.AddChild(fila);
        return campo;
    }

    /// <summary>Elige el control según el tipo de la stat y lo arma con su descripción.</summary>
    private void ArmarStat(StatDef stat, Component logica, Action? alCambiar = null)
    {
        switch (stat.Tipo)
        {
            case TipoStat.Booleano:
                ArmarStatBooleano(stat, logica, alCambiar);
                break;
            case TipoStat.NumeroConInfinito:
                ArmarStatNumero(stat, logica, conInfinito: true, alCambiar);
                break;
            case TipoStat.Opciones:
                ArmarStatOpciones(stat, logica, alCambiar);
                break;
            default:
                ArmarStatNumero(stat, logica, conInfinito: false, alCambiar);
                break;
        }
        AgregarDescripcion(stat);
    }

    /// <summary>La descripción en criollo (y la fórmula, si hay) debajo del control.</summary>
    private void AgregarDescripcion(StatDef stat)
    {
        if (string.IsNullOrEmpty(stat.Descripcion) && stat.Formula == null) return;

        var texto = new Label
        {
            Text = stat.Descripcion + (stat.Formula != null ? $"      {stat.Formula}" : ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        texto.AddThemeFontSizeOverride("font_size", 10);
        texto.AddThemeColorOverride("font_color",
            stat.Formula != null && string.IsNullOrEmpty(stat.Descripcion) ? ColorFormula : ColorDescripcion);
        _contenido.AddChild(texto);
    }

    private void ArmarStatNumero(StatDef stat, Component logica, bool conInfinito, Action? alCambiar = null)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var etiqueta = new Label
        {
            Text = stat.Etiqueta,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        etiqueta.AddThemeFontSizeOverride("font_size", 13);
        fila.AddChild(etiqueta);

        double leido = stat.Leer(logica);
        bool esInfinito = double.IsInfinity(leido);

        // El "recuerdo" del último valor finito: al destildar ∞ se vuelve acá.
        // Si nunca hubo uno (variante sandbox recién colocada), arranca en el
        // máximo del rango — el valor finito más parecido a "sin límite".
        // (ultimoFinito está SIEMPRE en la unidad real; la Escala es sólo del
        // control que se dibuja.)
        double escala = stat.Escala;
        double techoVisual = esInfinito ? stat.Max : Math.Max(stat.Max, leido);
        double ultimoFinito = esInfinito ? stat.Max : Math.Max(leido, stat.Min);

        var campo = new SpinBox
        {
            MinValue = stat.Min * escala,
            MaxValue = techoVisual * escala,
            Step = stat.Paso * escala,
            Value = ultimoFinito * escala,
            Suffix = stat.UnidadVisual ?? stat.Unidad,
            AllowGreater = true, // el usuario manda: puede pasarse del rango sugerido
            Editable = !esInfinito,
            CustomMinimumSize = new Vector2(110, 0),
        };
        campo.ValueChanged += v =>
        {
            ultimoFinito = v / escala;
            stat.Escribir(logica, ultimoFinito);
            alCambiar?.Invoke();
        };
        fila.AddChild(campo);

        if (conInfinito)
        {
            var check = new CheckBox
            {
                Text = "∞",
                ButtonPressed = esInfinito,
                TooltipText = "Activado: deshabilita este límite de protección (sin restricción).\nEl último valor finito se conserva para cuando vuelva a desactivarse.",
            };
            check.Toggled += activo =>
            {
                if (activo)
                {
                    ultimoFinito = campo.Value / escala;
                    stat.Escribir(logica, double.PositiveInfinity);
                    campo.Editable = false;
                    campo.Modulate = new Color(1, 1, 1, 0.45f);
                }
                else
                {
                    stat.Escribir(logica, ultimoFinito);
                    campo.SetValueNoSignal(ultimoFinito * escala);
                    campo.Editable = true;
                    campo.Modulate = Colors.White;
                }
                alCambiar?.Invoke();
            };
            if (esInfinito) campo.Modulate = new Color(1, 1, 1, 0.45f);
            fila.AddChild(check);
        }

        if (stat.MostrarSlider && !conInfinito)
        {
            var contenedor = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            contenedor.AddThemeConstantOverride("separation", 2);
            contenedor.AddChild(fila);

            var deslizador = new HSlider
            {
                MinValue = stat.Min * escala,
                MaxValue = stat.Max * escala,
                Step = stat.Paso * escala,
                Value = ultimoFinito * escala,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            deslizador.ValueChanged += v =>
            {
                ultimoFinito = v / escala;
                campo.SetValueNoSignal(v);
                stat.Escribir(logica, ultimoFinito);
                alCambiar?.Invoke();
            };
            campo.ValueChanged += v => deslizador.SetValueNoSignal(v);

            contenedor.AddChild(deslizador);
            _contenido.AddChild(contenedor);
            RegistrarHabilitado(stat, logica, contenedor);
            return;
        }

        _contenido.AddChild(fila);
        RegistrarHabilitado(stat, logica, fila);
    }

    private void ArmarStatBooleano(StatDef stat, Component logica, Action? alCambiar = null)
    {
        var check = new CheckBox
        {
            Text = stat.Etiqueta,
            ButtonPressed = stat.Leer(logica) >= 0.5,
        };
        check.AddThemeFontSizeOverride("font_size", 13);
        check.Toggled += activo =>
        {
            stat.Escribir(logica, activo ? 1 : 0);
            ActualizarHabilitados(); // los stats que dependen de esta casilla se prenden/apagan al toque
            alCambiar?.Invoke();
        };
        _contenido.AddChild(check);
        RegistrarHabilitado(stat, logica, check);
    }

    private void ArmarStatOpciones(StatDef stat, Component logica, Action? alCambiar = null)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var etiqueta = new Label
        {
            Text = stat.Etiqueta,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        etiqueta.AddThemeFontSizeOverride("font_size", 13);
        fila.AddChild(etiqueta);

        var opciones = new OptionButton();
        foreach (var nombre in stat.NombresOpciones ?? Array.Empty<string>())
        {
            opciones.AddItem(nombre);
        }
        opciones.Selected = (int)Math.Clamp(stat.Leer(logica), 0, Math.Max(0, opciones.ItemCount - 1));
        opciones.ItemSelected += indice =>
        {
            stat.Escribir(logica, indice);
            ActualizarHabilitados();
            alCambiar?.Invoke();
        };
        fila.AddChild(opciones);

        _contenido.AddChild(fila);
        RegistrarHabilitado(stat, logica, fila);
    }

    /// <summary>Si la stat tiene condición de habilitado, registra el grisado del control.</summary>
    private void RegistrarHabilitado(StatDef stat, Component logica, Control control)
    {
        var condicion = stat.HabilitadoSi;
        if (condicion == null) return;
        _habilitables.Add((
            () => condicion(logica),
            habilitado =>
            {
                control.Modulate = habilitado ? Colors.White : new Color(1, 1, 1, 0.4f);
                // Además de grisar, bloquear la entrada de lo que se pueda.
                switch (control)
                {
                    case CheckBox c: c.Disabled = !habilitado; break;
                    case HBoxContainer fila:
                        foreach (var hijo in fila.GetChildren())
                        {
                            if (hijo is SpinBox s) s.Editable = habilitado && s.Modulate.A > 0.5f;
                            if (hijo is OptionButton o) o.Disabled = !habilitado;
                        }
                        break;
                }
            }
        ));
    }

    /// <summary>
    /// Tres campos X/Y/Z sincronizados con la posición real del componente en
    /// el mundo. La Y es la altura: subilo, dejalo flotando, o escribí un
    /// número negativo si QUERÉS hundirlo (el campo no juzga — las teclas y
    /// la rueda sí frenan en el piso, para no hundir cosas sin querer).
    /// </summary>
    private void ArmarPosicion(ComponenteVisual visual)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fila.AddThemeConstantOverride("separation", 4);

        var campos = new SpinBox[3];
        string[] ejes = { "X", "Y", "Z" };
        for (int i = 0; i < 3; i++)
        {
            fila.AddChild(new Label { Text = ejes[i] });
            var campo = new SpinBox
            {
                MinValue = i == 1 ? 0 : -7,   // la mesa mide 14x14, centrada
                MaxValue = 7,
                Step = 0.05,
                AllowGreater = true,
                AllowLesser = true,           // el usuario manda
                Suffix = "m",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            campos[i] = campo;
            fila.AddChild(campo);
        }

        void Aplicar(double _)
        {
            visual.Mover(new Vector3((float)campos[0].Value, (float)campos[1].Value, (float)campos[2].Value));
            GeometriaEditada?.Invoke();
        }
        foreach (var campo in campos)
        {
            campo.ValueChanged += Aplicar;
        }

        // Sincronizar desde el mundo (tras un arrastre o RePág/AvPág) sin
        // pisar al que está tipeando en el campo.
        _sincronizadores.Add(() =>
        {
            if (!GodotObject.IsInstanceValid(visual)) return;
            var pos = visual.GlobalPosition;
            for (int i = 0; i < 3; i++)
            {
                if (!campos[i].GetLineEdit().HasFocus())
                {
                    campos[i].SetValueNoSignal(Math.Round(pos[i], 2));
                }
            }
        });
        _sincronizadores[^1](); // primera sincronización inmediata

        _contenido.AddChild(fila);
    }

    /// <summary>Tres campos X/Y/Z sincronizados con la rotación real del componente.</summary>
    private void ArmarRotacion(ComponenteVisual visual)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fila.AddThemeConstantOverride("separation", 4);

        var campos = new SpinBox[3];
        string[] ejes = { "X", "Y", "Z" };
        for (int i = 0; i < 3; i++)
        {
            fila.AddChild(new Label { Text = ejes[i] });
            var campo = new SpinBox
            {
                MinValue = -360,
                MaxValue = 360,
                Step = 5,
                AllowGreater = true,
                AllowLesser = true,
                Suffix = "°",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            campos[i] = campo;
            fila.AddChild(campo);
        }

        void Aplicar(double _)
        {
            visual.FijarRotacion(new Vector3((float)campos[0].Value, (float)campos[1].Value, (float)campos[2].Value));
            GeometriaEditada?.Invoke();
        }
        foreach (var campo in campos)
        {
            campo.ValueChanged += Aplicar;
        }

        // Sincronizar desde el mundo (tras R/Shift+R/T) sin pisar al que tipea.
        _sincronizadores.Add(() =>
        {
            if (!GodotObject.IsInstanceValid(visual)) return;
            var rot = visual.RotationDegrees;
            for (int i = 0; i < 3; i++)
            {
                if (!campos[i].GetLineEdit().HasFocus())
                {
                    campos[i].SetValueNoSignal(Math.Round(rot[i], 1));
                }
            }
        });
        _sincronizadores[^1](); // primera sincronización inmediata

        _contenido.AddChild(fila);
    }

    // =====================================================================
    //  Sección Arduino: programar la placa y hablarle por el puerto serie
    // =====================================================================

    private void ArmarSeccionArduino(ArduinoUno placa)
    {
        SubTitulo("ARDUINO UNO");

        NotaChica("PWM (analogWrite): el simulador modela el valor eficaz medio de la señal (V_medio = 5 V × ciclo de trabajo).\nPines SDA y SCL: conectados físicamente a A4 y A5 como en el UNO R3 (el periférico I2C/TWI por hardware no está emulado).");

        // -- Botones de programa --
        var filaBotones = new HBoxContainer();
        filaBotones.AddThemeConstantOverride("separation", 8);

        var cargar = new Button
        {
            Text = placa.ProgramaCargado ? "Cambiar programa (.hex)" : "Cargar programa (.hex)",
            TooltipText = "Selecciona el archivo .hex generado por el IDE de Arduino\n(Programa → Exportar binario compilado).",
            Disabled = placa.IsBurnt,
        };
        cargar.Pressed += () => DialogoCargarHex.Abrir(placa, this);
        filaBotones.AddChild(cargar);

        var reiniciar = new Button
        {
            Text = "Reset",
            TooltipText = "Reinicia el microcontrolador desde el inicio (equivale al botón RESET de la placa;\nconserva la EEPROM y los pines dañados).",
            Disabled = placa.IsBurnt,
        };
        reiniciar.Pressed += placa.Reiniciar;
        filaBotones.AddChild(reiniciar);

        _contenido.AddChild(filaBotones);

        // -- Pausa de la CPU (el circuito sigue; los pines quedan congelados) --
        var pausa = new CheckBox
        {
            Text = "Pausar la CPU (los pines quedan congelados)",
            ButtonPressed = placa.CpuEnPausa,
        };
        pausa.AddThemeFontSizeOverride("font_size", 12);
        pausa.Toggled += activo => placa.CpuEnPausa = activo;
        _contenido.AddChild(pausa);

        _sincronizadores.Add(() =>
        {
            if (GodotObject.IsInstanceValid(cargar))
            {
                cargar.Text = placa.ProgramaCargado ? "Cambiar programa (.hex)" : "Cargar programa (.hex)";
                cargar.Disabled = placa.IsBurnt;
            }
            if (GodotObject.IsInstanceValid(reiniciar)) reiniciar.Disabled = placa.IsBurnt;
            if (GodotObject.IsInstanceValid(pausa) && pausa.ButtonPressed != placa.CpuEnPausa)
                pausa.SetPressedNoSignal(placa.CpuEnPausa);
        });

        // -- Monitor serie --
        SubTitulo("MONITOR SERIE");
        var monitor = new RichTextLabel
        {
            ScrollFollowing = true,          // siempre pegado a la última línea
            SelectionEnabled = true,
            CustomMinimumSize = new Vector2(0, 140),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        monitor.AddThemeFontSizeOverride("normal_font_size", 12);
        var fondoMonitor = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f),
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        fondoMonitor.SetCornerRadiusAll(4);
        monitor.AddThemeStyleboxOverride("normal", fondoMonitor);
        _contenido.AddChild(monitor);

        // Refresco perezoso: solo cuando llegaron caracteres nuevos (para no
        // reasignar el texto entero — y romper la selección — en cada tick).
        long ultimoVisto = -1;
        _sincronizadores.Add(() =>
        {
            if (!GodotObject.IsInstanceValid(monitor)) return;
            if (placa.CaracteresSerieRecibidos == ultimoVisto) return;
            ultimoVisto = placa.CaracteresSerieRecibidos;
            monitor.Text = placa.SalidaSerie.Length == 0
                ? "(el programa todavía no envió datos por Serial)"
                : placa.SalidaSerie;
        });
        _sincronizadores[^1]();

        // -- Enviar por serie --
        var entrada = new LineEdit
        {
            PlaceholderText = "Escribe texto y presiona Enter para enviarlo por Serial…",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Editable = !placa.IsBurnt,
        };
        entrada.AddThemeFontSizeOverride("font_size", 12);
        entrada.TextSubmitted += texto =>
        {
            if (texto.Length == 0) return;
            placa.EnviarSerie(texto);   // llega al programa con \n al final, como el monitor real
            entrada.Clear();
        };
        _contenido.AddChild(entrada);
        _sincronizadores.Add(() =>
        {
            if (GodotObject.IsInstanceValid(entrada)) entrada.Editable = !placa.IsBurnt;
        });
    }

    // =====================================================================
    //  Armado para CABLE
    // =====================================================================

    private void ArmarParaCable(CableVisual cable)
    {
        if (cable.EsFisico)
        {
            ArmarParaCableFisico(cable);
            return;
        }

        var logica = cable.Logica;

        Titulo("Cable");
        SubTitulo(logica.EsConexionPerfecta()
            ? "Conexión ideal (sin caída de tensión)"
            : "Cable resistivo: produce caída de tensión y disipa calor");
        Separador();

        Lectura(() => $"Largo:  {logica.Longitud:F2} m");
        Lectura(() => $"Corriente:  {logica.Current * 1000:F1} mA");
        Lectura(() => $"Caída de voltaje:  {logica.VoltajeEnBornes:F3} V");
        Lectura(() => $"Potencia disipada:  {logica.Power * 1000:F1} mW");
        Lectura(() => SimuladorTermico.Habilitada
        ? $"Temperatura:  {logica.Temperatura:F1} °C"
        : "Temperatura:  —  (simulación térmica desactivada)");

        LecturaColor(ColorAdvertencia,
            () => logica.IsBurnt ? $"QUEMADO ({logica.MotivoQuemado})" : "");
        if (logica.IsBurnt) BotonReparar(() => cable.Reparar(_sim.TemperaturaAmbiente));

        Separador();
        SubTitulo("PARÁMETROS (editables en vivo · ∞ = sin límite)");

        // La resistencia manda: por debajo del umbral el cable es fusión
        // perfecta; por encima pasa a ser una arista real del solver.
        CampoSimple("Resistencia", "Ω", 0.001, 1000, 0.01,
            logica.Resistencia, v => logica.Resistencia = v,
            "Valores cercanos a cero representan un conductor ideal. Al aumentarla, el cable produce caída de tensión y disipa calor.");

        CampoSimple("Comba (curvatura)", "m", 0, 3, 0.05,
            cable.Comba < 0 ? 0 : cable.Comba, v => { cable.Comba = v; cable.Reconstruir(); },
            "Ajuste visual de la caída del cable entre bornes.");

        CampoSimple("Grosor", "m", 0.005, 0.08, 0.002,
            cable.Grosor, v => { cable.Grosor = v; cable.Reconstruir(); },
            null);

        SubTitulo("Límites de ruptura");
        CampoInfinito("Voltaje máx.", "V", 0.1, 1000, 1,
            () => logica.MaxVoltage, v => logica.MaxVoltage = v);
        CampoInfinito("Corriente máx.", "A", 0.001, 100, 0.1,
            () => logica.MaxCurrent, v => logica.MaxCurrent = v);
        CampoInfinito("Potencia máx.", "W", 0.01, 2000, 1,
            () => logica.MaxPower, v => logica.MaxPower = v);
        CampoInfinito("Temperatura máx.", "°C", 25, 1500, 5,
            () => logica.MaxTemperature, v => logica.MaxTemperature = v);

        Separador();
        Botones(incluirRotar: false);
    }

    /// <summary>
    /// El inspector del cable FÍSICO. Acá no hay límites editables: EL GROSOR
    /// MANDA — de él salen la resistencia del cobre (ρ·L/A), la ampacidad
    /// (cuántos amperes aguanta) y la masa térmica; el aislante muere a los
    /// 105 °C y el cable quemado deja de conducir. Lo editable es lo físico:
    /// grosor, color y dobleces.
    /// </summary>
    private void ArmarParaCableFisico(CableVisual cable)
    {
        var logica = cable.Logica;

        Titulo("Cable físico");
        SubTitulo(logica.EsConexionPerfecta()
            ? "Conductor de baja resistencia: conducción ideal dentro de su ampacidad"
            : "Conductor resistivo: presenta caída de tensión y calentamiento propio");
        Separador();

        Lectura(() => $"Longitud:  {logica.Longitud:F2} m");
        Lectura(() => $"Resistencia del cobre:  {logica.Resistencia * 1000:F1} mΩ");
        Lectura(() => $"Ampacidad (máx. admisible):  {logica.MaxCurrent:F1} A");
        Lectura(() => $"Corriente:  {logica.Current * 1000:F1} mA");
        Lectura(() => $"Caída de voltaje:  {logica.VoltajeEnBornes:F3} V");
        Lectura(() => SimuladorTermico.Habilitada
        ? $"Temperatura:  {logica.Temperatura:F1} °C  (límite del aislante: {logica.MaxTemperature:F0} °C)"
        : "Temperatura:  —  (simulación térmica desactivada)");

        LecturaColor(ColorAdvertencia,
            () => logica.IsBurnt ? $"QUEMADO ({logica.MotivoQuemado}) — circuito abierto" : "");
        if (logica.IsBurnt) BotonReparar(() => cable.Reparar(_sim.TemperaturaAmbiente));

        Separador();
        SubTitulo("PROPIEDADES DEL CONDUCTOR");

        CampoSimple("Diámetro / Grosor", "mm", 0.3, 6.0, 0.1,
            cable.GrosorMm, v => cable.CambiarGrosor(v),
            "Menor grosor implica mayor resistencia y menor ampacidad. Referencia: 0.5 mm (~4 A), 1.5 mm (~15 A), 2.5 mm (~27 A).");

        CampoColor("Color del cable", cable.ColorCable, c => cable.CambiarColor(c));

        Separador();
        SubTitulo("DOBLECES (rígidos: mantienen su posición)");
        NotaChica("Arrastre los puntos de control amarillos para dar forma al cable. Haga doble clic sobre el cable para agregar un doblez o clic derecho sobre un punto para eliminarlo. La altura se ajusta con la rueda del ratón o con RePág / AvPág.");

        var botonDoblez = new Button { Text = "+ Agregar doblez en el centro" };
        botonDoblez.Pressed += () =>
        {
            if (!GodotObject.IsInstanceValid(cable)) return;
            Vector3 medio = (cable.BorneA.GlobalPosition + cable.BorneB.GlobalPosition) * 0.5f;
            cable.InsertarPuntoEnSegmento(cable.PuntosControl.Count / 2, medio + Vector3.Up * 0.15f);
        };
        _contenido.AddChild(botonDoblez);

        Separador();
        Botones(incluirRotar: false);
    }

    private static readonly (string Nombre, ColorLuzRgb Rgb)[] ColoresLuzBasicos =
    {
        ("Rojo",     new ColorLuzRgb(0.95f, 0.16f, 0.14f)),
        ("Naranja",  new ColorLuzRgb(1.00f, 0.48f, 0.08f)),
        ("Amarillo", new ColorLuzRgb(1.00f, 0.88f, 0.14f)),
        ("Verde",    new ColorLuzRgb(0.16f, 0.88f, 0.28f)),
        ("Cian",     new ColorLuzRgb(0.14f, 0.88f, 0.94f)),
        ("Azul",     new ColorLuzRgb(0.18f, 0.46f, 1.00f)),
        ("Violeta",  new ColorLuzRgb(0.68f, 0.26f, 0.96f)),
        ("Blanco",   new ColorLuzRgb(0.96f, 0.97f, 1.00f)),
    };

    private static ImageTexture CrearIconoColor(Color color)
    {
        const int lado = 14;
        var img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgba8);
        var borde = new Color(0.08f, 0.10f, 0.14f, 0.95f);
        for (int y = 0; y < lado; y++)
        {
            for (int x = 0; x < lado; x++)
            {
                bool enBorde = x == 0 || y == 0 || x == lado - 1 || y == lado - 1;
                img.SetPixel(x, y, enBorde ? borde : new Color(color.R, color.G, color.B, 1f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Selector de color predeterminado, básicos y color libre para LED y Lámpara.</summary>
    private void ArmarSeccionColorLuz(ComponenteVisual visual, IComponenteLuminoso lum)
    {
        SubTitulo("COLOR DE LUZ Y MATERIAL");

        Color colorPredet = visual.Logica is LED
            ? new Color(1f, 0.35f, 0.25f)
            : new Color(1f, 0.85f, 0.55f);

        Color ColorActualParaPicker() => lum.ColorPersonalizado is { } c
            ? new Color(c.R, c.G, c.B)
            : colorPredet;

        var filaPersonalizado = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var etiquetaLibre = new Label
        {
            Text = "Color libre",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        etiquetaLibre.AddThemeFontSizeOverride("font_size", 13);
        filaPersonalizado.AddChild(etiquetaLibre);

        var selector = new ColorPickerButton
        {
            Color = ColorActualParaPicker(),
            EditAlpha = false,
            CustomMinimumSize = new Vector2(82, 26),
        };
        filaPersonalizado.AddChild(selector);

        var btnPredet = new Button
        {
            Text = "Predeterminado",
            Icon = CrearIconoColor(colorPredet),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        var grilla = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grilla.AddThemeConstantOverride("h_separation", 4);
        grilla.AddThemeConstantOverride("v_separation", 4);

        var botonesBasicos = new List<(Button Btn, ColorLuzRgb Rgb)>();

        void RefrescarEstilos()
        {
            var actual = lum.ColorPersonalizado;
            TemaUI.AplicarEstiloBoton(btnPredet, destacado: actual == null);
            foreach (var (btn, rgb) in botonesBasicos)
            {
                bool activo = actual is { } c
                    && Mathf.Abs(c.R - rgb.R) < 0.01f
                    && Mathf.Abs(c.G - rgb.G) < 0.01f
                    && Mathf.Abs(c.B - rgb.B) < 0.01f;
                TemaUI.AplicarEstiloBoton(btn, destacado: activo);
            }
        }

        void AplicarColor(ColorLuzRgb? nuevo, bool sincronizarPicker)
        {
            lum.ColorPersonalizado = nuevo;
            visual.ActualizarDesdeLogica();
            if (sincronizarPicker)
            {
                selector.Color = ColorActualParaPicker();
            }
            RefrescarEstilos();
        }

        btnPredet.Pressed += () => AplicarColor(null, sincronizarPicker: true);
        _contenido.AddChild(btnPredet);

        foreach (var preset in ColoresLuzBasicos)
        {
            var btn = new Button
            {
                Text = preset.Nombre,
                Icon = CrearIconoColor(new Color(preset.Rgb.R, preset.Rgb.G, preset.Rgb.B)),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };
            var rgbLocal = preset.Rgb;
            btn.Pressed += () => AplicarColor(rgbLocal, sincronizarPicker: true);
            grilla.AddChild(btn);
            botonesBasicos.Add((btn, rgbLocal));
        }
        _contenido.AddChild(grilla);

        selector.ColorChanged += c => AplicarColor(new ColorLuzRgb(c.R, c.G, c.B), sincronizarPicker: false);
        _contenido.AddChild(filaPersonalizado);

        RefrescarEstilos();
    }

    /// <summary>Selector de color, para el cable físico.</summary>
    private void CampoColor(string nombre, Color actual, Action<Color> alCambiar)
    {
        var fila = new HBoxContainer();
        fila.AddChild(new Label { Text = nombre, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var selector = new ColorPickerButton
        {
            Color = actual,
            EditAlpha = false,
            CustomMinimumSize = new Vector2(72, 26),
        };
        selector.ColorChanged += c => alCambiar(c);
        fila.AddChild(selector);
        _contenido.AddChild(fila);
    }

    /// <summary>Un renglón de ayuda, chiquito y gris, que se ajusta al ancho del panel.</summary>
    private void NotaChica(string texto)
    {
        var nota = new Label
        {
            Text = texto,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nota.AddThemeFontSizeOverride("font_size", 12);
        nota.AddThemeColorOverride("font_color", new Color(0.72f, 0.76f, 0.85f));
        _contenido.AddChild(nota);
    }

    // =====================================================================
    //  Piezas de armado
    // =====================================================================

    private void Titulo(string texto)
    {
        var etiqueta = new Label { Text = texto };
        etiqueta.AddThemeFontSizeOverride("font_size", 18);
        _contenido.AddChild(etiqueta);
    }

    private void SubTitulo(string texto)
    {
        var etiqueta = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        etiqueta.AddThemeFontSizeOverride("font_size", 12);
        etiqueta.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        _contenido.AddChild(etiqueta);
    }

    private void Separador() => _contenido.AddChild(new HSeparator());

    private Label Lectura(Func<string> leer) => LecturaColor(new Color(0.85f, 0.9f, 1f), leer);

    private Label LecturaColor(Color color, Func<string> leer)
    {
        var etiqueta = new Label { Text = leer(), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        etiqueta.AddThemeColorOverride("font_color", color);
        _contenido.AddChild(etiqueta);
        _lecturas.Add((etiqueta, leer));
        return etiqueta;
    }

    /// <summary>Campo numérico suelto (para el cable, que no tiene StatDef en el catálogo).</summary>
    private void CampoSimple(string nombre, string unidad, double min, double max, double paso,
        double valorActual, Action<double> escribir, string? descripcion)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var etiqueta = new Label
        {
            Text = nombre,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        etiqueta.AddThemeFontSizeOverride("font_size", 13);
        fila.AddChild(etiqueta);

        var campo = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = paso,
            Value = Math.Clamp(valorActual, min, max),
            Suffix = unidad,
            AllowGreater = true,
            CustomMinimumSize = new Vector2(110, 0),
        };
        campo.ValueChanged += v => escribir(v);
        fila.AddChild(campo);
        _contenido.AddChild(fila);

        if (descripcion != null)
        {
            var texto = new Label { Text = descripcion, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            texto.AddThemeFontSizeOverride("font_size", 10);
            texto.AddThemeColorOverride("font_color", ColorDescripcion);
            _contenido.AddChild(texto);
        }
    }

    /// <summary>Campo numérico + check ∞ suelto (los límites del cable).</summary>
    private void CampoInfinito(string nombre, string unidad, double min, double max, double paso,
        Func<double> leer, Action<double> escribir)
    {
        var fila = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var etiqueta = new Label
        {
            Text = nombre,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        etiqueta.AddThemeFontSizeOverride("font_size", 13);
        fila.AddChild(etiqueta);

        double leido = leer();
        bool esInfinito = double.IsInfinity(leido);
        double ultimoFinito = esInfinito ? max : Math.Clamp(leido, min, max);

        var campo = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = paso,
            Value = ultimoFinito,
            Suffix = unidad,
            AllowGreater = true,
            Editable = !esInfinito,
            CustomMinimumSize = new Vector2(110, 0),
        };
        if (esInfinito) campo.Modulate = new Color(1, 1, 1, 0.45f);
        campo.ValueChanged += v =>
        {
            ultimoFinito = v;
            escribir(v);
        };
        fila.AddChild(campo);

        var check = new CheckBox
        {
            Text = "∞",
            ButtonPressed = esInfinito,
            TooltipText = "Activado: deshabilita este límite de protección (sin restricción).",
        };
        check.Toggled += activo =>
        {
            if (activo)
            {
                ultimoFinito = campo.Value;
                escribir(double.PositiveInfinity);
                campo.Editable = false;
                campo.Modulate = new Color(1, 1, 1, 0.45f);
            }
            else
            {
                escribir(ultimoFinito);
                campo.SetValueNoSignal(ultimoFinito);
                campo.Editable = true;
                campo.Modulate = Colors.White;
            }
        };
        fila.AddChild(check);

        _contenido.AddChild(fila);
    }

    private static string NombreTipo(TipoCorriente tipo) => tipo switch
    {
        TipoCorriente.Ninguna => "sin alimentación",
        TipoCorriente.DC => "continua (DC)",
        TipoCorriente.AC => "alterna (AC)",
        TipoCorriente.Mixta => "mixta (DC + AC)",
        _ => tipo.ToString(),
    };

    private void Botones(bool incluirRotar = true)
    {
        var fila = new HBoxContainer();
        fila.AddThemeConstantOverride("separation", 8);

        if (incluirRotar)
        {
            var rotar = new Button
            {
                Text = "Rotar 45°",
                Icon = TemaUI.Icono(TemaUI.TipoIcono.Rotar, TemaUI.AcentoCian),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            TemaUI.AplicarEstiloBoton(rotar);
            rotar.Pressed += () => RotarPedido?.Invoke();
            fila.AddChild(rotar);
        }

        var eliminar = new Button
        {
            Text = "Eliminar",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Eliminar, new Color(0.96f, 0.45f, 0.45f)),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        TemaUI.AplicarEstiloBoton(eliminar, peligro: true);
        eliminar.Pressed += () => EliminarPedido?.Invoke();
        fila.AddChild(eliminar);

        _contenido.AddChild(fila);
    }

    /// <summary>Botón de "ponerle pilas nuevas". Va al lado del de reparar, pero es otra cosa: la pila gastada no está rota.</summary>
    private void BotonRecargar(Battery pila)
    {
        var boton = new Button
        {
            Text = $"Reemplazar batería (carga: {pila.FraccionCarga:P0})",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoDorado),
            TooltipText = "Restaura la carga nominal y la resistencia interna inicial.\nUna batería descargada no está dañada: ha agotado su energía.",
        };
        TemaUI.AplicarEstiloBoton(boton);
        boton.AddThemeColorOverride("font_color", TemaUI.AcentoDorado);
        boton.Pressed += () =>
        {
            pila.Recargar();
            Mostrar(_objeto);
        };
        _contenido.AddChild(boton);
    }

    private void BotonDisyuntor(ComponenteVisual visual, Disyuntor disyuntor)
    {
        var boton = new Button();
        TemaUI.AplicarEstiloBoton(boton);
        void SincronizarBoton()
        {
            if (!GodotObject.IsInstanceValid(boton)) return;
            if (disyuntor.IsBurnt)
            {
                boton.Visible = false;
                return;
            }
            boton.Visible = true;
            if (disyuntor.Disparado)
            {
                boton.Text = "Rearmar disyuntor (volver a encender)";
                boton.Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoVerde);
                boton.AddThemeColorOverride("font_color", TemaUI.AcentoVerde);
                boton.TooltipText = "Cierra nuevamente los contactos tras un disparo automático.\nSi la falla en el circuito continúa, volverá a disparar.";
            }
            else if (disyuntor.CerradoManual)
            {
                boton.Text = "Desconectar disyuntor manualmente";
                boton.Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoDorado);
                boton.AddThemeColorOverride("font_color", TemaUI.AcentoDorado);
                boton.TooltipText = "Abre manualmente el circuito protegido (equivalente a bajar la llave).";
            }
            else
            {
                boton.Text = "Conectar disyuntor (encender)";
                boton.Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoVerde);
                boton.AddThemeColorOverride("font_color", TemaUI.AcentoVerde);
                boton.TooltipText = "Cierra manualmente el disyuntor para energizar el circuito.";
            }
        }

        SincronizarBoton();
        boton.Pressed += () =>
        {
            disyuntor.AlternarManual();
            visual.ActualizarDesdeLogica();
            SincronizarBoton();
            RefrescarLecturas();
        };
        _contenido.AddChild(boton);
        _sincronizadores.Add(SincronizarBoton);
    }

    private void BotonReparar(Action reparar)
    {
        var boton = new Button
        {
            Text = "Reparar componente",
            Icon = TemaUI.Icono(TemaUI.TipoIcono.Reparar, TemaUI.AcentoVerde),
            TooltipText = "Restaura el componente a estado operativo.\nNota: si persiste la falla en el circuito (sobretensión, sobrecorriente o temperatura), volverá a dañarse.",
        };
        TemaUI.AplicarEstiloBoton(boton);
        boton.AddThemeColorOverride("font_color", TemaUI.AcentoVerde);
        boton.Pressed += () =>
        {
            reparar();
            Mostrar(_objeto); // reconstruye el panel para el mismo objeto
        };
        _contenido.AddChild(boton);
    }
}
