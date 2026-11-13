using Godot;
using ElectricSim.Core;
using ElectricSim.Core.Components;

namespace ElectricSim.Game;

// =====================================================================
//  EL CATÁLOGO, VERSIÓN JERÁRQUICA
//
//  Antes esto era una lista plana de "componentes". Ahora tiene tres
//  niveles, que son los que la paleta muestra:
//
//    CATEGORÍA  ("Fuentes", "Iluminación", ...)
//      └─ TIPO  ("LED", "Batería 9V", ...)  → modelo 3D, bornes, stats
//           └─ VARIANTE ("realista", "sin límites ∞") → valores de fábrica
//
//  El TIPO define lo que el componente ES (qué modelo se ve, dónde van
//  los bornes, qué estadísticas existen y qué significan). La VARIANTE
//  define cómo viene DE FÁBRICA (un LED común y un LED indestructible
//  son el mismo tipo con otra fábrica). Así, agregar una variante nueva
//  es UNA entrada de tres líneas, sin duplicar modelos ni anclas.
// =====================================================================

/// <summary>Qué clase de control muestra el inspector para una estadística.</summary>
public enum TipoStat
{
    /// <summary>Campo numérico común.</summary>
    Numero,
    /// <summary>Campo numérico + check "∞": tildado, el valor es infinito y el número se ignora.</summary>
    NumeroConInfinito,
    /// <summary>Casilla de verdadero/falso (ej: modo corriente objetivo).</summary>
    Booleano,
    /// <summary>Lista desplegable de opciones (el valor es el índice). Preparado para los colores de LED.</summary>
    Opciones,
}

/// <summary>Agrupación visual de una estadística dentro del panel inspector.</summary>
public enum GrupoStat
{
    General,
    /// <summary>Mínimos para funcionar y máximos antes de quemarse (tarjeta simplificada).</summary>
    ConfiguracionElectrica,
}

/// <summary>
/// Una estadística editable de un componente: cómo se llama, en qué unidad
/// está, entre qué valores se puede mover, cómo leerla/escribirla sobre la
/// lógica real, y — nuevo — QUÉ ES en criollo, para que el inspector la
/// explique en vez de tirar un número pelado. El inspector arma sus
/// controles recorriendo esta lista: agregar una estadística sigue siendo
/// agregar UNA entrada acá, sin tocar la interfaz.
/// </summary>
public class StatDef
{
    public string Etiqueta;
    public string Unidad;
    public double Min;
    public double Max;
    public double Paso;

    /// <summary>
    /// Factor SOLO VISUAL: el inspector muestra (valor real × Escala) con la
    /// UnidadVisual. El valor real, el que se guarda en los proyectos y el que
    /// ven Min/Max/Paso, sigue en la unidad original. Existe porque los
    /// capacitores viven en Faradios (1e-4 F = 100µF) y un SpinBox de Godot
    /// redondea al múltiplo del paso: con paso 0.01F era IMPOSIBLE elegir un
    /// capacitor de 100µF, 10µF o 1µF.
    /// </summary>
    public double Escala = 1.0;

    /// <summary>Unidad que se muestra cuando Escala != 1 (ej: "µF"). Si es null se usa Unidad.</summary>
    public string? UnidadVisual;
    public Func<Component, double> Leer;
    public Action<Component, double> Escribir;

    /// <summary>Qué control se muestra (número, número con ∞, check, opciones).</summary>
    public TipoStat Tipo = TipoStat.Numero;

    /// <summary>Grupo visual al que pertenece esta estadística en el inspector.</summary>
    public GrupoStat Grupo = GrupoStat.General;

    /// <summary>La explicación en criollo: qué es esto y qué pasa si lo movés.</summary>
    public string Descripcion = "";

    /// <summary>Fórmula asociada, si la hay (se muestra chiquita al lado de la descripción).</summary>
    public string? Formula;

    /// <summary>Para Tipo=Opciones: los nombres de cada opción (el valor guardado es el índice).</summary>
    public string[]? NombresOpciones;

    /// <summary>Si es true, el inspector agrega un slider horizontal junto al SpinBox numérico.</summary>
    public bool MostrarSlider;

    /// <summary>
    /// Si está definido y devuelve false, el control se muestra GRISADO
    /// (ej: los Ohms manuales se apagan cuando el modo corriente objetivo
    /// está manejando la resistencia solo).
    /// </summary>
    public Func<Component, bool>? HabilitadoSi;

    public StatDef(string etiqueta, string unidad, double min, double max, double paso,
        Func<Component, double> leer, Action<Component, double> escribir)
    {
        Etiqueta = etiqueta;
        Unidad = unidad;
        Min = min;
        Max = max;
        Paso = paso;
        Leer = leer;
        Escribir = escribir;
    }
}

/// <summary>
/// Una lectura EN VIVO propia del tipo (además de las universales V/I/P/T°
/// que el inspector muestra siempre): el brillo del LED, la carga del
/// capacitor, si la resistencia alcanzó su corriente objetivo, etc.
/// Devuelve texto ya formateado; el inspector la refresca cada tick.
/// </summary>
public class LecturaDef
{
    public string Etiqueta;
    public Func<Component, string> Valor;

    public LecturaDef(string etiqueta, Func<Component, string> valor)
    {
        Etiqueta = etiqueta;
        Valor = valor;
    }
}

/// <summary>Las secciones del acordeón de la paleta.</summary>
public enum CategoriaComponente
{
    Fuentes,
    Iluminacion,
    Pasivos,
    Control,
    Proteccion,
    Microcontroladores,
}

public static class CategoriaExtensiones
{
    /// <summary>Nombre para mostrar en el encabezado del acordeón.</summary>
    public static string NombreLindo(this CategoriaComponente c) => c switch
    {
        CategoriaComponente.Fuentes => "Fuentes de energía",
        CategoriaComponente.Iluminacion => "Iluminación",
        CategoriaComponente.Pasivos => "Pasivos",
        CategoriaComponente.Control => "Control",
        CategoriaComponente.Proteccion => "Protección",
        CategoriaComponente.Microcontroladores => "Microcontroladores",
        _ => c.ToString(),
    };
}

/// <summary>
/// Una VARIANTE de un tipo: mismo modelo 3D y mismas estadísticas, distinta
/// configuración de fábrica. "LED" y "LED ∞" son dos variantes del tipo LED.
/// </summary>
public class VarianteComponente
{
    public string Id = "";
    public string Nombre = "";
    public string Descripcion = "";
    /// <summary>True para las variantes indestructibles: la tarjeta de la paleta muestra el distintivo ∞.</summary>
    public bool SinLimites;
    public Func<Component> CrearLogica = null!;
}

/// <summary>
/// Un TIPO de componente: lo que el componente ES. Modelo 3D (o primitiva),
/// anclas de bornes, categoría, estadísticas con sus explicaciones, lecturas
/// en vivo propias, y la lista de variantes de fábrica.
///
/// ANCLAS DE BORNES: son FRACCIONES de la caja ya ajustada del modelo
/// (X y Z entre −0.5 y 0.5 desde el centro, Y entre 0 = piso y 1 = techo).
/// Así no dependen de la escala real de cada .glb — el mismo número sirve
/// aunque el modelo se reexporte más grande o más chico.
/// </summary>
public class DefinicionTipo
{
    public string Id = "";
    public string Nombre = "";
    public CategoriaComponente Categoria;
    public string Descripcion = "";

    /// <summary>Ruta res:// del modelo .glb (null → se usa CrearPrimitiva).</summary>
    public string? RutaModelo;

    /// <summary>Segundo modelo para componentes con dos estados visuales (interruptor arriba/abajo).</summary>
    public string? RutaModeloAlterno;

    /// <summary>Constructor de un modelo con primitivas de Godot, para componentes sin .glb propio todavía.</summary>
    public Func<Node3D>? CrearPrimitiva;

    /// <summary>Rotación previa al ajuste (ej: la batería viene modelada con los bornes para abajo).</summary>
    public Basis RotacionBase = Basis.Identity;

    /// <summary>Altura final del componente en el mundo (autoescala por caja envolvente).</summary>
    public float AlturaObjetivo = 0.4f;

    public Vector3 AnclaBorneA;
    public Vector3 AnclaBorneB;

    /// <summary>
    /// Anclas de los bornes EXTRA de los componentes multi-borne (Arduino y
    /// sus 22 patas además del 5V/GND). La Clave tiene que coincidir con la
    /// Etiqueta que el componente lógico publica en IComponenteMultiBorne
    /// (por ej. "D13", "A0", "3V3"). Mismas fracciones de caja que A/B.
    /// </summary>
    public List<(string Clave, Vector3 Ancla)> AnclasBornesExtra = new();

    /// <summary>Tamaño relativo de las esferas de borne (1 = normal). El Arduino usa 0.55 para que sus 24 no se pisen.</summary>
    public float EscalaBornes = 1f;

    /// <summary>Etiqueta flotante sobre el borne A (null = sin cartel). El Arduino la usa: "5V".</summary>
    public string? EtiquetaBorneA;

    /// <summary>Etiqueta flotante sobre el borne B (null = sin cartel). El Arduino la usa: "GND".</summary>
    public string? EtiquetaBorneB;

    /// <summary>Ancla (fracción de caja) del LED "L" integrado de la placa, que espeja a D13. Null = no hay.</summary>
    public Vector3? AnclaLedInterno;

    public List<StatDef> Stats = new();
    public List<LecturaDef> Lecturas = new();
    public List<VarianteComponente> Variantes = new();

    /// <summary>
    /// EXTENSIÓN DEL GUARDADO. Las Stats ya se guardan y se restauran solas
    /// (son números con nombre); estos dos hooks son para el estado VIVO que
    /// no es una stat: si un interruptor quedó cerrado, qué .hex tiene una
    /// placa, la posición de una perilla...
    ///
    /// Se completan en la entrada del catálogo del componente, que es el
    /// único archivo que tocás al agregar algo nuevo:
    ///
    ///     interruptor.GuardarEstado = (c, e) => e.Poner("cerrado", ((Switch)c).IsClosed);
    ///     interruptor.CargarEstado  = (c, e) => ((Switch)c).IsClosed = e.Booleano("cerrado", true);
    ///
    /// Si un componente no los define, no pasa nada: se guarda igual con su
    /// tipo, posición, rotación y stats.
    /// </summary>
    public Action<Component, EstadoGuardado>? GuardarEstado;

    /// <summary>El inverso de GuardarEstado: restaura el estado vivo al abrir un proyecto. Ver arriba.</summary>
    public Action<Component, EstadoGuardado>? CargarEstado;
}

/// <summary>
/// La SELECCIÓN CONCRETA que viaja por el juego: un tipo + una variante.
/// Expone los mismos miembros que la vieja DefinicionComponente (Nombre,
/// CrearLogica, RutaModelo, anclas, Stats...) reenviando al tipo o a la
/// variante según corresponda — así ComponenteVisual, el controlador y el
/// inspector siguen funcionando sin enterarse del cambio de estructura.
/// </summary>
public class DefinicionComponente
{
    public DefinicionTipo Tipo { get; }
    public VarianteComponente Variante { get; }

    public DefinicionComponente(DefinicionTipo tipo, VarianteComponente variante)
    {
        Tipo = tipo;
        Variante = variante;
    }

    public string Id => $"{Tipo.Id}.{Variante.Id}";
    public string Nombre => Variante.Nombre;
    public string Descripcion => Variante.Descripcion;
    public Func<Component> CrearLogica => Variante.CrearLogica;

    public string? RutaModelo => Tipo.RutaModelo;
    public string? RutaModeloAlterno => Tipo.RutaModeloAlterno;
    public Func<Node3D>? CrearPrimitiva => Tipo.CrearPrimitiva;
    public Basis RotacionBase => Tipo.RotacionBase;
    public float AlturaObjetivo => Tipo.AlturaObjetivo;
    public Vector3 AnclaBorneA => Tipo.AnclaBorneA;
    public Vector3 AnclaBorneB => Tipo.AnclaBorneB;
    public List<(string Clave, Vector3 Ancla)> AnclasBornesExtra => Tipo.AnclasBornesExtra;
    public float EscalaBornes => Tipo.EscalaBornes;
    public string? EtiquetaBorneA => Tipo.EtiquetaBorneA;
    public string? EtiquetaBorneB => Tipo.EtiquetaBorneB;
    public Vector3? AnclaLedInterno => Tipo.AnclaLedInterno;
    public List<StatDef> Stats => Tipo.Stats;
    public List<LecturaDef> Lecturas => Tipo.Lecturas;
    public Action<Component, EstadoGuardado>? GuardarEstado => Tipo.GuardarEstado;
    public Action<Component, EstadoGuardado>? CargarEstado => Tipo.CargarEstado;
}

/// <summary>
/// El catálogo completo. Para AGREGAR UN TIPO NUEVO: (1) su clase en
/// Core/Components, (2) su DefinicionTipo acá con al menos una variante.
/// Para AGREGAR UNA VARIANTE de algo que ya existe: tres líneas en la
/// lista Variantes del tipo. La paleta, el fantasma, los bornes y el
/// inspector salen solos de estas definiciones.
/// </summary>
public static class CatalogoComponentes
{
    public static readonly List<DefinicionTipo> Tipos = Crear();

    /// <summary>Todas las combinaciones tipo×variante, aplanadas (compatibilidad + búsquedas).</summary>
    public static readonly List<DefinicionComponente> Todos =
        Tipos.SelectMany(t => t.Variantes.Select(v => new DefinicionComponente(t, v))).ToList();

    /// <summary>Busca por id "tipo.variante" (ej "led.sandbox"), o por id de tipo a secas (devuelve su primera variante).</summary>
    public static DefinicionComponente? PorId(string id) =>
        Todos.Find(d => d.Id == id) ?? Todos.Find(d => d.Tipo.Id == id);

    // ---------------------------------------------------------------
    //  Ayudantes de fábrica
    // ---------------------------------------------------------------

    /// <summary>Le pone infinito a los cuatro límites de ruptura: el componente no muere por nada eléctrico ni térmico.</summary>
    /// <summary>
    /// La tensión que guarda el capacitor, en VOLTS y como porcentaje de su
    /// máximo. Antes esta lectura decía sólo "Carga: 29%", y eso engaña: el
    /// porcentaje es la tensión sobre el máximo que aguanta (25V por defecto),
    /// no "qué tan lleno está". Un capacitor de 25V cargado del todo con una
    /// pila de 9V marca 36% y NUNCA va a marcar 100%: no está trabado, es que
    /// la pila no llega a 25V.
    /// </summary>
    private static string DescribirCargaCapacitor(Capacitor k)
    {
        double referencia = double.IsPositiveInfinity(k.MaxVoltage) ? k.VoltajeReferenciaCarga : k.MaxVoltage;
        return $"{Math.Abs(k.VoltajeAlmacenado):F2} V  ({k.ChargeLevel:P0} de {referencia:F0} V)";
    }

    /// <summary>Dice si se está cargando, descargando o si ya no circula corriente (y por qué eso no es un error).</summary>
    private static string EstadoCapacitor(Capacitor k)
    {
        if (k.IsBurnt) return "Dañado (fuera de servicio)";

        double corriente = Math.Abs(k.Current);
        if (corriente >= 1e-3)
        {
            // Si la corriente empuja el voltaje hacia arriba (en valor absoluto), se carga.
            bool cargando = k.Current * k.VoltajeAlmacenado >= 0;
            return cargando
                ? $"En carga ({corriente * 1000:F1} mA)"
                : $"En descarga ({corriente * 1000:F1} mA)";
        }

        return Math.Abs(k.VoltajeAlmacenado) < 0.05
            ? "Descargado"
            : "En equilibrio: ya no circula corriente continua porque alcanzó la tensión máxima disponible en su rama.";
    }

    private static T TodoInfinito<T>(T c) where T : Component
    {
        SinLimitesEn(c);
        // Un compuesto (transistor, potenciómetro) tambien ES sus
        // aristas internas: si la etapa de entrada conserva sus limites de
        // fabrica, la variante "infinita" muere igual y la promesa es mentira.
        if (c is IComponenteCompuesto compuesto)
        {
            foreach (var interna in compuesto.AristasInternas)
            {
                SinLimitesEn(interna);
            }
        }
        return c;
    }

    private static void ConfigurarGuardadoColorLuz(DefinicionTipo def)
    {
        def.GuardarEstado = (c, e) =>
        {
            if (c is IComponenteLuminoso lum && lum.ColorPersonalizado is { } rgb)
                e.PonerColor("colorLuz", new Color(rgb.R, rgb.G, rgb.B));
        };
        def.CargarEstado = (c, e) =>
        {
            if (c is IComponenteLuminoso lum && e.Tiene("colorLuz"))
            {
                var col = e.ColorDe("colorLuz", Colors.White);
                lum.ColorPersonalizado = new ColorLuzRgb(col.R, col.G, col.B);
            }
        };
    }

    /// <summary>Desactiva mínimos y deja los límites de ruptura en infinito.</summary>
    private static void SinLimitesEn(Component c)
    {
        c.MinVoltage = 0;
        c.MinCurrent = 0;
        c.MaxVoltage = double.PositiveInfinity;
        c.MaxCurrent = double.PositiveInfinity;
        c.MaxPower = double.PositiveInfinity;
        c.MaxTemperature = double.PositiveInfinity;
    }

    private static VarianteComponente Variante(string id, string nombre, string descripcion,
        Func<Component> crear, bool sinLimites = false) => new()
    {
        Id = id,
        Nombre = nombre,
        Descripcion = descripcion,
        CrearLogica = crear,
        SinLimites = sinLimites,
    };

    // ---------------------------------------------------------------
    //  Estadísticas compartidas
    // ---------------------------------------------------------------

    /// <summary>
    /// Los cuatro límites de ruptura que tiene TODO componente. Son de tipo
    /// NumeroConInfinito: el inspector les pone el check "∞" al lado — 
    /// tildado, el límite directamente no existe (y el número queda grisado
    /// recordando tu último valor). Se acabó el "999" mentiroso.
    /// </summary>
    private static IEnumerable<StatDef> StatsLimites() => new[]
    {
        new StatDef("Voltaje máx.", "V", 0.1, 1000, 0.5, c => c.MaxVoltage, (c, v) => c.MaxVoltage = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Tensión máxima admisible entre sus bornes antes de sufrir ruptura dieléctrica o eléctrica. Si se marca ∞, no se daña por sobretensión.",
        },
        new StatDef("Corriente máx.", "A", 0.001, 100, 0.05, c => c.MaxCurrent, (c, v) => c.MaxCurrent = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Corriente máxima continua que soporta el componente sin dañarse. Si se marca ∞, queda protegido frente a sobrecorrientes.",
        },
        new StatDef("Potencia máx.", "W", 0.01, 2000, 0.05, c => c.MaxPower, (c, v) => c.MaxPower = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Potencia eléctrica máxima que puede disipar de forma segura. Se calcula como el producto de tensión por corriente en el componente.",
            Formula = "P = V × I",
        },
        new StatDef("Temperatura máx.", "°C", 25, 1500, 5, c => c.MaxTemperature, (c, v) => c.MaxTemperature = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Temperatura límite de operación antes de sufrir daño térmico. Con ∞ activado no se quema por calor, aunque sigue transfiriendo temperatura a componentes cercanos.",
        },
    };

    // ---------------------------------------------------------------
    //  Los tipos
    // ---------------------------------------------------------------

    private static List<DefinicionTipo> Crear()
    {
        var lista = new List<DefinicionTipo>();

        // ================= FUENTES =================

        var bateria = new DefinicionTipo
        {
            Id = "bateria9v",
            Nombre = "Batería 9V",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Fuente electroquímica de corriente continua (DC) de 9 V nominales. Posee resistencia interna y capacidad finita en mAh.",
            RutaModelo = "res://models/bateria9V.glb",
            // El modelo viene con los bornes hacia abajo: media vuelta en X.
            RotacionBase = new Basis(Vector3.Right, Mathf.Pi),
            AlturaObjetivo = 0.55f,
            AnclaBorneA = new Vector3(0f, 0.99f, 0.23f),   // positivo (poste chico)
            AnclaBorneB = new Vector3(0f, 0.99f, -0.22f),  // negativo (poste ancho)
        };
        bateria.Stats.Add(new StatDef("Voltaje", "V", 0.5, 48, 0.5,
            c => ((Battery)c).Voltage, (c, v) => ((Battery)c).Voltage = v)
        {
            Descripcion = "Fuerza electromotriz (tensión en vacío) que impulsa la corriente eléctrica a través del circuito.",
            Formula = "I = V / R",
        });
        bateria.Stats.Add(new StatDef("Resist. interna", "Ω", 0.05, 50, 0.05,
            c => ((Battery)c).ResistenciaInternaNominal, (c, v) => ((Battery)c).ResistenciaInternaNominal = v)
        {
            Descripcion = "Oposición interna propia de la batería. Provoca una caída de tensión cuando entrega corriente (V_bornes = V_nominal − I · R_interna) y genera calor por efecto Joule.",
        });
        bateria.Stats.AddRange(StatsLimites());
        bateria.Stats.Add(new StatDef("Capacidad", "mAh", 10, 100_000, 10,
            c => ((Battery)c).CargaNominal, (c, v) => { var b = (Battery)c; b.CargaNominal = v; b.CargaRestante = Math.Min(b.CargaRestante, v); })
        { Descripcion = "Carga eléctrica almacenada (miliamperios-hora). Determina la autonomía según el consumo: por ejemplo, 220 mAh a 10 mA duran 22 horas." });
        bateria.Lecturas.Add(new LecturaDef("Carga restante",
            c => {
                var b = (Battery)c;
                if (double.IsPositiveInfinity(b.CargaNominal)) return "∞  (sin desgaste)";
                string autonomia = double.IsPositiveInfinity(b.AutonomiaHoras)
                    ? "sin consumo"
                    : b.AutonomiaHoras >= 1 ? $"~{b.AutonomiaHoras:F1} h al consumo actual" : $"~{b.AutonomiaHoras * 60:F0} min al consumo actual";
                return $"{b.FraccionCarga:P0}  ({b.CargaRestante:F0} de {b.CargaNominal:F0} mAh · {autonomia})";
            }));
        bateria.Lecturas.Add(new LecturaDef("Voltaje real",
            c => { var b = (Battery)c; return $"{b.Voltage:F2} V de {b.VoltajeNominal:F2} V nominales  (R int.: {b.ResistenciaInternaNominal:F2} Ω)"; }));
        bateria.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((Battery)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        bateria.Variantes.Add(Variante("real", "Batería 9V",
            "Batería alcalina estándar de 9 V (500 mAh, 1.8 Ω de resistencia interna). En cortocircuito limita su corriente a ~5 A y se calienta rápidamente.",
            () => new Battery("Batería 9V", 9)));
        bateria.Variantes.Add(Variante("sandbox", "Batería 9V ∞",
            "Versión ideal sin desgaste de carga ni ruptura por sobrecarga o temperatura.",
            () => TodoInfinito(new Battery("Batería 9V ∞", 9) { CargaNominal = double.PositiveInfinity }), sinLimites: true));
        lista.Add(bateria);

        var pilaBoton = new DefinicionTipo
        {
            Id = "pilaboton",
            Nombre = "Pila botón 3V",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Celda de litio compacta de 3 V (tipo CR2032). Debido a su alta resistencia interna (15 Ω), es ideal para cargas de muy bajo consumo como LEDs pequeños o memorias.",
            RutaModelo = "res://models/pilaboton.glb",
            // El modelo pilaboton.glb mide 2.0 × 0.1718 × 2.0 (relación diámetro/espesor = 11.64).
            // Con AlturaObjetivo = 0.02m queda de 2cm de espesor y ~23cm de diámetro en mesa
            // (en vez de 1.16m cuando tenía AlturaObjetivo = 0.1m).
            AlturaObjetivo = 0.02f,
            EscalaBornes = 0.6f,
            AnclaBorneA = new Vector3(0f, 0.95f, 0f),      // positivo: centro de la cara superior
            AnclaBorneB = new Vector3(0.45f, 0.25f, 0f),   // negativo: borde lateral metálico
        };
        pilaBoton.Stats.Add(new StatDef("Voltaje", "V", 0.5, 12, 0.5,
            c => ((Battery)c).Voltage, (c, v) => ((Battery)c).Voltage = v)
        {
            Descripcion = "Tensión nominal en vacío entre el terminal positivo superior y el negativo inferior.",
            Formula = "I = V / R",
        });
        pilaBoton.Stats.Add(new StatDef("Resist. interna", "Ω", 0.05, 50, 0.05,
            c => ((Battery)c).ResistenciaInternaNominal, (c, v) => ((Battery)c).ResistenciaInternaNominal = v)
        {
            Descripcion = "Resistencia serie interna. En las pilas botón es elevada (~15 Ω), lo que reduce la tensión en bornes si se exige demasiada corriente.",
        });
        pilaBoton.Stats.AddRange(StatsLimites());
        pilaBoton.Stats.Add(new StatDef("Capacidad", "mAh", 10, 100_000, 10,
            c => ((Battery)c).CargaNominal, (c, v) => { var b = (Battery)c; b.CargaNominal = v; b.CargaRestante = Math.Min(b.CargaRestante, v); })
        { Descripcion = "Carga eléctrica almacenada en miliamperios-hora (220 mAh típicos en una celda CR2032)." });
        pilaBoton.Lecturas.Add(new LecturaDef("Carga restante",
            c => {
                var b = (Battery)c;
                if (double.IsPositiveInfinity(b.CargaNominal)) return "∞  (sin desgaste)";
                string autonomia = double.IsPositiveInfinity(b.AutonomiaHoras)
                    ? "sin consumo"
                    : b.AutonomiaHoras >= 1 ? $"~{b.AutonomiaHoras:F1} h al consumo actual" : $"~{b.AutonomiaHoras * 60:F0} min al consumo actual";
                return $"{b.FraccionCarga:P0}  ({b.CargaRestante:F0} de {b.CargaNominal:F0} mAh · {autonomia})";
            }));
        pilaBoton.Lecturas.Add(new LecturaDef("Voltaje real",
            c => { var b = (Battery)c; return $"{b.Voltage:F2} V de {b.VoltajeNominal:F2} V nominales  (R int.: {b.ResistenciaInternaNominal:F2} Ω)"; }));
        pilaBoton.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((Battery)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        pilaBoton.Variantes.Add(Variante("real", "Pila botón 3V",
            "Celda CR2032 de 3 V y 220 mAh. Si la carga exige más de 50 mA, su resistencia interna provoca una caída importante de tensión.",
            () => new Battery("Pila botón", 3)
            {
                MaxCurrent = 2,            // perforación, no "se acabó"
                CorrientePico = 0.05,      // BLANDO: por encima se hunde
                CargaNominal = 220, CargaRestante = 220,   // CR2032
                ResistenciaInternaNominal = 15,
                CapacidadTermica = 3,
            }));
        pilaBoton.Variantes.Add(Variante("sandbox", "Pila botón ∞",
            "Versión ideal de 3 V con carga inagotable y sin límites de ruptura.",
            () => TodoInfinito(new Battery("Pila botón ∞", 3) { CargaNominal = double.PositiveInfinity,
                ResistenciaInternaNominal = 15,
                CapacidadTermica = 3,
            }), sinLimites: true));
        lista.Add(pilaBoton);

        var fuenteAc = new DefinicionTipo
        {
            Id = "fuenteac",
            Nombre = "Fuente AC",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Generador de corriente alterna senoidal definido por su tensión eficaz (RMS) y frecuencia. Sus bornes representan fase (marrón) y neutro (celeste).",
            CrearPrimitiva = Primitivas.FuenteAC,
            AlturaObjetivo = 0.42f,
            AnclaBorneA = new Vector3(-0.24f, 1.0f, 0f),
            AnclaBorneB = new Vector3(0.24f, 1.0f, 0f),
        };
        fuenteAc.Stats.Add(new StatDef("Voltaje RMS", "V", 1, 48, 1,
            c => ((FuenteAC)c).VoltajeRMS, (c, v) => ((FuenteAC)c).VoltajeRMS = v)
        {
            Descripcion = "Valor eficaz (RMS) de la tensión alterna: equivale a la tensión continua que produciría la misma potencia media sobre una carga resistiva (Vpico = Vrms · √2).",
        });
        fuenteAc.Stats.Add(new StatDef("Frecuencia", "Hz", 1, 400, 1,
            c => ((FuenteAC)c).Frecuencia, (c, v) => ((FuenteAC)c).Frecuencia = v)
        {
            Descripcion = "Cantidad de ciclos completos por segundo (Hertz). Determina la reactancia de capacitores (Xc = 1/(2π·f·C)) e inductores (XL = 2π·f·L).",
        });
        fuenteAc.Stats.AddRange(StatsLimites());
        fuenteAc.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((FuenteAC)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        fuenteAc.Variantes.Add(Variante("real", "Fuente AC",
            "Fuente de baja tensión alterna de 12 V RMS a 50 Hz (típica de fuentes de laboratorio y transformadores auxiliares).",
            () => new FuenteAC("Fuente AC", 12, 50)));
        fuenteAc.Variantes.Add(Variante("sandbox", "Fuente AC ∞",
            "Fuente de corriente alterna ideal sin límites de corriente, potencia ni temperatura.",
            () => TodoInfinito(new FuenteAC("Fuente AC ∞", 12, 50)), sinLimites: true));
        lista.Add(fuenteAc);

        // ================= PASIVOS =================

        var resistencia = new DefinicionTipo
        {
            Id = "resistencia",
            Nombre = "Resistencia",
            Categoria = CategoriaComponente.Pasivos,
            Descripcion = "Componente pasivo que se opone al paso de la corriente eléctrica según la Ley de Ohm (V = I · R), disipando energía en forma de calor y limitando la corriente de la rama.",
            RutaModelo = "res://models/resistencia.glb",
            // El .glb viene orientado a lo largo del eje Z (largo Z=12.69, ancho X=2.0, alto Y=3.09).
            // Al rotarlo +90° en Y queda alineado sobre el eje X (como el resto de los pasivos)
            // y los bornes coinciden exactamente con los extremos metálicos de PATA y pata2.
            RotacionBase = new Basis(Vector3.Up, Mathf.Pi / 2f),
            AlturaObjetivo = 0.14f,
            EscalaBornes = 0.65f,
            AnclaBorneA = new Vector3(-0.487f, 0.10f, -0.012f),
            AnclaBorneB = new Vector3(0.487f, 0.10f, -0.014f),
        };
        resistencia.Stats.Add(new StatDef("Resistencia", "Ω", 0.1, 1000000, 10,
            c => ((Resistor)c).Resistance, (c, v) => ((Resistor)c).Resistance = v)
        {
            Descripcion = "Valor óhmico del componente. A mayor resistencia, menor será la corriente que circule para una misma diferencia de potencial.",
            Formula = "I = V / R",
            HabilitadoSi = c => !((Resistor)c).ModoCorrienteObjetivo,
        });
        resistencia.Stats.Add(new StatDef("Modo corriente objetivo", "", 0, 1, 1,
            c => ((Resistor)c).ModoCorrienteObjetivo ? 1 : 0,
            (c, v) => ((Resistor)c).ModoCorrienteObjetivo = v >= 0.5)
        {
            Tipo = TipoStat.Booleano,
            Descripcion = "Ajuste automático: calcula y actualiza el valor en ohmios para alcanzar la corriente deseada en esta rama aplicando la Ley de Ohm.",
        });
        resistencia.Stats.Add(new StatDef("Corriente objetivo", "mA", 0.1, 5000, 1,
            c => ((Resistor)c).CorrienteObjetivo * 1000,
            (c, v) => ((Resistor)c).CorrienteObjetivo = v / 1000.0)
        {
            Descripcion = "Corriente deseada a través de la resistencia. Si la tensión del circuito no es suficiente para alcanzarla, se indicará en las lecturas.",
            HabilitadoSi = c => ((Resistor)c).ModoCorrienteObjetivo,
        });
        resistencia.Stats.AddRange(StatsLimites());
        resistencia.Lecturas.Add(new LecturaDef("Código de colores",
            c => CodigoColores.Describir(((Resistor)c).Resistance)));
        resistencia.Lecturas.Add(new LecturaDef("Objetivo", c =>
        {
            var r = (Resistor)c;
            if (!r.ModoCorrienteObjetivo) return "— (modo manual)";
            return r.ObjetivoAlcanzado ? "Alcanzado" : "No alcanzado (tensión insuficiente o convergiendo)";
        }));
        resistencia.Variantes.Add(Variante("real", "Resistencia",
            "Resistencia de carbón de 220 Ω y 0.5 W de disipación máxima, valor habitual para limitar la corriente de un LED estándar.",
            () => new Resistor("Resistencia", 220)
            {
                MaxPower = 0.5,       // como una resistencia de ½ watt real
                MaxVoltage = 250,
                MaxCurrent = 2,
            }));
        resistencia.Variantes.Add(Variante("sandbox", "Resistencia ∞",
            "Resistencia de 220 Ω sin límites de potencia ni temperatura.",
            () => TodoInfinito(new Resistor("Resistencia ∞", 220)), sinLimites: true));
        lista.Add(resistencia);

        var capacitor = new DefinicionTipo
        {
            Id = "capacitor",
            Nombre = "Capacitor",
            Categoria = CategoriaComponente.Pasivos,
            Descripcion = "Almacena energía en un campo eléctrico entre sus placas. En corriente continua (DC) conduce mientras se carga y bloquea la corriente una vez cargado. En corriente alterna (AC) presenta una reactancia capacitiva Xc = 1/(2π·f·C) y adelanta la corriente 90° respecto de la tensión.",
            RutaModelo = "res://models/capacitor.glb",
            AlturaObjetivo = 0.6f,
            EscalaBornes = 0.6f,
            AnclaBorneA = new Vector3(0.007f, 0.050f, 0.227f),
            AnclaBorneB = new Vector3(0.005f, 0.118f, -0.164f),
        };
        capacitor.Stats.Add(new StatDef("Capacitancia", "F", 1e-7, 10, 1e-7,
            c => ((Capacitor)c).Capacitance, (c, v) => ((Capacitor)c).Capacitance = v)
        {
            Descripcion = "Capacidad de almacenamiento de carga por unidad de tensión (Q = C · V). Determina la constante de tiempo del circuito (τ = R · C).",
            Formula = "τ = R·C  (63% de carga en 1τ, 99% en 5τ)",
            Escala = 1e6, UnidadVisual = "µF",
        });
        capacitor.Stats.Add(new StatDef("Resistencia serie (ESR)", "Ω", 0, 100, 0.01,
            c => ((Capacitor)c).ResistenciaSerie, (c, v) => ((Capacitor)c).ResistenciaSerie = v)
        {
            Descripcion = "Resistencia serie equivalente de las placas y terminales. Es la única parte del capacitor que disipa calor real por efecto Joule (la energía reactiva almacenada en el campo no calienta).",
            Formula = "P_calor = I²·ESR",
        });
        capacitor.Stats.AddRange(StatsLimites());
        capacitor.Lecturas.Add(new LecturaDef("Tensión almacenada",
            c => DescribirCargaCapacitor((Capacitor)c)));
        capacitor.Lecturas.Add(new LecturaDef("Estado",
            c => EstadoCapacitor((Capacitor)c)));
        capacitor.Lecturas.Add(new LecturaDef("Energía almacenada",
            c => $"{((Capacitor)c).EnergiaAlmacenada:F3} J  (½·C·V²)"));
        capacitor.Lecturas.Add(new LecturaDef("Comportamiento AC",
            c => c.ResultadoAC is { } r
                ? $"{r.CorrienteRms:F3} A · desfase {r.FaseGrados:F0}° · cos φ = {r.FactorPotencia:F2} · Q = {r.PotenciaReactiva:F1} VAr"
                : "— (activo solo en redes de corriente alterna)"));
        capacitor.Lecturas.Add(new LecturaDef("Potencia de carga",
            c => $"{((Capacitor)c).PotenciaAlmacenamiento * 1000:F1} mW  (energía hacia el campo eléctrico)"));
        capacitor.Variantes.Add(Variante("real", "Capacitor",
            "Capacitor no polarizado de 50 mF (50 000 µF) y 25 V máximos, ideal para observar curvas de carga y descarga RC en tiempo real.",
            () => new Capacitor("Capacitor", 0.05)));
        capacitor.Variantes.Add(Variante("sandbox", "Capacitor ∞",
            "Capacitor no polarizado sin límites de sobretensión ni temperatura.",
            () => TodoInfinito(new Capacitor("Capacitor ∞", 0.05)), sinLimites: true));
        lista.Add(capacitor);

        // ================= ILUMINACIÓN =================

        var led = new DefinicionTipo
        {
            Id = "led",
            Nombre = "LED",
            Categoria = CategoriaComponente.Iluminacion,
            Descripcion = "Diodo emisor de luz (polarizado). Conduce únicamente desde el ánodo (+) hacia el cátodo (−) cuando se supera su tensión umbral directa (Vf), requiriendo siempre una resistencia en serie para limitar la corriente.",
            RutaModelo = "res://models/LED.glb",
            AlturaObjetivo = 0.5f,
            AnclaBorneA = new Vector3(-0.3f, 0.05f, 0f),   // pata larga = positivo (ánodo)
            AnclaBorneB = new Vector3(0.28f, 0.02f, 0f),   // pata corta = negativo (cátodo)
        };
        led.Stats.Add(new StatDef("Voltaje umbral (Vf)", "V", 0.5, 5, 0.1,
            c => ((LED)c).VoltajeUmbral, (c, v) => ((LED)c).VoltajeUmbral = v)
        {
            Descripcion = "Tensión directa mínima necesaria para que el LED comience a conducir y emitir luz. En conducción mantiene esta caída casi constante entre sus terminales.",
            Formula = "I = (V_fuente − Vf) / R_serie",
        });
        led.Stats.Add(new StatDef("Resist. dinámica (cond.)", "Ω", 1, 2000, 1,
            c => ((LED)c).ResistenciaEncendido, (c, v) => ((LED)c).ResistenciaEncendido = v)
        {
            Descripcion = "Resistencia interna del semiconductor una vez superada la tensión umbral. Es muy baja (~12 Ω), por lo que conectarlo directo a una fuente sin resistencia serie provoca sobrecorriente.",
        });
        led.Stats.Add(new StatDef("Voltaje inverso máx.", "V", 1, 100, 1,
            c => ((LED)c).MaxVoltajeInverso, (c, v) => ((LED)c).MaxVoltajeInverso = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Tensión máxima que soporta cuando se conecta con polaridad invertida (cátodo al positivo) antes de sufrir ruptura inversa.",
        });
        led.Stats.AddRange(StatsLimites());
        led.Lecturas.Add(new LecturaDef("Estado", c =>
        {
            var l = (LED)c;
            return l.IsBurnt ? "Dañado" : l.EstaEncendido ? "Encendido" : "Apagado";
        }));
        led.Lecturas.Add(new LecturaDef("Brillo relativo",
            c => $"{((LED)c).Brillo:P0}"));
        led.Variantes.Add(Variante("real", "LED",
            "LED estándar (Vf = 2.0 V, potencia máxima 250 mW). Debe utilizarse con resistencia limitadora en serie.",
            () => new LED("LED", 2.0, 12)));
        led.Variantes.Add(Variante("sandbox", "LED ∞",
            "LED ideal protegido contra sobrecorriente, exceso de potencia y polarización inversa.",
            () => { var l = TodoInfinito(new LED("LED ∞", 2.0, 12)); l.MaxVoltajeInverso = double.PositiveInfinity; return l; },
            sinLimites: true));
        ConfigurarGuardadoColorLuz(led);
        lista.Add(led);

        var lampara = new DefinicionTipo
        {
            Id = "lampara",
            Nombre = "Lámpara",
            Categoria = CategoriaComponente.Iluminacion,
            Descripcion = "Lámpara incandescente de filamento metálico. No tiene polaridad, funciona tanto en continua (DC) como en alterna (AC), y su resistencia aumenta a medida que el filamento se calienta.",
            CrearPrimitiva = Primitivas.Lampara,
            AlturaObjetivo = 0.4f,
            AnclaBorneA = new Vector3(-0.2f, 0.06f, 0f),
            AnclaBorneB = new Vector3(0.2f, 0.06f, 0f),
        };
        lampara.Stats.Add(new StatDef("Resistencia nominal", "Ω", 5, 5000, 5,
            c => ((Lampara)c).Resistencia, (c, v) => ((Lampara)c).Resistencia = v)
        {
            Descripcion = "Resistencia eléctrica del filamento a temperatura de régimen (encendido). En frío su resistencia es unas 10 veces menor, generando un pico de corriente inicial al encenderse.",
            Formula = "R(T) = R_frío · (1 + α·(T − 20))",
        });
        lampara.Stats.Add(new StatDef("Potencia nominal", "W", 0.1, 100, 0.1,
            c => ((Lampara)c).PotenciaNominal, (c, v) => ((Lampara)c).PotenciaNominal = v)
        {
            Descripcion = "Potencia eléctrica para la cual alcanza el 100% de su brillo nominal.",
        });
        lampara.Stats.Add(new StatDef("Potencia mínima", "W", 0.0, 10, 0.01,
            c => ((Lampara)c).PotenciaMinima, (c, v) => ((Lampara)c).PotenciaMinima = v)
        {
            Descripcion = "Potencia umbral por debajo de la cual el filamento no alcanza la temperatura de incandescencia visible.",
        });
        lampara.Stats.AddRange(StatsLimites());
        lampara.Lecturas.Add(new LecturaDef("Filamento",
            c => { var l = (Lampara)c; return $"{l.GetResistance():F0} Ω a {l.Temperatura:F0} °C  (en frío: {l.ResistenciaFria:F0} Ω · nominal: {l.Resistencia:F0} Ω)"; }));
        lampara.Lecturas.Add(new LecturaDef("Pico de encendido",
            c => { var l = (Lampara)c; return $"×{l.RelacionFrioCaliente:F0} respecto del régimen permanente"; }));
        lampara.Lecturas.Add(new LecturaDef("Brillo relativo",
            c => $"{((Lampara)c).Brillo:P0}"));
        lampara.Variantes.Add(Variante("real", "Lámpara 12V (2.4W)",
            "Lámpara de baja tensión con filamento de 60 Ω en caliente y 2.4 W de potencia nominal.",
            () => new Lampara("Lámpara", 60)));
        lampara.Variantes.Add(Variante("foco220", "Foco 220V (60W)",
            "Lámpara incandescente de red eléctrica (220 V, 60 W, 800 Ω en caliente).",
            () => new Lampara("Foco 220V", 800) { PotenciaNominal = 60, PotenciaMinima = 1 }));
        lampara.Variantes.Add(Variante("sandbox", "Lámpara ∞",
            "Lámpara incandescente sin límite de ruptura por sobrepotencia o temperatura.",
            () => TodoInfinito(new Lampara("Lámpara ∞", 60)), sinLimites: true));
        ConfigurarGuardadoColorLuz(lampara);
        lista.Add(lampara);

        // ================= CONTROL =================

        var interruptor = new DefinicionTipo
        {
            Id = "interruptor",
            Nombre = "Interruptor",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Llave de conmutación biestable (encendido/apagado). Permite abrir o cerrar el paso de corriente manualmente con un clic o mediante un temporizador cíclico en segundos o ticks.",
            RutaModelo = "res://models/swichlinearriba.glb",     // palanca arriba = cerrado
            RutaModeloAlterno = "res://models/swichlineabajo.glb", // palanca abajo = abierto
            AlturaObjetivo = 0.32f,
            AnclaBorneA = new Vector3(-0.3f, 0.05f, 0f),
            AnclaBorneB = new Vector3(-0.02f, 0.05f, 0f),
        };
        interruptor.Stats.Add(new StatDef("Activar por tiempo (auto)", "", 0, 1, 1,
            c => ((Switch)c).TemporizadorActivo ? 1 : 0,
            (c, v) => ((Switch)c).TemporizadorActivo = v >= 0.5)
        {
            Tipo = TipoStat.Booleano,
            Descripcion = "Conmuta automáticamente entre estado abierto y cerrado según el período configurado.",
        });
        interruptor.Stats.Add(new StatDef("Unidad del temporizador", "", 0, 1, 1,
            c => ((Switch)c).UsarTicks ? 1 : 0,
            (c, v) => ((Switch)c).UsarTicks = v >= 0.5)
        {
            Tipo = TipoStat.Opciones,
            NombresOpciones = new[] { "Segundos (s)", "Ticks" },
            Descripcion = "Define si el intervalo de conmutación automática se expresa en segundos o en ticks de simulación.",
            HabilitadoSi = c => ((Switch)c).TemporizadorActivo,
        });
        interruptor.Stats.Add(new StatDef("Cambiar cada (segundos)", "s", 0.05, 300, 0.1,
            c => ((Switch)c).IntervaloSegundos,
            (c, v) => ((Switch)c).IntervaloSegundos = v)
        {
            Descripcion = "Período en segundos entre cada cambio de posición de la llave.",
            HabilitadoSi = c => ((Switch)c).TemporizadorActivo && !((Switch)c).UsarTicks,
        });
        interruptor.Stats.Add(new StatDef("Cambiar cada (ticks)", " ticks", 1, 10000, 1,
            c => ((Switch)c).IntervaloTicks,
            (c, v) => ((Switch)c).IntervaloTicks = (int)Math.Round(v))
        {
            Descripcion = "Cantidad de ticks de simulación entre cada cambio de posición de la llave.",
            HabilitadoSi = c => ((Switch)c).TemporizadorActivo && ((Switch)c).UsarTicks,
        });
        interruptor.Stats.AddRange(StatsLimites());
        interruptor.Lecturas.Add(new LecturaDef("Posición",
            c => ((Switch)c).IsClosed ? "Cerrado (conduce)" : "Abierto (no conduce)"));
        interruptor.Lecturas.Add(new LecturaDef("Temporizador",
            c => ((Switch)c).DescribirTemporizador()));
        interruptor.Variantes.Add(Variante("real", "Interruptor",
            "Interruptor mecánico de un polo con límites reales de corriente en sus contactos.",
            () => new Switch("Interruptor", startClosed: false)));
        interruptor.Variantes.Add(Variante("sandbox", "Interruptor ∞",
            "Interruptor ideal sin desgaste ni daño por sobrecorriente en sus contactos.",
            () => TodoInfinito(new Switch("Interruptor ∞", startClosed: false)), sinLimites: true));
        // Estado vivo que no es una stat: si quedó abierto o cerrado.
        interruptor.GuardarEstado = (c, e) => e.Poner("cerrado", ((Switch)c).IsClosed);
        interruptor.CargarEstado = (c, e) => ((Switch)c).IsClosed = e.Booleano("cerrado", true);
        lista.Add(interruptor);

        var pulsador = new DefinicionTipo
        {
            Id = "pulsador",
            Nombre = "Pulsador",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Interruptor momentáneo normalmente abierto (NA). Cierra el circuito durante el tiempo de pulsación configurado y luego retorna automáticamente al estado abierto. Permite programar activaciones periódicas automáticas.",
            CrearPrimitiva = Primitivas.Pulsador,
            AlturaObjetivo = 0.22f,
            AnclaBorneA = new Vector3(-0.42f, 0.3f, 0f),
            AnclaBorneB = new Vector3(0.42f, 0.3f, 0f),
        };
        pulsador.Stats.Add(new StatDef("Unidad de tiempo", "", 0, 1, 1,
            c => ((Pulsador)c).UsarTicks ? 1 : 0,
            (c, v) => ((Pulsador)c).UsarTicks = v >= 0.5)
        {
            Tipo = TipoStat.Opciones,
            NombresOpciones = new[] { "Segundos (s)", "Ticks" },
            Descripcion = "Define si la duración del pulso y el intervalo de espera se configuran en segundos o en ticks.",
        });
        pulsador.Stats.Add(new StatDef("Tiempo presionado (seg.)", "s", 0.05, 120, 0.05,
            c => ((Pulsador)c).DuracionPulso, (c, v) => ((Pulsador)c).DuracionPulso = v)
        {
            Descripcion = "Duración en segundos durante la cual el contacto permanece cerrado al accionarse.",
            HabilitadoSi = c => !((Pulsador)c).UsarTicks,
        });
        pulsador.Stats.Add(new StatDef("Tiempo presionado (ticks)", " ticks", 1, 10000, 1,
            c => ((Pulsador)c).DuracionPulsoTicks, (c, v) => ((Pulsador)c).DuracionPulsoTicks = (int)Math.Round(v))
        {
            Descripcion = "Cantidad de ticks durante los cuales el contacto permanece cerrado al accionarse.",
            HabilitadoSi = c => ((Pulsador)c).UsarTicks,
        });
        pulsador.Stats.Add(new StatDef("Presionar por tiempo (auto)", "", 0, 1, 1,
            c => ((Pulsador)c).TemporizadorActivo ? 1 : 0,
            (c, v) => ((Pulsador)c).TemporizadorActivo = v >= 0.5)
        {
            Tipo = TipoStat.Booleano,
            Descripcion = "Activa la pulsación automática periódica. El contador de espera se reinicia una vez que el pulsador se suelta.",
        });
        pulsador.Stats.Add(new StatDef("Presionar cada (segundos)", "s", 0.05, 300, 0.1,
            c => ((Pulsador)c).IntervaloSegundos, (c, v) => ((Pulsador)c).IntervaloSegundos = v)
        {
            Descripcion = "Tiempo de espera en segundos (con el botón abierto) antes de iniciar la siguiente pulsación.",
            HabilitadoSi = c => ((Pulsador)c).TemporizadorActivo && !((Pulsador)c).UsarTicks,
        });
        pulsador.Stats.Add(new StatDef("Presionar cada (ticks)", " ticks", 1, 10000, 1,
            c => ((Pulsador)c).IntervaloTicks, (c, v) => ((Pulsador)c).IntervaloTicks = (int)Math.Round(v))
        {
            Descripcion = "Ticks de espera (con el botón abierto) antes de iniciar la siguiente pulsación.",
            HabilitadoSi = c => ((Pulsador)c).TemporizadorActivo && ((Pulsador)c).UsarTicks,
        });
        pulsador.Stats.AddRange(StatsLimites());
        pulsador.Lecturas.Add(new LecturaDef("Estado",
            c => ((Pulsador)c).Presionado ? "Presionado (conduce)" : "Abierto (en reposo)"));
        pulsador.Lecturas.Add(new LecturaDef("Temporizador",
            c => ((Pulsador)c).DescribirTemporizador()));
        pulsador.Variantes.Add(Variante("real", "Pulsador",
            "Pulsador momentáneo con cierre predeterminado de 0.5 s por accionamiento.",
            () => new Pulsador("Pulsador")));
        pulsador.Variantes.Add(Variante("sandbox", "Pulsador ∞",
            "Pulsador momentáneo ideal sin límites de corriente ni temperatura.",
            () => TodoInfinito(new Pulsador("Pulsador ∞")), sinLimites: true));
        lista.Add(pulsador);

        // ================= PROTECCIÓN =================

        var fusible = new DefinicionTipo
        {
            Id = "fusible",
            Nombre = "Fusible",
            Categoria = CategoriaComponente.Proteccion,
            Descripcion = "Elemento de protección contra sobrecorrientes. Se conecta en serie con el circuito; cuando la corriente supera el umbral nominal durante el tiempo de fusión, su filamento se funde y abre el circuito para proteger al resto de los componentes.",
            CrearPrimitiva = Primitivas.Fusible,
            AlturaObjetivo = 0.14f,
            AnclaBorneA = new Vector3(-0.47f, 0.45f, 0f),
            AnclaBorneB = new Vector3(0.47f, 0.45f, 0f),
        };
        fusible.Stats.Add(new StatDef("Corriente de corte", "A", 0.005, 50, 0.05,
            c => c.MaxCurrent, (c, v) => c.MaxCurrent = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Corriente nominal máxima que el fusible permite circular de forma continua antes de fundirse y abrir el circuito.",
        });
        fusible.Stats.Add(new StatDef("Resist. del filamento", "Ω", 0.01, 5, 0.01,
            c => ((Fusible)c).ResistenciaInterna, (c, v) => ((Fusible)c).ResistenciaInterna = v)
        {
            Descripcion = "Resistencia eléctrica propia del hilo conductor interno mientras permanece íntegro.",
        });
        fusible.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Fundido (circuito abierto)" : "Íntegro (conduce)"));
        fusible.Variantes.Add(Variante("real", "Fusible rápido (1A)",
            "Fusible de acción rápida calibrado a 1 A. Debe dimensionarse ligeramente por encima de la corriente normal de trabajo del circuito.",
            () => new Fusible("Fusible", 1.0)));
        fusible.Variantes.Add(Variante("retardado", "Fusible retardado (T)",
            "Fusible de acción lenta (curva T): tolera picos breves de arranque (como los de motores o capacitores) pero corta ante sobrecargas sostenidas.",
            () => new Fusible("Fusible T", 1.0) { ExponenteCurva = 1, SegundosBase = 1.5 }));
        // --- Disyuntor termomagnético (rearmable) ---
        var disyuntor = new DefinicionTipo
        {
            Id = "disyuntor",
            Nombre = "Disyuntor (Breaker)",
            Categoria = CategoriaComponente.Proteccion,
            Descripcion = "Interruptor automático de protección rearmable (AC y DC). A diferencia de un fusible, abre la línea ante sobrecorriente o sobretensión sin destruirse y permite volver a encenderlo con un clic o desde el inspector tras corregir la falla.",
            CrearPrimitiva = Primitivas.Disyuntor,
            AlturaObjetivo = 0.34f,
            AnclaBorneA = new Vector3(-0.49f, 0.24f, 0f),
            AnclaBorneB = new Vector3(0.49f, 0.24f, 0f),
        };
        disyuntor.Stats.Add(new StatDef("Corriente de disparo", "A", 0.05, 50, 0.05,
            c => ((Disyuntor)c).CorrienteDisparo, (c, v) => ((Disyuntor)c).CorrienteDisparo = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Corriente nominal máxima antes de disparar automáticamente (curva inversa rápida ante cortocircuitos). En ∞ se deshabilita el disparo por amperaje.",
            Formula = "Si |I| > I_disparo → abre la línea",
        });
        disyuntor.Stats.Add(new StatDef("Voltaje máx. de disparo", "V", 1, 500, 1,
            c => ((Disyuntor)c).VoltajeDisparo, (c, v) => ((Disyuntor)c).VoltajeDisparo = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Tensión máxima de línea o en bornes tolerada antes de disparar por sobretensión. En ∞ se deshabilita el disparo por voltaje.",
            Formula = "Si V_línea > V_disparo → abre la línea",
        });
        disyuntor.Stats.Add(new StatDef("Retardo térmico", "s", 0.02, 5, 0.05,
            c => ((Disyuntor)c).TiempoDisparo, (c, v) => ((Disyuntor)c).TiempoDisparo = v)
        {
            Descripcion = "Tiempo base de respuesta para sobrecargas moderadas (los cortocircuitos severos y sobretensiones cortan casi al instante).",
        });
        disyuntor.Stats.Add(new StatDef("Resist. de contacto", "Ω", 0.01, 2, 0.01,
            c => ((Disyuntor)c).ResistenciaInterna, (c, v) => ((Disyuntor)c).ResistenciaInterna = v)
        {
            Descripcion = "Resistencia interna mínima de los contactos cerrados para censar la corriente sin alterar el circuito.",
        });
        disyuntor.Stats.AddRange(StatsLimites());
        disyuntor.Lecturas.Add(new LecturaDef("Estado", c =>
        {
            var d = (Disyuntor)c;
            if (d.IsBurnt) return $"Dañado ({d.MotivoQuemado})";
            if (d.Disparado) return $"DISPARADO — {d.MotivoDisparo}";
            return d.CerradoManual ? "Encendido (cerrado, conduce)" : "Apagado manualmente (abierto)";
        }));
        disyuntor.Lecturas.Add(new LecturaDef("Tensión monitoreada",
            c => $"{((Disyuntor)c).VoltajeObservado:F2} V"));
        disyuntor.Variantes.Add(Variante("real", "Disyuntor (1A / 15V)",
            "Protección rearmable para circuitos electrónicos DC de baja tensión: corta sobre 1 A o 15 V y se vuelve a encender con un clic.",
            () => new Disyuntor("Disyuntor", 1.0, 15.0)));
        disyuntor.Variantes.Add(Variante("potencia", "Disyuntor (10A / 250V)",
            "Llave termomagnética de mayor capacidad para líneas AC 220 V~ o cargas de alta potencia.",
            () => new Disyuntor("Disyuntor 10A", 10.0, 250.0)));
        disyuntor.Variantes.Add(Variante("sandbox", "Disyuntor ∞",
            "Disyuntor con carcasa indestructible (límites físicos ∞) manteniendo los umbrales configurables de disparo.",
            () => TodoInfinito(new Disyuntor("Disyuntor ∞", 1.0, 15.0)), sinLimites: true));
        disyuntor.GuardarEstado = (c, e) =>
        {
            var d = (Disyuntor)c;
            e.Poner("cerrado_manual", d.CerradoManual);
            e.Poner("disparado", d.Disparado);
            e.Poner("motivo_disparo", d.MotivoDisparo);
        };
        disyuntor.CargarEstado = (c, e) =>
        {
            var d = (Disyuntor)c;
            d.RestaurarEstado(
                e.Booleano("cerrado_manual", true),
                e.Booleano("disparado", false),
                e.Texto("motivo_disparo", ""));
        };
        lista.Add(disyuntor);

        lista.Add(fusible);


        // ================= LO NUEVO DEL PEDIDO (con primitivas provisorias:
        // cuando lleguen los .glb, es solo completar RutaModelo) =========

        // --- Potenciómetro: el "dimmer" de perilla ---
        var pote = new DefinicionTipo
        {
            Id = "potenciometro",
            Nombre = "Potenciómetro (3 terminales)",
            Categoria = CategoriaComponente.Pasivos,
            Descripcion = "Resistencia variable de tres terminales: los dos extremos de la pista resistiva (A y B) y un cursor móvil central (W). Conectando A y B a una fuente, el terminal W entrega una fracción ajustable de la tensión (divisor de tensión). Si se usan solo dos terminales (A y W), actúa como resistencia variable en serie.",
            CrearPrimitiva = Primitivas.Potenciometro,
            AlturaObjetivo = 0.28f,
            AnclaBorneA = new Vector3(-0.324f, 0.15f, 0.48f),
            EtiquetaBorneA = "A",
            // OJO: el PinB heredado ES EL CURSOR. La arista propia del
            // componente es PinA↔PinB, y esa arista tiene que ser el tramo
            // A→cursor para que el divisor funcione. El otro extremo de la
            // pista va como borne extra ("B").
            EtiquetaBorneB = "W",
            AnclaBorneB = new Vector3(0f, 0.15f, 0.48f),
            EscalaBornes = 0.75f,
        };
        pote.AnclasBornesExtra.Add(("B", new Vector3(0.324f, 0.15f, 0.48f))); // el otro extremo de la pista
        pote.Stats.Add(new StatDef("Posición del cursor", "%", 0, 100, 1,
            c => ((Potenciometro)c).Perilla, (c, v) => ((Potenciometro)c).Perilla = v)
        {
            MostrarSlider = true,
            Descripcion = "Ubicación porcentual del cursor (W) a lo largo de la pista entre A y B. En configuración de divisor de tensión sin carga, determina directamente el porcentaje de tensión presente en W.",
            Formula = "V_W = V_AB · R_(W→B) / R_total",
        });
        pote.Stats.Add(new StatDef("Curva logarítmica", "", 0, 1, 1,
            c => ((Potenciometro)c).EsLogaritmico ? 1 : 0, (c, v) => ((Potenciometro)c).EsLogaritmico = v >= 0.5)
        {
            Tipo = TipoStat.Booleano,
            Descripcion = "Desactivado: pista lineal (ideal para divisor de tensión). Activado: respuesta logarítmica (ideal para audio/brillo).",
        });
        pote.Stats.Add(new StatDef("Resistencia total", "Ω", 100, 1_000_000, 100,
            c => ((Potenciometro)c).ResistenciaTotal, (c, v) => ((Potenciometro)c).ResistenciaTotal = v)
        {
            Descripcion = "Resistencia nominal medida entre los dos extremos fijos (A y B) de la pista resistiva.",
        });
        pote.Stats.Add(new StatDef("Resist. de contacto", "Ω", 0.1, 1000, 1,
            c => ((Potenciometro)c).ResistenciaContacto, (c, v) => ((Potenciometro)c).ResistenciaContacto = v)
        {
            Descripcion = "Resistencia residual mínima cuando el cursor se encuentra en cualquiera de los extremos físicos de la pista.",
        });
        pote.Stats.AddRange(StatsLimites());
        pote.Lecturas.Add(new LecturaDef("Tramo A→W",
            c => $"{((Potenciometro)c).ResistenciaTramoA:F0} Ω"));
        pote.Lecturas.Add(new LecturaDef("Tramo W→B",
            c => $"{((Potenciometro)c).ResistenciaTramoB:F0} Ω"));
        pote.Lecturas.Add(new LecturaDef("Divisor de tensión",
            c => { var p = (Potenciometro)c; return $"{p.FraccionDeVoltaje:P0} de V_AB · tensión en W: {p.VoltajeCursor:F2} V"; }));
        pote.Variantes.Add(Variante("real", "Potenciómetro 10kΩ",
            "Potenciómetro lineal de carbón de 10 kΩ y 0.5 W de potencia máxima.",
            () => new Potenciometro("Potenciómetro", 10_000)));
        pote.Variantes.Add(Variante("sandbox", "Potenciómetro ∞",
            "Potenciómetro lineal de 10 kΩ sin límites de potencia ni temperatura.",
            () => TodoInfinito(new Potenciometro("Potenciómetro ∞", 10_000)), sinLimites: true));
        lista.Add(pote);

        // --- Motores DC (cuatro variantes mecánicas/visuales con rotación real en el eje) ---
        var motor = CrearTipoMotor(
            id: "motor",
            nombre: "Motor DC",
            descripcion: "Máquina rotativa de corriente continua que transforma energía eléctrica en energía mecánica. Su indicador giratorio en el eje muestra la velocidad y el sentido según la polaridad aplicada.",
            primitiva: Primitivas.Motor,
            alturaObjetivo: 0.3f,
            anclaA: new Vector3(-0.42f, 0.35f, 0f),
            anclaB: new Vector3(0.42f, 0.35f, 0f));
        motor.Variantes.Add(Variante("real", "Motor DC",
            "Motor de corriente continua con bobinado de 25 Ω. En el arranque o con el eje bloqueado consume su corriente máxima.",
            () => new Motor("Motor DC", 25)));
        motor.Variantes.Add(Variante("sandbox", "Motor DC ∞",
            "Motor DC ideal protegido contra quemado por bloqueo de eje, sobrecorriente o temperatura.",
            () => TodoInfinito(new Motor("Motor DC ∞", 25)), sinLimites: true));
        lista.Add(motor);

        var motorVentilador = CrearTipoMotor(
            id: "motor_ventilador",
            nombre: "Motor Ventilador (Hélice)",
            descripcion: "Motor DC acoplado a una hélice de 3 aspas con pala testigo naranja. Permite observar claramente cómo varía la velocidad con el voltaje recibido y cómo invierte el giro al cambiar la polaridad.",
            primitiva: Primitivas.MotorVentilador,
            alturaObjetivo: 0.42f,
            anclaA: new Vector3(-0.42f, 0.13f, 0.22f),
            anclaB: new Vector3(-0.42f, 0.13f, -0.22f),
            escalaBornes: 0.72f);
        motorVentilador.Variantes.Add(Variante("real", "Motor Ventilador",
            "Ventilador DC con bobinado de 18 Ω y mayor inercia rotacional por las aspas.",
            () => new Motor("Motor Ventilador", 18)
            {
                ConstanteFcem = 0.045,
                ConstantePar = 0.045,
                Friccion = 1.4e-5,
                Inercia = 4e-5,
                MaxCurrent = 0.45,
            }));
        motorVentilador.Variantes.Add(Variante("sandbox", "Motor Ventilador ∞",
            "Ventilador DC ideal sin límites de sobrecorriente ni temperatura.",
            () => TodoInfinito(new Motor("Motor Ventilador ∞", 18)
            {
                ConstanteFcem = 0.045,
                ConstantePar = 0.045,
                Friccion = 1.4e-5,
                Inercia = 4e-5,
            }), sinLimites: true));
        lista.Add(motorVentilador);

        var motorReductor = CrearTipoMotor(
            id: "motor_reductor",
            nombre: "Motor Reductor (Disco)",
            descripcion: "Motor DC con caja reductora de alto torque y disco tacométrico con puntero rojo. Gira a RPM bajas/medias para seguir cada vuelta y el cambio de sentido en tiempo real.",
            primitiva: Primitivas.MotorReductor,
            alturaObjetivo: 0.34f,
            anclaA: new Vector3(-0.49f, 0.54f, 0.19f),
            anclaB: new Vector3(-0.49f, 0.54f, -0.19f),
            escalaBornes: 0.72f);
        motorReductor.Variantes.Add(Variante("real", "Motor Reductor",
            "Motorreductor de alto par (k = 0.35 V·s/rad) y velocidad moderada (~200-350 RPM).",
            () => new Motor("Motor Reductor", 30)
            {
                ConstanteFcem = 0.35,
                ConstantePar = 0.35,
                Friccion = 5e-5,
                Inercia = 1.5e-4,
                MaxCurrent = 0.35,
            }));
        motorReductor.Variantes.Add(Variante("sandbox", "Motor Reductor ∞",
            "Motorreductor ideal protegido contra bloqueo de eje o sobrecarga.",
            () => TodoInfinito(new Motor("Motor Reductor ∞", 30)
            {
                ConstanteFcem = 0.35,
                ConstantePar = 0.35,
                Friccion = 5e-5,
                Inercia = 1.5e-4,
            }), sinLimites: true));
        lista.Add(motorReductor);

        var motorRapido = CrearTipoMotor(
            id: "motor_alta_velocidad",
            nombre: "Micro Motor Rápido",
            descripcion: "Motor DC compacto de baja impedancia y rotor liviano con barra bicolor de balanceo. Responde al instante a variaciones de tensión o pulsos PWM alcanzando altas RPM.",
            primitiva: Primitivas.MotorAltaVelocidad,
            alturaObjetivo: 0.26f,
            anclaA: new Vector3(-0.476f, 0.565f, 0.225f),
            anclaB: new Vector3(-0.476f, 0.565f, -0.225f),
            escalaBornes: 0.68f);
        motorRapido.Variantes.Add(Variante("real", "Micro Motor Rápido",
            "Motor tipo 130 de alta velocidad (12 Ω, k = 0.012 V·s/rad, arranque muy ágil).",
            () => new Motor("Micro Motor Rápido", 12)
            {
                ConstanteFcem = 0.012,
                ConstantePar = 0.012,
                Friccion = 3e-6,
                Inercia = 5e-6,
                MaxCurrent = 0.6,
            }));
        motorRapido.Variantes.Add(Variante("sandbox", "Micro Motor Rápido ∞",
            "Micro motor de altas RPM ideal sin límites de quemado.",
            () => TodoInfinito(new Motor("Micro Motor Rápido ∞", 12)
            {
                ConstanteFcem = 0.012,
                ConstantePar = 0.012,
                Friccion = 3e-6,
                Inercia = 5e-6,
            }), sinLimites: true));
        lista.Add(motorRapido);

        // --- Temporizador NE555 (DIP-8) ---
        var ne555 = new DefinicionTipo
        {
            Id = "ne555",
            Nombre = "Temporizador NE555",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Circuito integrado temporizador de 8 pines (el más famoso de la electrónica). Incluye divisor interno de 3 × 5 kΩ (1/3 y 2/3 Vcc), flip-flop SR, descarga (DISCH) y salida push-pull (OUT) para armar osciladores astables (ondas cuadradas para parpadeo o tonos) y retardos monoestables (con resistencia pull-up de 10 kΩ en TRIG).",
            CrearPrimitiva = Primitivas.Ne555,
            AlturaObjetivo = 0.22f,
            AnclaBorneA = new Vector3(-0.359f, 0.38f, -0.462f), // Pin 8: VCC
            AnclaBorneB = new Vector3(-0.359f, 0.38f, 0.462f),  // Pin 1: GND
            EtiquetaBorneA = "VCC",
            EtiquetaBorneB = "GND",
            EscalaBornes = 0.55f,
        };
        ne555.AnclasBornesExtra.Add(("TRIG", new Vector3(-0.120f, 0.38f, 0.462f)));  // Pin 2
        ne555.AnclasBornesExtra.Add(("OUT", new Vector3(0.120f, 0.38f, 0.462f)));    // Pin 3
        ne555.AnclasBornesExtra.Add(("RESET", new Vector3(0.359f, 0.38f, 0.462f)));  // Pin 4
        ne555.AnclasBornesExtra.Add(("CTRL", new Vector3(0.359f, 0.38f, -0.462f)));  // Pin 5
        ne555.AnclasBornesExtra.Add(("THRES", new Vector3(0.120f, 0.38f, -0.462f))); // Pin 6
        ne555.AnclasBornesExtra.Add(("DISCH", new Vector3(-0.120f, 0.38f, -0.462f))); // Pin 7
        ne555.Stats.Add(new StatDef("V mín. alimentación", "V", 2.0, 9.0, 0.5,
            c => ((Temporizador555)c).VoltajeMinimoOperacion,
            (c, v) => ((Temporizador555)c).VoltajeMinimoOperacion = v)
        {
            Descripcion = "Tensión mínima entre VCC (pin 8) y GND (pin 1) para que el integrado entre en operación (4.5 V típico en NE555 bipolar).",
        });
        ne555.Stats.Add(new StatDef("Resist. de salida OUT", "Ω", 1, 200, 1,
            c => ((Temporizador555)c).ResistenciaSalida,
            (c, v) => ((Temporizador555)c).ResistenciaSalida = v)
        {
            Descripcion = "Impedancia interna de la etapa de salida pin 3 (OUT) tanto en estado ALTO como BAJO.",
        });
        ne555.Stats.Add(new StatDef("Resist. descarga DISCH", "Ω", 1, 100, 1,
            c => ((Temporizador555)c).ResistenciaDescarga,
            (c, v) => ((Temporizador555)c).ResistenciaDescarga = v)
        {
            Descripcion = "Resistencia en conducción del transistor interno de descarga entre pin 7 (DISCH) y GND.",
        });
        ne555.Stats.AddRange(StatsLimites());
        ne555.Lecturas.Add(new LecturaDef("Estado salida (OUT)", c =>
        {
            var t = (Temporizador555)c;
            if (t.IsBurnt) return $"Quemado ({t.MotivoQuemado})";
            if (!t.Energizado) return $"Apagado / subalimentado ({t.VoltajeAlimentacion:F2} V)";
            string edo = t.SalidaAlta ? "ALTO (conduce a VCC)" : "BAJO (conduce a GND · DISCH activo)";
            return t.FrecuenciaMedidaHz > 0.05
                ? $"{edo} · {t.FrecuenciaMedidaHz:F2} Hz"
                : edo;
        }));
        ne555.Lecturas.Add(new LecturaDef("Tensión OUT / VCC",
            c => { var t = (Temporizador555)c; return $"{t.VoltajeSalida:F2} V  /  {t.VoltajeAlimentacion:F2} V"; }));
        ne555.Lecturas.Add(new LecturaDef("Umbrales (⅓ · ⅔ Vcc)",
            c => { var t = (Temporizador555)c; return $"TRIG < {t.UmbralInferior:F2} V  ·  THRES > {t.UmbralSuperior:F2} V"; }));
        ne555.Lecturas.Add(new LecturaDef("Corriente pin 3 (OUT)",
            c => $"{((Temporizador555)c).CorrienteSalida * 1000:F1} mA"));
        ne555.Variantes.Add(Variante("real", "Temporizador NE555",
            "Integrado NE555 clásico (4.5-16 V=, salida hasta 200 mA). Ideal para osciladores LED, retardos y señales cuadradas.",
            () => new Temporizador555("NE555")));
        ne555.Variantes.Add(Variante("sandbox", "Temporizador NE555 ∞",
            "NE555 ideal sin límites de ruptura por sobretensión, polaridad inversa, corriente ni temperatura.",
            () => TodoInfinito(new Temporizador555("NE555 ∞")), sinLimites: true));
        ne555.GuardarEstado = (c, e) => e.Poner("salida_alta", ((Temporizador555)c).SalidaAlta);
        ne555.CargarEstado = (c, e) => ((Temporizador555)c).RestaurarSalida(e.Booleano("salida_alta", false));
        lista.Add(ne555);

        // --- Transistor NPN: la LLAVE electrónica de tres bornes ---
        var transistor = new DefinicionTipo
        {
            Id = "transistor_npn",
            Nombre = "Transistor NPN",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Transistor bipolar de unión (BJT) tipo NPN. Una pequeña corriente entrante por la Base (B) hacia el Emisor (E) controla el paso de una corriente mucho mayor desde el Colector (C) hacia el Emisor (E), funcionando como interruptor electrónico o amplificador.",
            RutaModelo = "res://models/transistor.glb",
            RotacionBase = new Basis(Vector3.Right, Mathf.Pi / 2f), // el modelo viene acostado: se para con las patas hacia abajo
            AlturaObjetivo = 0.7f,
            AnclaBorneA = new Vector3(0.006f, 0.043f, 0.316f), // C (colector)
            AnclaBorneB = new Vector3(0.292f, 0.043f, 0.316f),  // E (emisor)
            EtiquetaBorneA = "C",
            EtiquetaBorneB = "E",
            EscalaBornes = 0.6f, // tres bornes juntos: esferas un toque más chicas
        };
        transistor.AnclasBornesExtra.Add(("B", new Vector3(-0.280f, 0.043f, 0.316f))); // base: la pata de la izquierda (B-C-E)
        transistor.Stats.Add(new StatDef("Umbral base-emisor", "V", 0.3, 1.0, 0.05,
            c => ((TransistorNPN)c).VoltajeUmbralBase, (c, v) => ((TransistorNPN)c).VoltajeUmbralBase = v)
        {
            Descripcion = "Tensión directa mínima entre Base y Emisor (V_BE) necesaria para polarizar la juntura de control (~0.7 V en transistores de silicio).",
            Formula = "Conduce si V(B) − V(E) ≥ V_BE(umbral)",
        });
        transistor.Stats.Add(new StatDef("Resist. en saturación", "Ω", 0.1, 100, 0.5,
            c => ((TransistorNPN)c).ResistenciaSaturacion, (c, v) => ((TransistorNPN)c).ResistenciaSaturacion = v)
        {
            Descripcion = "Resistencia equivalente entre Colector y Emisor cuando el transistor se encuentra saturado (en conducción plena).",
        });
        transistor.Stats.Add(new StatDef("Resist. interna de base", "Ω", 1, 1000000, 10,
            c => ((TransistorNPN)c).ResistenciaBase, (c, v) => ((TransistorNPN)c).ResistenciaBase = v)
        {
            Descripcion = "Resistencia serie interna de la juntura Base-Emisor. Como dicha juntura actúa como un diodo, siempre debe colocarse una resistencia limitadora externa en la Base.",
        });
        transistor.Stats.Add(new StatDef("Resist. de corte (base)", "Ω", 1000, 10000000, 1000,
            c => ((TransistorNPN)c).ResistenciaFugaBase, (c, v) => ((TransistorNPN)c).ResistenciaFugaBase = v)
        {
            Descripcion = "Resistencia de pérdida por debajo del umbral V_BE. Mantiene al transistor en corte cuando la Base queda desconectada.",
        });
        transistor.Stats.Add(new StatDef("Corriente mín. de base", "mA", 0.001, 100, 0.01,
            c => ((TransistorNPN)c).CorrienteMinimaBase * 1000,
            (c, v) => ((TransistorNPN)c).CorrienteMinimaBase = v / 1000.0)
        {
            Descripcion = "Corriente de Base mínima requerida para conmutar el canal Colector-Emisor al estado de saturación.",
            Formula = "I_B ≥ I_C / hFE",
        });
        transistor.Stats.AddRange(StatsLimites());
        transistor.Lecturas.Add(new LecturaDef("Estado",
            c => ((TransistorNPN)c).Conduciendo ? "Saturación (conduce entre C y E)" : "En corte (abierto entre C y E)"));
        transistor.Lecturas.Add(new LecturaDef("V base-emisor",
            c => $"{((TransistorNPN)c).VoltajeBaseEmisor:F2} V"));
        transistor.Lecturas.Add(new LecturaDef("Corriente de base",
            c => $"{((TransistorNPN)c).CorrienteBase * 1000:F2} mA"));
        transistor.Variantes.Add(Variante("real", "Transistor NPN (2N2222)",
            "Transistor NPN de propósito general (V_BE ≈ 0.7 V, potencia máxima 0.5 W).",
            () => new TransistorNPN("Transistor NPN")));
        transistor.Variantes.Add(Variante("sandbox", "Transistor NPN ∞",
            "Transistor NPN ideal sin límites de ruptura por corriente, tensión ni temperatura.",
            () => TodoInfinito(new TransistorNPN("Transistor NPN ∞")), sinLimites: true));
        lista.Add(transistor);

        // --- Inductor: por INDUCTANCIA (1mH / 100mH / 1H) + la variante ∞ ---
        var inductor = new DefinicionTipo
        {
            Id = "inductor",
            Nombre = "Inductor (bobina)",
            Categoria = CategoriaComponente.Pasivos,
            Descripcion = "Almacena energía en un campo magnético cuando circula corriente por su devanado. Se opone a los cambios bruscos de corriente (V = L · dI/dt): en continua termina comportándose como la resistencia de su alambre, mientras que en alterna presenta una reactancia inductiva XL = 2π·f·L y atrasa la corriente 90° respecto de la tensión.",
            RutaModelo = "res://models/inductor.glb",
            AlturaObjetivo = 0.6f,
            EscalaBornes = 0.6f,
            AnclaBorneA = new Vector3(-0.485f, 0.057f, -0.039f),
            AnclaBorneB = new Vector3(0.472f, 0.050f, -0.040f),
        };
        inductor.Stats.Add(new StatDef("Inductancia", "H", 0.001, 10, 0.001,
            c => ((Inductor)c).Inductancia, (c, v) => ((Inductor)c).Inductancia = v)
        { Descripcion = "Inductancia de la bobina en Henrios (H). Determina la constante de tiempo inductiva del circuito (τ = L / R) y la reactancia en corriente alterna." });
        inductor.Stats.Add(new StatDef("Resist. del bobinado", "Ω", 0.05, 100, 0.05,
            c => ((Inductor)c).ResistenciaBobinado, (c, v) => ((Inductor)c).ResistenciaBobinado = v)
        {
            Descripcion = "Resistencia óhmica del alambre de cobre que forma la bobina. En régimen permanente de corriente continua es lo único que limita la corriente.",
        });
        inductor.Stats.AddRange(StatsLimites());
        inductor.Lecturas.Add(new LecturaDef("Campo magnético",
            c => $"{((Inductor)c).NivelFlujo:P0} establecido"));
        inductor.Lecturas.Add(new LecturaDef("Energía magnética",
            c => $"{((Inductor)c).EnergiaAlmacenada * 1000:F2} mJ  (½·L·I²)"));
        inductor.Lecturas.Add(new LecturaDef("Comportamiento AC",
            c => c.ResultadoAC is { } r
                ? $"{r.CorrienteRms:F3} A · desfase {r.FaseGrados:F0}° · cos φ = {r.FactorPotencia:F2} · Q = {r.PotenciaReactiva:F1} VAr"
                : "— (activo solo en redes de corriente alterna)"));
        inductor.Lecturas.Add(new LecturaDef("Tensión en bornes",
            c => $"{((Inductor)c).VoltajeEnBornes:F1} V  (máx. admisible: {((Inductor)c).MaxVoltage:F0} V)"));
        inductor.Variantes.Add(Variante("rapido", "Bobina 1 mH (respuesta rápida)",
            "Inductor de 1 mH con constante de tiempo breve, empleado en filtrado de alta frecuencia.",
            () => new Inductor("Bobina 1 mH", 0.001)));   // 1 mH
        inductor.Variantes.Add(Variante("lento", "Bobina 100 mH (respuesta media)",
            "Inductor de 100 mH que permite visualizar claramente el crecimiento gradual de la corriente.",
            () => new Inductor("Bobina 100 mH", 0.1)));  // 100 mH
        inductor.Variantes.Add(Variante("grande", "Bobina 1 H (alta inductancia)",
            "Inductor de 1 H con constante de tiempo larga y elevada sobretensión inducida al abrir el circuito.",
            () => new Inductor("Bobina 1 H", 1.0)));
        inductor.Variantes.Add(Variante("sandbox", "Inductor ∞",
            "Inductor de 1 H sin límites de ruptura por sobretensión ni sobrecorriente.",
            () => TodoInfinito(new Inductor("Inductor ∞", 1.0)), sinLimites: true));
        lista.Add(inductor);

        // --- Pilas y baterías nuevas ---
        var pilaAA = new DefinicionTipo
        {
            Id = "pilaaa",
            Nombre = "Pila AA",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Celda cilíndrica estándar de 1.5 V de corriente continua (DC). Para obtener tensiones mayores se conectan varias en serie.",
            RutaModelo = "res://models/pilaAA.glb",
            AlturaObjetivo = 0.28f,
            EscalaBornes = 0.65f,
            AnclaBorneA = new Vector3(0f, 0.99f, 0f),
            AnclaBorneB = new Vector3(0f, 0.02f, 0f),
        };
        pilaAA.Stats.Add(new StatDef("Voltaje", "V", 0.5, 12, 0.5,
            c => ((Battery)c).Voltage, (c, v) => ((Battery)c).Voltage = v)
        { Descripcion = "Fuerza electromotriz (tensión en vacío) de la pila.", Formula = "I = V / R" });
        pilaAA.Stats.Add(new StatDef("Resist. interna", "Ω", 0.05, 50, 0.05,
            c => ((Battery)c).ResistenciaInternaNominal, (c, v) => ((Battery)c).ResistenciaInternaNominal = v)
        { Descripcion = "Resistencia interna propia de la celda. Produce caída de tensión y calentamiento cuando entrega corriente." });
        pilaAA.Stats.AddRange(StatsLimites());
        pilaAA.Stats.Add(new StatDef("Capacidad", "mAh", 10, 100_000, 10,
            c => ((Battery)c).CargaNominal, (c, v) => { var b = (Battery)c; b.CargaNominal = v; b.CargaRestante = Math.Min(b.CargaRestante, v); })
        { Descripcion = "Carga eléctrica almacenada (miliamperios-hora). Define la duración según la corriente consumida." });
        pilaAA.Lecturas.Add(new LecturaDef("Carga restante",
            c => {
                var b = (Battery)c;
                if (double.IsPositiveInfinity(b.CargaNominal)) return "∞  (sin desgaste)";
                string autonomia = double.IsPositiveInfinity(b.AutonomiaHoras)
                    ? "sin consumo"
                    : b.AutonomiaHoras >= 1 ? $"~{b.AutonomiaHoras:F1} h al consumo actual" : $"~{b.AutonomiaHoras * 60:F0} min al consumo actual";
                return $"{b.FraccionCarga:P0}  ({b.CargaRestante:F0} de {b.CargaNominal:F0} mAh · {autonomia})";
            }));
        pilaAA.Lecturas.Add(new LecturaDef("Voltaje real",
            c => { var b = (Battery)c; return $"{b.Voltage:F2} V de {b.VoltajeNominal:F2} V nominales  (R int.: {b.ResistenciaInternaNominal:F2} Ω)"; }));
        pilaAA.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((Battery)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        pilaAA.Variantes.Add(Variante("real", "Pila AA",
            "Pila alcalina de 1.5 V, 2500 mAh y hasta 2 A de corriente pico.",
            () => new Battery("Pila AA", 1.5) { MaxCurrent = 15, CorrientePico = 2, CargaNominal = 2500, CargaRestante = 2500, ResistenciaInternaNominal = 0.3, CapacidadTermica = 1.2 }));
        pilaAA.Variantes.Add(Variante("sandbox", "Pila AA ∞",
            "Pila AA ideal de 1.5 V con carga inagotable y sin límites de ruptura.",
            () => TodoInfinito(new Battery("Pila AA ∞", 1.5) { CargaNominal = double.PositiveInfinity,  ResistenciaInternaNominal = 0.3 }), sinLimites: true));
        lista.Add(pilaAA);

        var pilaAAA = new DefinicionTipo
        {
            Id = "pilaaaa",
            Nombre = "Pila AAA",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Celda cilíndrica compacta de 1.5 V de corriente continua (DC). Tiene menor capacidad y mayor resistencia interna que la pila AA.",
            RutaModelo = "res://models/pilaAAA.glb",
            AlturaObjetivo = 0.24f,
            EscalaBornes = 0.6f,
            AnclaBorneA = new Vector3(0f, 0.99f, 0f),
            AnclaBorneB = new Vector3(0f, 0.02f, 0f),
        };
        pilaAAA.Stats.Add(new StatDef("Voltaje", "V", 0.5, 12, 0.5,
            c => ((Battery)c).Voltage, (c, v) => ((Battery)c).Voltage = v)
        { Descripcion = "Fuerza electromotriz (tensión en vacío) de la pila.", Formula = "I = V / R" });
        pilaAAA.Stats.Add(new StatDef("Resist. interna", "Ω", 0.05, 50, 0.05,
            c => ((Battery)c).ResistenciaInternaNominal, (c, v) => ((Battery)c).ResistenciaInternaNominal = v)
        { Descripcion = "Resistencia interna propia de la celda (mayor que en una AA debido a su menor tamaño)." });
        pilaAAA.Stats.AddRange(StatsLimites());
        pilaAAA.Stats.Add(new StatDef("Capacidad", "mAh", 10, 100_000, 10,
            c => ((Battery)c).CargaNominal, (c, v) => { var b = (Battery)c; b.CargaNominal = v; b.CargaRestante = Math.Min(b.CargaRestante, v); })
        { Descripcion = "Carga eléctrica almacenada (miliamperios-hora). Define la duración según el consumo." });
        pilaAAA.Lecturas.Add(new LecturaDef("Carga restante",
            c => {
                var b = (Battery)c;
                if (double.IsPositiveInfinity(b.CargaNominal)) return "∞  (sin desgaste)";
                string autonomia = double.IsPositiveInfinity(b.AutonomiaHoras)
                    ? "sin consumo"
                    : b.AutonomiaHoras >= 1 ? $"~{b.AutonomiaHoras:F1} h al consumo actual" : $"~{b.AutonomiaHoras * 60:F0} min al consumo actual";
                return $"{b.FraccionCarga:P0}  ({b.CargaRestante:F0} de {b.CargaNominal:F0} mAh · {autonomia})";
            }));
        pilaAAA.Lecturas.Add(new LecturaDef("Voltaje real",
            c => { var b = (Battery)c; return $"{b.Voltage:F2} V de {b.VoltajeNominal:F2} V nominales  (R int.: {b.ResistenciaInternaNominal:F2} Ω)"; }));
        pilaAAA.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((Battery)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        pilaAAA.Variantes.Add(Variante("real", "Pila AAA",
            "Pila alcalina de 1.5 V, 1000 mAh y hasta 1 A de corriente pico.",
            () => new Battery("Pila AAA", 1.5) { MaxCurrent = 10, CorrientePico = 1, CargaNominal = 1000, CargaRestante = 1000, ResistenciaInternaNominal = 0.6, CapacidadTermica = 0.8 }));
        pilaAAA.Variantes.Add(Variante("sandbox", "Pila AAA ∞",
            "Pila AAA ideal de 1.5 V con carga inagotable y sin límites de ruptura.",
            () => TodoInfinito(new Battery("Pila AAA ∞", 1.5) { CargaNominal = double.PositiveInfinity,  ResistenciaInternaNominal = 0.6 }), sinLimites: true));
        lista.Add(pilaAAA);

        var bateriaAuto = new DefinicionTipo
        {
            Id = "bateriaauto",
            Nombre = "Batería de auto",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Batería de plomo-ácido de 12 V con resistencia interna muy baja (0.01 Ω), capaz de entregar corrientes de arranque muy elevadas.",
            RutaModelo = "res://models/bateriaauto.glb",
            AlturaObjetivo = 0.5f,
            EscalaBornes = 1.0f,
            AnclaBorneA = new Vector3(0.370f, 0.976f, 0.228f),
            AnclaBorneB = new Vector3(-0.371f, 0.976f, 0.228f),
        };
        bateriaAuto.Stats.Add(new StatDef("Voltaje", "V", 0.5, 48, 0.5,
            c => ((Battery)c).Voltage, (c, v) => ((Battery)c).Voltage = v)
        { Descripcion = "Fuerza electromotriz (tensión en vacío) de la batería.", Formula = "I = V / R" });
        bateriaAuto.Stats.Add(new StatDef("Resist. interna", "Ω", 0.005, 50, 0.005,
            c => ((Battery)c).ResistenciaInternaNominal, (c, v) => ((Battery)c).ResistenciaInternaNominal = v)
        { Descripcion = "Resistencia interna muy baja (~0.01 Ω), lo que permite entregar cientos de amperios en cortocircuito o arranque." });
        bateriaAuto.Stats.AddRange(StatsLimites());
        bateriaAuto.Stats.Add(new StatDef("Capacidad", "mAh", 10, 100_000, 10,
            c => ((Battery)c).CargaNominal, (c, v) => { var b = (Battery)c; b.CargaNominal = v; b.CargaRestante = Math.Min(b.CargaRestante, v); })
        { Descripcion = "Capacidad nominal de almacenamiento (50 000 mAh = 50 Ah)." });
        bateriaAuto.Lecturas.Add(new LecturaDef("Carga restante",
            c => {
                var b = (Battery)c;
                if (double.IsPositiveInfinity(b.CargaNominal)) return "∞  (sin desgaste)";
                string autonomia = double.IsPositiveInfinity(b.AutonomiaHoras)
                    ? "sin consumo"
                    : b.AutonomiaHoras >= 1 ? $"~{b.AutonomiaHoras:F1} h al consumo actual" : $"~{b.AutonomiaHoras * 60:F0} min al consumo actual";
                return $"{b.FraccionCarga:P0}  ({b.CargaRestante:F0} de {b.CargaNominal:F0} mAh · {autonomia})";
            }));
        bateriaAuto.Lecturas.Add(new LecturaDef("Voltaje real",
            c => { var b = (Battery)c; return $"{b.Voltage:F2} V de {b.VoltajeNominal:F2} V nominales  (R int.: {b.ResistenciaInternaNominal:F2} Ω)"; }));
        bateriaAuto.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Dañada" : ((Battery)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        bateriaAuto.Variantes.Add(Variante("real", "Batería de auto",
            "Batería de 12 V, 50 Ah y 600 A de corriente pico, con gran masa térmica.",
            () => new Battery("Batería de auto", 12) { MaxCurrent = 3000, CorrientePico = 600, CargaNominal = 50_000, CargaRestante = 50_000, ResistenciaInternaNominal = 0.01, CapacidadTermica = 40 }));
        bateriaAuto.Variantes.Add(Variante("sandbox", "Batería de auto ∞",
            "Batería de 12 V ideal con carga inagotable y sin límites de ruptura.",
            () => TodoInfinito(new Battery("Batería de auto ∞", 12) { CargaNominal = double.PositiveInfinity,  ResistenciaInternaNominal = 0.01, CapacidadTermica = 40 }), sinLimites: true));
        lista.Add(bateriaAuto);

        // --- Tomacorriente y adaptadores ---
        var toma = new DefinicionTipo
        {
            Id = "tomacorriente",
            Nombre = "Tomacorriente 220V",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Toma de red eléctrica domiciliaria de 220 V RMS en corriente alterna (AC) a 50 Hz. Incluye protección por sobrecorriente a 25 A.",
            RutaModelo = "res://models/tomacorriente.glb",
            AlturaObjetivo = 0.4f,
            EscalaBornes = 0.8f,
            AnclaBorneA = new Vector3(0.127f, 1.000f, -0.131f),  // fase
            AnclaBorneB = new Vector3(-0.130f, 1.000f, -0.131f),   // neutro
        };
        toma.Stats.Add(new StatDef("Voltaje RMS", "V", 1, 400, 1,
            c => ((FuenteAC)c).VoltajeRMS, (c, v) => ((FuenteAC)c).VoltajeRMS = v)
        { Descripcion = "Tensión eficaz de la red eléctrica (220 V en estándar europeo/sudamericano o 110–120 V en estándar norteamericano)." });
        toma.Stats.Add(new StatDef("Frecuencia", "Hz", 1, 400, 1,
            c => ((FuenteAC)c).Frecuencia, (c, v) => ((FuenteAC)c).Frecuencia = v)
        { Descripcion = "Frecuencia de la red eléctrica (50 Hz o 60 Hz según la región)." });
        toma.Stats.AddRange(StatsLimites());
        toma.Lecturas.Add(new LecturaDef("Estado",
            c => c.IsBurnt ? "Protección disparada (abierto)" : ((FuenteAC)c).EnCortocircuito ? "EN CORTOCIRCUITO" : "Normal"));
        toma.Variantes.Add(Variante("real", "Tomacorriente 220V",
            "Red de 220 V AC a 50 Hz con corte por sobrecorriente a 25 A (equivalente a llave térmica).",
            () => new FuenteAC("Tomacorriente", 220, 50) { MaxCurrent = 25 }));
        toma.Variantes.Add(Variante("sandbox", "Tomacorriente ∞",
            "Fuente de red eléctrica ideal sin corte por sobrecorriente ni temperatura.",
            () => TodoInfinito(new FuenteAC("Tomacorriente ∞", 220, 50)), sinLimites: true));
        lista.Add(toma);

        // ================= MICROCONTROLADORES =================

        var arduino = new DefinicionTipo
        {
            Id = "arduino_uno",
            Nombre = "Arduino UNO",
            Categoria = CategoriaComponente.Microcontroladores,
            Descripcion = "Placa de desarrollo basada en el microcontrolador ATmega328P a 16 MHz. Permite cargar programas en formato Intel HEX (.hex) y utilizar sus pines de alimentación, entrada/salida digital, PWM y entradas analógicas dentro del circuito.",
            RutaModelo = "res://models/Arduino-UNO.glb",
            // El .glb ya viene acostado (eje Y hacia arriba): no necesita rotación base.
            AlturaObjetivo = 0.14f,
            EscalaBornes = 0.165f,

            // Todas las anclas son fracciones de la caja del modelo acostado
            // (X: −0.5 lado USB … +0.5 lado ICSP; Z: −0.5 fila DIGITAL … +0.5
            // fila POWER/ANALOG; Y: 0.7877 = boca de los conectores hembra).
            // Medidas sobre los zócalos reales del modelo (paso de 2.54 mm).
            AnclaBorneA = new Vector3(0.0898f, 0.7877f, 0.4511f),   // 5V
            AnclaBorneB = new Vector3(0.1237f, 0.7877f, 0.4511f),   // GND
            EtiquetaBorneA = "5V",
            EtiquetaBorneB = "GND",
            AnclaLedInterno = new Vector3(-0.02f, 0.145f, -0.32f),  // LED "L" sobre el PCB, junto al pin 13
        };

        // Fila DIGITAL superior (Z = −0.4545). Dos conectores: el de 10 pines
        // (SCL, SDA, AREF, GND, D13…D8) y el de 8 pines (D7…D0), separados
        // por un hueco mayor que el paso normal, como en la placa real.
        const float yZocalo = 0.7877f;
        const float zDigital = -0.4545f;
        const float zPower = 0.4511f;
        float[] xDigital =
        {
            0.4282f, 0.3942f, 0.3603f, 0.3263f, 0.2924f, 0.2585f, 0.2245f, 0.1906f, // D0..D7
            0.1366f, 0.1026f, 0.0687f, 0.0348f, 0.0008f, -0.0331f,                   // D8..D13
        };
        for (int d = 0; d <= 13; d++)
        {
            arduino.AnclasBornesExtra.Add(($"D{d}", new Vector3(xDigital[d], yZocalo, zDigital)));
        }
        arduino.AnclasBornesExtra.Add(("GND3", new Vector3(-0.0671f, yZocalo, zDigital)));
        arduino.AnclasBornesExtra.Add(("AREF", new Vector3(-0.1010f, yZocalo, zDigital)));
        arduino.AnclasBornesExtra.Add(("SDA", new Vector3(-0.1349f, yZocalo, zDigital)));
        arduino.AnclasBornesExtra.Add(("SCL", new Vector3(-0.1689f, yZocalo, zDigital)));

        // Fila POWER inferior (Z = +0.4511): [NC], IOREF, RESET, 3V3, 5V, GND, GND, VIN.
        arduino.AnclasBornesExtra.Add(("IOREF", new Vector3(-0.0120f, yZocalo, zPower)));
        arduino.AnclasBornesExtra.Add(("RESET", new Vector3(0.0219f, yZocalo, zPower)));
        arduino.AnclasBornesExtra.Add(("3V3", new Vector3(0.0558f, yZocalo, zPower)));
        arduino.AnclasBornesExtra.Add(("GND2", new Vector3(0.1577f, yZocalo, zPower)));
        arduino.AnclasBornesExtra.Add(("VIN", new Vector3(0.1916f, yZocalo, zPower)));

        // Fila ANALOG IN inferior (Z = +0.4511): A0..A5.
        float[] xAnalog = { 0.2593f, 0.2931f, 0.3268f, 0.3606f, 0.3944f, 0.4282f };
        for (int a = 0; a <= 5; a++)
        {
            arduino.AnclasBornesExtra.Add(($"A{a}", new Vector3(xAnalog[a], yZocalo, zPower)));
        }

        arduino.Stats.Add(new StatDef("Corriente máx. por pin", "A", 0.005, 1, 0.005,
            c => ((ArduinoUno)c).LimiteCorrientePin, (c, v) => ((ArduinoUno)c).LimiteCorrientePin = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Corriente máxima admisible por cada pin de E/S (~40 mA en el ATmega328P real). Si se supera (por ejemplo, conectando un LED sin resistencia), ese pin se daña de forma permanente.",
        });
        arduino.Stats.Add(new StatDef("Frecuencia del reloj", "MHz", 1, 32, 1,
            c => ((ArduinoUno)c).FrecuenciaMHz, (c, v) => ((ArduinoUno)c).FrecuenciaMHz = v)
        {
            Descripcion = "Frecuencia de reloj del microcontrolador (16 MHz por defecto). Puede reducirse para observar retardos en cámara lenta o aumentarse para acelerar la ejecución.",
        });
        arduino.Stats.Add(new StatDef("Voltaje máx. en VIN", "V", 7, 50, 1,
            c => ((ArduinoUno)c).VoltajeMaximoVin, (c, v) => ((ArduinoUno)c).VoltajeMaximoVin = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Tensión máxima admisible en el pin VIN (rango recomendado: 7 a 12 V; límite absoluto: 20 V). Si se supera, la placa se daña por sobretensión.",
        });
        arduino.Stats.AddRange(StatsLimites());

        arduino.Lecturas.Add(new LecturaDef("Programa",
            c => ((ArduinoUno)c).ProgramaCargado ? ((ArduinoUno)c).InfoPrograma : "— (haz clic en la placa o usa «Cargar programa»)"));
        arduino.Lecturas.Add(new LecturaDef("CPU", c => ((ArduinoUno)c).EstadoCpu));
        arduino.Lecturas.Add(new LecturaDef("Velocidad",
            c => ((ArduinoUno)c).ProgramaCargado ? $"{((ArduinoUno)c).VelocidadReal:P0} del tiempo real" : "—"));
        arduino.Lecturas.Add(new LecturaDef("Alimentación", c => ((ArduinoUno)c).EstadoAlimentacion));
        arduino.Lecturas.Add(new LecturaDef("Regulador 3.3 V", c => ((ArduinoUno)c).EstadoRegulador3V3));
        arduino.Lecturas.Add(new LecturaDef("Referencia ADC", c => ((ArduinoUno)c).ReferenciaAdcTexto));
        arduino.Lecturas.Add(new LecturaDef("Pines dañados", c =>
        {
            var placa = (ArduinoUno)c;
            if (placa.PinesDanadosTotal == 0) return "Ninguno";
            var nombres = new List<string>();
            for (int i = 0; i < ArduinoUno.NombresPines.Length; i++)
            {
                if (placa.PinesDanados[i]) nombres.Add(ArduinoUno.NombresPines[i]);
            }
            return $"{placa.PinesDanadosTotal} ({string.Join(", ", nombres)})";
        }));
        arduino.Lecturas.Add(new LecturaDef("Pines activos", c =>
        {
            var placa = (ArduinoUno)c;
            var lineas = new List<string>();
            for (int i = 0; i < ElectricSim.Core.Arduino.Atmega328P.CantidadPines; i++)
            {
                var estado = placa.EstadoPin(i);
                if (estado.EsSalida || estado.PullupActivo || placa.PinesDanados[i])
                {
                    lineas.Add(placa.DescribirPin(i));
                }
            }
            return lineas.Count == 0 ? "Todos en reposo" : "\n" + string.Join("\n", lineas);
        }));

        arduino.Variantes.Add(Variante("real", "Arduino UNO", "Placa Arduino UNO estándar: reloj de 16 MHz, límite de 40 mA por pin y regulador VIN hasta 20 V.",
            () => new ArduinoUno("Arduino UNO")));
        arduino.Variantes.Add(Variante("sandbox", "Arduino UNO ∞", "Placa Arduino UNO ideal protegida contra sobrecorriente en pines, sobretensión en VIN y daños térmicos.",
            () =>
            {
                var placa = TodoInfinito(new ArduinoUno("Arduino UNO ∞"));
                placa.LimiteCorrientePin = double.PositiveInfinity;
                placa.VoltajeMaximoVin = double.PositiveInfinity;
                return placa;
            }, sinLimites: true));
        // Estado vivo de la placa: programa .hex, pausa de CPU, pines dañados
        // y estado del regulador de 3.3 V viajan con el proyecto.
        arduino.GuardarEstado = (c, e) =>
        {
            var placa = (ArduinoUno)c;
            e.Poner("hex", placa.TextoHexCargado);
            e.Poner("nombreHex", placa.NombrePrograma);
            e.Poner("cpuPausa", placa.CpuEnPausa);
            e.Poner("pinesDanados", placa.MascaraPinesDanados);
            e.Poner("reg3v3Quemado", placa.Regulador3V3Quemado);
            e.Poner("motivoReg3v3", placa.MotivoRegulador3V3);
        };
        arduino.CargarEstado = (c, e) =>
        {
            var placa = (ArduinoUno)c;
            string hex = e.Texto("hex");
            if (hex != "") placa.CargarPrograma(hex, e.Texto("nombreHex", "programa"));
            placa.CpuEnPausa = e.Booleano("cpuPausa", false);
            int danados = (int)e.Numero("pinesDanados", 0);
            if (danados != 0) placa.RestaurarPinesDanados(danados);
            if (e.Booleano("reg3v3Quemado", false))
            {
                placa.RestaurarRegulador3V3Quemado(e.Texto("motivoReg3v3", "cortocircuito"));
            }
        };
        lista.Add(arduino);


        // =================================================================
        //  SEMICONDUCTORES Y CONVERTIDORES (la tanda grande)
        // =================================================================

        // --- Diodo rectificador ---
        var diodo = new DefinicionTipo
        {
            Id = "diodo",
            Nombre = "Diodo",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Semiconductor unidireccional: permite el paso de corriente del Ánodo (A) al Cátodo (K) con una caída directa de ~0.7 V, y bloquea la conducción en sentido inverso.",
            CrearPrimitiva = Primitivas.DiodoRectificador,
            AlturaObjetivo = 0.14f,
            AnclaBorneA = new Vector3(-0.42f, 0.45f, 0f),
            AnclaBorneB = new Vector3(0.42f, 0.45f, 0f),
            EtiquetaBorneA = "A",
            EtiquetaBorneB = "K",
        };
        diodo.Stats.Add(new StatDef("Caída directa", "V", 0.2, 1.2, 0.05,
            c => ((Diodo)c).VoltajeUmbral, (c, v) => ((Diodo)c).VoltajeUmbral = v)
        {
            Descripcion = "Tensión directa mínima necesaria para que el diodo conduzca (~0.7 V en silicio).",
        });
        diodo.Stats.Add(new StatDef("Resist. encendido", "Ω", 0.1, 100, 0.5,
            c => ((Diodo)c).ResistenciaEncendido, (c, v) => ((Diodo)c).ResistenciaEncendido = v)
        {
            Descripcion = "Resistencia dinámica interna del diodo mientras conduce en polarización directa.",
        });
        diodo.Stats.Add(new StatDef("V inverso máx.", "V", 10, 1000, 10,
            c => ((Diodo)c).MaxVoltajeInverso, (c, v) => ((Diodo)c).MaxVoltajeInverso = v)
        {
            Tipo = TipoStat.NumeroConInfinito,
            Descripcion = "Tensión inversa máxima (PIV) que soporta antes de sufrir ruptura por avalancha.",
        });
        diodo.Stats.AddRange(StatsLimites());
        diodo.Lecturas.Add(new LecturaDef("Estado",
            c => ((Diodo)c).ConduciendoDirecta ? "Conduciendo (A→K)" : "Bloqueando"));
        diodo.Variantes.Add(Variante("real", "Diodo (1N4007)",
            "Diodo rectificador de silicio estándar (0.7 V de caída directa, 1 A máximo).",
            () => new Diodo("Diodo")));
        diodo.Variantes.Add(Variante("sandbox", "Diodo ∞",
            "Diodo rectificador ideal sin límites de ruptura directa ni inversa.",
            () => TodoInfinito(new Diodo("Diodo ∞") { MaxVoltajeInverso = double.PositiveInfinity }), sinLimites: true));
        lista.Add(diodo);

        // --- Diodo Zener ---
        var zener = new DefinicionTipo
        {
            Id = "diodo_zener",
            Nombre = "Diodo Zener",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Diodo diseñado para operar en polarización inversa: al alcanzar su tensión Zener (Vz), conduce manteniendo dicha tensión casi constante. Se utiliza como regulador o referencia de tensión junto a una resistencia limitadora en serie.",
            CrearPrimitiva = Primitivas.DiodoZener,
            AlturaObjetivo = 0.14f,
            AnclaBorneA = new Vector3(-0.42f, 0.45f, 0f),
            AnclaBorneB = new Vector3(0.42f, 0.45f, 0f),
            EtiquetaBorneA = "A",
            EtiquetaBorneB = "K",
        };
        zener.Stats.Add(new StatDef("Voltaje Zener", "V", 2, 30, 0.1,
            c => ((Diodo)c).VoltajeZener, (c, v) => ((Diodo)c).VoltajeZener = v)
        {
            Descripcion = "Tensión de regulación en polarización inversa (cátodo K al positivo, ánodo A al negativo).",
            Formula = "V_salida ≈ V_zener (con R limitadora en serie)",
        });
        zener.Stats.Add(new StatDef("Caída directa", "V", 0.2, 1.2, 0.05,
            c => ((Diodo)c).VoltajeUmbral, (c, v) => ((Diodo)c).VoltajeUmbral = v)
        {
            Descripcion = "Caída de tensión cuando se polariza en directa (~0.7 V, como un diodo convencional).",
        });
        zener.Stats.AddRange(StatsLimites());
        zener.Lecturas.Add(new LecturaDef("Estado",
            c => ((Diodo)c).RegulandoZener ? "Regulando en inversa (V = V_zener)"
               : ((Diodo)c).ConduciendoDirecta ? "Conduciendo en directa" : "Bloqueando"));
        zener.Variantes.Add(Variante("real", "Zener 5.1V",
            "Diodo Zener de 5.1 V y 0.5 W de disipación máxima.",
            () => new Diodo("Zener") { VoltajeZener = 5.1, MaxVoltajeInverso = double.PositiveInfinity, MaxPower = 0.5 }));
        zener.Variantes.Add(Variante("sandbox", "Zener ∞",
            "Diodo Zener de 5.1 V sin límites de potencia ni temperatura.",
            () => TodoInfinito(new Diodo("Zener ∞") { VoltajeZener = 5.1, MaxVoltajeInverso = double.PositiveInfinity }), sinLimites: true));
        lista.Add(zener);

        // --- Transistor PNP ---
        var pnp = new DefinicionTipo
        {
            Id = "transistor_pnp",
            Nombre = "Transistor PNP",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Transistor bipolar de unión (BJT) tipo PNP, complementario al NPN. Conduce entre Emisor (E) y Colector (C) cuando la Base (B) tiene una tensión menor que el Emisor en al menos ~0.7 V (corriente saliente de la Base).",
            RutaModelo = "res://models/transistor.glb",
            RotacionBase = new Basis(Vector3.Right, Mathf.Pi / 2f), // el modelo viene acostado: se para con las patas hacia abajo
            AlturaObjetivo = 0.7f,
            AnclaBorneA = new Vector3(0.006f, 0.043f, 0.316f), // C (colector)
            AnclaBorneB = new Vector3(0.292f, 0.043f, 0.316f),  // E (emisor)
            EtiquetaBorneA = "C",
            EtiquetaBorneB = "E",
            EscalaBornes = 0.6f,
        };
        pnp.AnclasBornesExtra.Add(("B", new Vector3(-0.280f, 0.043f, 0.316f))); // base: la pata de la izquierda (B-C-E)
        pnp.Stats.Add(new StatDef("Umbral emisor-base", "V", 0.3, 1.0, 0.05,
            c => ((TransistorPNP)c).VoltajeUmbralBase, (c, v) => ((TransistorPNP)c).VoltajeUmbralBase = v)
        {
            Descripcion = "Tensión mínima entre Emisor y Base (V_EB) requerida para polarizar la juntura de control (~0.7 V en silicio).",
            Formula = "Conduce si V(E) − V(B) ≥ V_EB(umbral)",
        });
        pnp.Stats.Add(new StatDef("Resist. en saturación", "Ω", 0.1, 100, 0.5,
            c => ((TransistorPNP)c).ResistenciaSaturacion, (c, v) => ((TransistorPNP)c).ResistenciaSaturacion = v)
        {
            Descripcion = "Resistencia equivalente entre Emisor y Colector cuando el transistor está saturado.",
        });
        pnp.Stats.Add(new StatDef("Resist. interna de base", "Ω", 1, 1000000, 10,
            c => ((TransistorPNP)c).ResistenciaBase, (c, v) => ((TransistorPNP)c).ResistenciaBase = v)
        {
            Descripcion = "Resistencia serie interna de la juntura Emisor-Base. Requiere resistencia limitadora externa en la Base.",
        });
        pnp.Stats.Add(new StatDef("Resist. de corte (base)", "Ω", 1000, 10000000, 1000,
            c => ((TransistorPNP)c).ResistenciaFugaBase, (c, v) => ((TransistorPNP)c).ResistenciaFugaBase = v)
        {
            Descripcion = "Resistencia de pérdida por debajo del umbral V_EB. Mantiene el transistor en corte si la Base queda flotante (desconectada).",
        });
        pnp.Stats.Add(new StatDef("Corriente mín. de base", "mA", 0.001, 100, 0.01,
            c => ((TransistorPNP)c).CorrienteMinimaBase * 1000,
            (c, v) => ((TransistorPNP)c).CorrienteMinimaBase = v / 1000.0)
        {
            Descripcion = "Corriente saliente de Base mínima necesaria para llevar el transistor a saturación.",
            Formula = "I_B ≥ I_C / hFE",
        });
        pnp.Stats.AddRange(StatsLimites());
        pnp.Lecturas.Add(new LecturaDef("Estado",
            c => ((TransistorPNP)c).Conduciendo ? "Saturación (conduce entre E y C)" : "En corte (abierto entre E y C)"));
        pnp.Lecturas.Add(new LecturaDef("V emisor-base",
            c => $"{((TransistorPNP)c).VoltajeEmisorBase:F2} V"));
        pnp.Lecturas.Add(new LecturaDef("Corriente de base",
            c => $"{((TransistorPNP)c).CorrienteBase * 1000:F2} mA"));
        pnp.Variantes.Add(Variante("real", "Transistor PNP (2N2907)",
            "Transistor PNP de propósito general (V_EB ≈ 0.7 V, potencia máxima 0.5 W).",
            () => new TransistorPNP("Transistor PNP")));
        pnp.Variantes.Add(Variante("sandbox", "Transistor PNP ∞",
            "Transistor PNP ideal sin límites de ruptura por corriente, tensión ni temperatura.",
            () => TodoInfinito(new TransistorPNP("Transistor PNP ∞")), sinLimites: true));
        lista.Add(pnp);

        // --- Capacitor electrolítico (polarizado) ---
        var electrolitico = new DefinicionTipo
        {
            Id = "capacitor_electrolitico",
            Nombre = "Capacitor electrolítico",
            Categoria = CategoriaComponente.Pasivos,
            Descripcion = "Capacitor polarizado de alta capacidad específica. El terminal positivo (+) debe conectarse al potencial más alto; la polarización inversa degrada el dieléctrico y daña el componente.",
            RutaModelo = "res://models/capacitor.glb",
            AlturaObjetivo = 0.6f,
            EscalaBornes = 0.6f,
            AnclaBorneA = new Vector3(0.007f, 0.050f, 0.227f),
            AnclaBorneB = new Vector3(0.005f, 0.118f, -0.164f),
            EtiquetaBorneA = "+",
            EtiquetaBorneB = "−",
        };
        electrolitico.Stats.Add(new StatDef("Capacitancia", "F", 1e-7, 10, 1e-7,
            c => ((Capacitor)c).Capacitance, (c, v) => ((Capacitor)c).Capacitance = v)
        {
            Descripcion = "Capacidad de almacenamiento de carga eléctrica en microfaradios (µF).",
            Escala = 1e6, UnidadVisual = "µF",
        });
        electrolitico.Stats.Add(new StatDef("Tolerancia inversa", "V", 0.5, 5, 0.1,
            c => ((CapacitorElectrolitico)c).ToleranciaInversa, (c, v) => ((CapacitorElectrolitico)c).ToleranciaInversa = v)
        {
            Descripcion = "Tensión inversa máxima tolerable antes de iniciar la descomposición química del electrolito.",
        });
        electrolitico.Stats.AddRange(StatsLimites());
        electrolitico.Lecturas.Add(new LecturaDef("Tensión almacenada",
            c => DescribirCargaCapacitor((Capacitor)c)));
        electrolitico.Lecturas.Add(new LecturaDef("Estado",
            c => EstadoCapacitor((Capacitor)c)));
        electrolitico.Lecturas.Add(new LecturaDef("Energía almacenada",
            c => $"{((Capacitor)c).EnergiaAlmacenada:F3} J  (½·C·V²)"));
        electrolitico.Lecturas.Add(new LecturaDef("Polaridad",
            c => ((CapacitorElectrolitico)c).InversionEfectiva < -((CapacitorElectrolitico)c).ToleranciaInversa
                ? $"INVERTIDA ({((CapacitorElectrolitico)c).InversionEfectiva:F2} V) — degradación del electrolito"
                : "Correcta"));
        electrolitico.Variantes.Add(Variante("real", "Capacitor electrolítico",
            "Capacitor electrolítico polarizado de 50 mF y 25 V nominales.",
            () => new CapacitorElectrolitico("Electrolítico", 0.05)));
        electrolitico.Variantes.Add(Variante("sandbox", "Capacitor electrolítico ∞",
            "Sin límites de sobretensión directa ni temperatura (mantiene la sensibilidad a polaridad inversa).",
            () => TodoInfinito(new CapacitorElectrolitico("Electrolítico ∞", 0.05)), sinLimites: true));
        lista.Add(electrolitico);

        var cajaPantalla = new DefinicionTipo
        {
            Id = "caja_pantalla",
            Nombre = "Caja con pantalla",
            Categoria = CategoriaComponente.Control,
            Descripcion = "Instrumento de paso ideal: la corriente entra por el lado positivo (+) y sale idéntica por el lado negativo (−), sin caída ni pérdidas, mostrando en pantalla Voltaje, Amperaje, Temperatura y todos los valores de Corriente Alterna.",
            RutaModelo = "res://models/cajaconpantalla.glb",
            // En cajaconpantalla.glb el signo '+' rojo está en Z = -1 y el '−' azul en Z = +1.
            // Con +90° en Y, el '+' queda a la izquierda (X = -0.5) y el '−' a la derecha (X = +0.5).
            // Los dos conectores negros laterales tienen su centro en Y_frac = 0.358.
            RotacionBase = new Basis(Vector3.Up, Mathf.Pi / 2f),
            AlturaObjetivo = 0.38f,
            EscalaBornes = 0.7f,
            AnclaBorneA = new Vector3(-0.500f, 0.358f, 0f),
            AnclaBorneB = new Vector3(0.500f, 0.358f, 0f),
            EtiquetaBorneA = "+",
            EtiquetaBorneB = "−",
        };
        cajaPantalla.Lecturas.Add(new LecturaDef("Voltaje medido",
            c => CajaConPantalla.FormatearVoltaje(((CajaConPantalla)c).VoltajeMedido)));
        cajaPantalla.Lecturas.Add(new LecturaDef("Amperaje (entrada + = salida −)",
            c => CajaConPantalla.FormatearCorriente(Math.Abs(c.Current))));
        cajaPantalla.Lecturas.Add(new LecturaDef("Temperatura",
            c => SimuladorTermico.Habilitada ? $"{c.Temperatura:F1} °C" : "-"));
        cajaPantalla.Lecturas.Add(new LecturaDef("¿Es corriente alterna?",
            c =>
            {
                var cp = (CajaConPantalla)c;
                if (cp.EsCorrienteAlterna) return $"Sí  ({cp.FrecuenciaHz:F0} Hz)";
                return cp.TipoCorrienteRecibida == TipoCorriente.DC ? "No  (corriente continua DC)" : "No";
            }));
        cajaPantalla.Lecturas.Add(new LecturaDef("Valores AC (tensión)",
            c =>
            {
                var cp = (CajaConPantalla)c;
                return cp.EsCorrienteAlterna
                    ? $"Vrms = {cp.VoltajeRms:F2} V   ·   Vpico = {cp.VoltajePico:F2} V"
                    : "-";
            }));
        cajaPantalla.Lecturas.Add(new LecturaDef("Valores AC (corriente)",
            c =>
            {
                var cp = (CajaConPantalla)c;
                return cp.EsCorrienteAlterna
                    ? $"Irms = {CajaConPantalla.FormatearCorriente(cp.CorrienteRms)}   ·   Ipico = {CajaConPantalla.FormatearCorriente(cp.CorrientePico)}"
                    : "-";
            }));
        cajaPantalla.Lecturas.Add(new LecturaDef("Valores AC (fase y FP)",
            c =>
            {
                var cp = (CajaConPantalla)c;
                return cp.EsCorrienteAlterna
                    ? $"Desfase φ = {cp.FaseGrados:F1}°   ·   cos φ = {cp.FactorPotencia:F2}"
                    : "-";
            }));
        cajaPantalla.Lecturas.Add(new LecturaDef("Valores AC (potencias)",
            c =>
            {
                var cp = (CajaConPantalla)c;
                return cp.EsCorrienteAlterna
                    ? $"P = {CajaConPantalla.FormatearPotencia(cp.PotenciaActiva)}   ·   S = {cp.PotenciaAparente:F2} VA   ·   Q = {cp.PotenciaReactiva:F2} VAr"
                    : "-";
            }));
        cajaPantalla.Variantes.Add(Variante("ideal", "Caja con pantalla",
            "Pasa toda la corriente y el voltaje del borne + al borne − sin alterarlos y muestra todas las mediciones en vivo.",
            () => TodoInfinito(new CajaConPantalla("Caja con pantalla")), sinLimites: true));
        lista.Add(cajaPantalla);

        // --- Transformador AC→DC (fuente rectificada aislada) ---
        var trafoAcDc = new DefinicionTipo
        {
            Id = "transformador_ac_dc",
            Nombre = "Transformador AC→DC",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Convierte corriente alterna de entrada (AC1/AC2) en corriente continua de salida (+/−) mediante etapa transformadora, puente rectificador y filtrado, conservando la potencia según su eficiencia (η). Con corriente continua en la entrada primaria no induce y se quema por sobrecorriente.",
            RutaModelo = "res://models/transformador_ac_dc.glb",
            CrearPrimitiva = Primitivas.TransformadorAcDc,
            AlturaObjetivo = 0.36f,
            AnclaBorneA = new Vector3(0.429f, 0.389f, 0.286f),
            AnclaBorneB = new Vector3(0.429f, 0.389f, -0.286f),
            EtiquetaBorneA = "+",
            EtiquetaBorneB = "−",
            EscalaBornes = 0.78f,
        };
        trafoAcDc.AnclasBornesExtra.Add(("AC1", new Vector3(-0.429f, 0.389f, 0.286f)));
        trafoAcDc.AnclasBornesExtra.Add(("AC2", new Vector3(-0.429f, 0.389f, -0.286f)));
        trafoAcDc.Stats.Add(new StatDef("Voltaje de salida", "V", 3, 48, 0.5,
            c => ((TransformadorAcDc)c).VoltajeSalidaNominal, (c, v) => ((TransformadorAcDc)c).VoltajeSalidaNominal = v)
        {
            Descripcion = "Tensión continua (DC) nominal entregada entre + y − cuando la entrada recibe su tensión alterna nominal.",
            Formula = "V_DC = V_AC · (V_salida / V_AC_nom)",
        });
        trafoAcDc.Stats.Add(new StatDef("Voltaje AC nominal", "V", 12, 240, 1,
            c => ((TransformadorAcDc)c).VoltajeEntradaNominal, (c, v) => ((TransformadorAcDc)c).VoltajeEntradaNominal = v)
        {
            Descripcion = "Tensión alterna RMS nominal esperada en los bornes AC1 y AC2.",
        });
        trafoAcDc.Stats.Add(new StatDef("Eficiencia (η)", "", 0.5, 0.98, 0.01,
            c => ((TransformadorAcDc)c).Eficiencia, (c, v) => ((TransformadorAcDc)c).Eficiencia = v)
        {
            Descripcion = "Fracción de la potencia de entrada que se transfiere a la salida DC; el resto se disipa como calor.",
            Formula = "P_in = P_out / η",
        });
        trafoAcDc.Stats.AddRange(StatsLimites());
        trafoAcDc.Lecturas.Add(new LecturaDef("Estado",
            c =>
            {
                var t = (TransformadorAcDc)c;
                if (t.IsBurnt) return $"Dañado ({t.MotivoQuemado})";
                if (t.DcEnPrimario) return "Corriente continua en primario (sobrecalentamiento)";
                return t.Encendido ? "Transformando AC → DC (salida activa)" : "En reposo (sin alterna en AC1/AC2)";
            }));
        trafoAcDc.Lecturas.Add(new LecturaDef("Entrada → salida",
            c => { var t = (TransformadorAcDc)c; return $"{t.VoltajeEntrada:F1} V~  →  {t.VoltajeEnBornes:F2} V="; }));
        trafoAcDc.Lecturas.Add(new LecturaDef("Corriente primario / secundario",
            c => { var t = (TransformadorAcDc)c; return $"{t.CorrienteEntrada * 1000:F1} mA~  /  {Math.Abs(t.Current) * 1000:F1} mA="; }));
        trafoAcDc.Variantes.Add(Variante("v220a12", "Transformador 220V~ → 12V=",
            "Fuente transformadora AC→DC de 220 V~ a 12 V= con puente rectificador y filtro.",
            () => new TransformadorAcDc("Trafo AC→DC 12V") { VoltajeEntradaNominal = 220, VoltajeSalidaNominal = 12 }));
        trafoAcDc.Variantes.Add(Variante("v220a5", "Transformador 220V~ → 5V=",
            "Fuente transformadora AC→DC de 220 V~ a 5 V= para electrónica digital.",
            () => new TransformadorAcDc("Trafo AC→DC 5V") { VoltajeEntradaNominal = 220, VoltajeSalidaNominal = 5 }));
        trafoAcDc.Variantes.Add(Variante("sandbox", "Transformador AC→DC ∞",
            "Transformador AC→DC ideal sin límites de ruptura por corriente, potencia ni temperatura.",
            () => TodoInfinito(new TransformadorAcDc("Trafo AC→DC ∞") { VoltajeEntradaNominal = 220, VoltajeSalidaNominal = 12 }), sinLimites: true));
        lista.Add(trafoAcDc);

        // --- Inversor (DC→AC) ---
        var inversor = new DefinicionTipo
        {
            Id = "inversor",
            Nombre = "Inversor DC→AC",
            Categoria = CategoriaComponente.Fuentes,
            Descripcion = "Convierte corriente continua de entrada (IN+/IN−) en corriente alterna RMS de salida (F~/N~), conservando la potencia según su eficiencia (η), con consumo en vacío y corte automático por baja tensión o polaridad invertida.",
            RutaModelo = "res://models/inversor_dc_ac.glb",
            CrearPrimitiva = Primitivas.InversorDcAc,
            AlturaObjetivo = 0.34f,
            AnclaBorneA = new Vector3(0.430f, 0.469f, 0.286f),
            AnclaBorneB = new Vector3(0.430f, 0.469f, -0.286f),
            EtiquetaBorneA = "F~",
            EtiquetaBorneB = "N~",
            EscalaBornes = 0.78f,
        };
        inversor.AnclasBornesExtra.Add(("IN+", new Vector3(-0.430f, 0.469f, 0.286f)));
        inversor.AnclasBornesExtra.Add(("IN−", new Vector3(-0.430f, 0.469f, -0.286f)));
        inversor.Stats.Add(new StatDef("Voltaje de salida", "V", 110, 240, 5,
            c => ((Inversor)c).VoltajeSalidaRMS, (c, v) => ((Inversor)c).VoltajeSalidaRMS = v)
        {
            Descripcion = "Tensión eficaz (RMS) de corriente alterna generada en los bornes F~ y N~.",
        });
        inversor.Stats.Add(new StatDef("Frecuencia", "Hz", 50, 60, 10,
            c => ((Inversor)c).FrecuenciaSalida, (c, v) => ((Inversor)c).FrecuenciaSalida = v)
        {
            Descripcion = "Frecuencia de la onda alterna de salida (50 Hz o 60 Hz).",
        });
        inversor.Stats.Add(new StatDef("V mínimo de entrada", "V", 3, 20, 0.5,
            c => ((Inversor)c).VoltajeMinimoEntrada, (c, v) => ((Inversor)c).VoltajeMinimoEntrada = v)
        {
            Descripcion = "Umbral de corte por baja tensión DC para proteger la batería de entrada.",
        });
        inversor.Stats.Add(new StatDef("Eficiencia (η)", "", 0.5, 0.98, 0.01,
            c => ((Inversor)c).Eficiencia, (c, v) => ((Inversor)c).Eficiencia = v)
        {
            Descripcion = "Fracción de la potencia de entrada DC transferida a la salida AC.",
            Formula = "I_in = P_out / (η · V_in) + I_standby",
        });
        inversor.Stats.AddRange(StatsLimites());
        inversor.Lecturas.Add(new LecturaDef("Estado",
            c =>
            {
                var inv = (Inversor)c;
                if (inv.IsBurnt) return $"Dañado ({inv.MotivoQuemado})";
                if (inv.EntradaIncompatibleAC) return "Entrada incompatible (requiere DC)";
                if (inv.PolaridadInvertida) return "Polaridad invertida en entrada DC";
                return inv.Encendido ? "Encendido (invirtiendo DC → AC)" : "Apagado (tensión DC insuficiente)";
            }));
        inversor.Lecturas.Add(new LecturaDef("Entrada → salida",
            c => { var inv = (Inversor)c; return $"{inv.VoltajeEntrada:F1} V=  →  {inv.VoltajeEnBornes:F0} V~ ({inv.FrecuenciaSalida:F0} Hz)"; }));
        inversor.Lecturas.Add(new LecturaDef("Corriente entrada / salida",
            c => { var inv = (Inversor)c; return $"{inv.CorrienteEntrada:F2} A=  /  {Math.Abs(inv.Current) * 1000:F0} mA~"; }));
        inversor.Variantes.Add(Variante("real", "Inversor 12V= → 220V~",
            "Inversor de 220 V~ RMS (50 Hz) desde entrada de 12 V=, con 85 % de eficiencia y corte bajo 10 V.",
            () => new Inversor("Inversor")));
        inversor.Variantes.Add(Variante("sandbox", "Inversor DC→AC ∞",
            "Inversor ideal sin límites de ruptura por corriente, potencia ni temperatura.",
            () => TodoInfinito(new Inversor("Inversor ∞")), sinLimites: true));
        lista.Add(inversor);

        foreach (var def in lista)
        {
            if (def.Id == "arduino_uno") continue;
            AsegurarConfiguracionElectrica(def);
        }

        return lista;
    }

    /// <summary>
    /// Etiqueta los límites eléctricos compartidos como parte de la tarjeta simplificada
    /// (voltaje/amperaje/watts mínimos y máximos) y agrega los mínimos faltantes para que
    /// todo componente del catálogo — excepto Arduino UNO — permita configurarlos y guardarlos.
    /// </summary>
    private static void AsegurarConfiguracionElectrica(DefinicionTipo def)
    {
        bool tieneVMin = false;
        bool tieneIMin = false;
        bool tieneVMax = false;
        bool tieneIMax = false;
        bool tienePMax = false;
        bool tieneTMax = false;

        foreach (var stat in def.Stats)
        {
            switch (stat.Etiqueta)
            {
                case "Voltaje mín.":
                    tieneVMin = true;
                    stat.Grupo = GrupoStat.ConfiguracionElectrica;
                    break;
                case "Corriente mín.":
                    tieneIMin = true;
                    stat.Grupo = GrupoStat.ConfiguracionElectrica;
                    break;
                case "Voltaje máx.":
                    tieneVMax = true;
                    stat.Grupo = GrupoStat.ConfiguracionElectrica;
                    break;
                case "Corriente máx.":
                case "Corriente de corte":
                    tieneIMax = true;
                    stat.Grupo = GrupoStat.ConfiguracionElectrica;
                    break;
                case "Potencia máx.":
                    tienePMax = true;
                    stat.Grupo = GrupoStat.ConfiguracionElectrica;
                    break;
                case "Temperatura máx.":
                    tieneTMax = true;
                    break;
            }
        }

        if (!tieneVMin)
        {
            def.Stats.Add(new StatDef("Voltaje mín.", "V", 0, 1000, 0.01,
                c => c.MinVoltage, (c, v) => c.MinVoltage = Math.Max(0, v))
            {
                Grupo = GrupoStat.ConfiguracionElectrica,
                Descripcion = "Voltaje mínimo necesario para que el componente funcione (0 = sin mínimo).",
            });
        }
        if (!tieneIMin)
        {
            def.Stats.Add(new StatDef("Corriente mín.", "A", 0, 100, 0.001,
                c => c.MinCurrent, (c, v) => c.MinCurrent = Math.Max(0, v))
            {
                Grupo = GrupoStat.ConfiguracionElectrica,
                Descripcion = "Amperaje mínimo necesario para que el componente funcione (0 = sin mínimo).",
            });
        }

        if (!tieneVMax || !tieneIMax || !tienePMax || !tieneTMax)
        {
            foreach (var lim in StatsLimites())
            {
                if (lim.Etiqueta == "Voltaje máx." && !tieneVMax)
                {
                    lim.Grupo = GrupoStat.ConfiguracionElectrica;
                    def.Stats.Add(lim);
                }
                else if (lim.Etiqueta == "Corriente máx." && !tieneIMax)
                {
                    lim.Grupo = GrupoStat.ConfiguracionElectrica;
                    def.Stats.Add(lim);
                }
                else if (lim.Etiqueta == "Potencia máx." && !tienePMax)
                {
                    lim.Grupo = GrupoStat.ConfiguracionElectrica;
                    def.Stats.Add(lim);
                }
                else if (lim.Etiqueta == "Temperatura máx." && !tieneTMax)
                {
                    def.Stats.Add(lim);
                }
            }
        }
    }

    private static DefinicionTipo CrearTipoMotor(string id, string nombre, string descripcion,
        Func<Node3D> primitiva, float alturaObjetivo, Vector3 anclaA, Vector3 anclaB, float escalaBornes = 1f)
    {
        var def = new DefinicionTipo
        {
            Id = id,
            Nombre = nombre,
            Categoria = CategoriaComponente.Control,
            Descripcion = descripcion,
            CrearPrimitiva = primitiva,
            AlturaObjetivo = alturaObjetivo,
            AnclaBorneA = anclaA,
            AnclaBorneB = anclaB,
            EtiquetaBorneA = "+",
            EtiquetaBorneB = "−",
            EscalaBornes = escalaBornes,
        };
        def.Stats.Add(new StatDef("Resist. del bobinado", "Ω", 1, 500, 1,
            c => ((Motor)c).ResistenciaBobinado, (c, v) => ((Motor)c).ResistenciaBobinado = v)
        {
            Descripcion = "Resistencia óhmica del devanado del rotor. Limita la corriente de arranque cuando el motor aún está detenido (FCEM = 0 V).",
            Formula = "I = (V − V_fcem) / R_bobinado",
        });
        def.Stats.Add(new StatDef("Constante k (V·s/rad)", "", 0.005, 0.5, 0.005,
            c => ((Motor)c).ConstanteFcem,
            (c, v) => { var m = (Motor)c; m.ConstanteFcem = v; m.ConstantePar = v; })
        {
            Descripcion = "Constante electromecánica del motor: relaciona tanto la fuerza contraelectromotriz con la velocidad angular como el par motor con la corriente.",
            Formula = "V_fcem = k·ω   ·   Par = k·I",
        });
        def.Stats.Add(new StatDef("Par de carga", "N·m", 0, 0.05, 0.001,
            c => ((Motor)c).ParCarga, (c, v) => ((Motor)c).ParCarga = v)
        {
            Descripcion = "Resistencia mecánica aplicada sobre el eje. Al aumentar el par de carga, el motor reduce su velocidad, disminuye su FCEM y demanda más corriente de la fuente.",
            Formula = "dω/dt = (k·I − Par_carga − B·ω) / J",
        });
        def.Stats.Add(new StatDef("Momento de inercia", "kg·m²", 1e-6, 1e-3, 1e-6,
            c => ((Motor)c).Inercia, (c, v) => ((Motor)c).Inercia = v)
        { Descripcion = "Inercia rotacional del rotor y su carga acoplada. A mayor inercia, más tiempo tarda el motor en acelerar hasta su velocidad de régimen." });
        def.Stats.AddRange(StatsLimites());
        def.Lecturas.Add(new LecturaDef("FCEM inducida",
            c => { var m = (Motor)c; return $"{m.OffsetVoltaje:F2} V  (se opone a la tensión de alimentación)"; }));
        def.Lecturas.Add(new LecturaDef("Par y potencia mecánica",
            c => { var m = (Motor)c; return $"{m.Par * 1000:F2} mN·m  ·  {m.PotenciaMecanica:F2} W mecánicos"; }));
        def.Lecturas.Add(new LecturaDef("Velocidad y sentido", c =>
        {
            var m = (Motor)c;
            if (!m.EstaGirando) return "Detenido (0 RPM)";
            return m.RPM > 0 ? $"Horario (→) · {m.RPM:F0} RPM" : $"Antihorario (←) · {Math.Abs(m.RPM):F0} RPM";
        }));
        return def;
    }
}

/// <summary>
/// Modelos hechos con primitivas de Godot para los componentes que todavía
/// no tienen su .glb propio. El día que exista el modelo real, alcanza con
/// poner RutaModelo en el catálogo y borrar la primitiva — nada más cambia.
/// </summary>
public static class Primitivas
{
    private static MeshInstance3D Pieza(Mesh mesh, Color color, Vector3 posicion,
        float rugosidad = 0.6f, float metalico = 0f, bool emisivo = false)
    {
        return new MeshInstance3D
        {
            Mesh = mesh,
            Position = posicion,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = rugosidad,
                Metallic = metalico,
                EmissionEnabled = emisivo,
                Emission = color,
                EmissionEnergyMultiplier = emisivo ? 0.4f : 0f,
            },
        };
    }

    public static Node3D FuenteAC()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.5f, 0.38f, 0.3f) },
            new Color(0.22f, 0.24f, 0.28f), new Vector3(0, 0.19f, 0)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.42f, 0.26f, 0.02f) },
            new Color(0.1f, 0.11f, 0.13f), new Vector3(0, 0.2f, 0.155f)));
        var etiqueta = new Label3D
        {
            Text = "~ AC",
            FontSize = 96,
            PixelSize = 0.0016f,
            Modulate = new Color(0.95f, 0.8f, 0.3f),
            Position = new Vector3(0, 0.2f, 0.17f),
        };
        raiz.AddChild(etiqueta);
        // Dos torretas donde asientan los bornes de fase y neutro.
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.06f },
            Colores.Fase, new Vector3(-0.12f, 0.41f, 0), metalico: 0.5f));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.06f },
            Colores.Neutro, new Vector3(0.12f, 0.41f, 0), metalico: 0.5f));
        return raiz;
    }

    public static Node3D Lampara()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 0.12f },
            new Color(0.75f, 0.75f, 0.78f), new Vector3(0, 0.06f, 0), rugosidad: 0.3f, metalico: 0.8f));
        // La ampolla se llama "Bulbo": ComponenteVisual la busca por nombre
        // para hacerla brillar según la potencia real.
        var bulbo = Pieza(new SphereMesh { Radius = 0.13f, Height = 0.26f },
            new Color(1f, 0.93f, 0.75f, 0.85f), new Vector3(0, 0.24f, 0), rugosidad: 0.1f);
        bulbo.Name = "Bulbo";
        ((StandardMaterial3D)bulbo.MaterialOverride!).Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        raiz.AddChild(bulbo);
        return raiz;
    }

    public static Node3D Capacitor()
    {
        var raiz = new Node3D();
        // Patas
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.1f },
            new Color(0.75f, 0.75f, 0.78f), new Vector3(-0.05f, 0.05f, 0), metalico: 0.8f));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.1f },
            new Color(0.75f, 0.75f, 0.78f), new Vector3(0.05f, 0.05f, 0), metalico: 0.8f));
        // Cuerpo electrolítico
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.26f },
            new Color(0.12f, 0.2f, 0.45f), new Vector3(0, 0.23f, 0)));
        // Franja del lado negativo
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.03f, 0.24f, 0.004f) },
            new Color(0.85f, 0.85f, 0.88f), new Vector3(0.055f, 0.23f, 0.079f)));
        return raiz;
    }

    public static Node3D Pulsador()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.3f, 0.1f, 0.3f) },
            new Color(0.25f, 0.26f, 0.3f), new Vector3(0, 0.05f, 0)));
        // El cilindro rojo se llama "Boton": se hunde mientras está presionado.
        var boton = Pieza(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.09f, Height = 0.1f },
            new Color(0.8f, 0.15f, 0.12f), new Vector3(0, 0.14f, 0), rugosidad: 0.35f);
        boton.Name = "Boton";
        raiz.AddChild(boton);
        return raiz;
    }

    public static Node3D Fusible()
    {
        var raiz = new Node3D();
        var vidrio = Pieza(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.3f },
            new Color(0.85f, 0.9f, 0.95f, 0.35f), new Vector3(0, 0.06f, 0), rugosidad: 0.05f);
        ((StandardMaterial3D)vidrio.MaterialOverride!).Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        vidrio.RotationDegrees = new Vector3(0, 0, 90); // cilindro acostado
        raiz.AddChild(vidrio);
        // El alambre interno se llama "Filamento": desaparece al quemarse.
        var filamento = Pieza(new BoxMesh { Size = new Vector3(0.26f, 0.012f, 0.012f) },
            new Color(0.9f, 0.75f, 0.4f), new Vector3(0, 0.06f, 0), metalico: 0.9f, emisivo: true);
        filamento.Name = "Filamento";
        raiz.AddChild(filamento);
        // Tapas metálicas
        foreach (float x in new[] { -0.16f, 0.16f })
        {
            var tapa = Pieza(new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.06f },
                new Color(0.75f, 0.75f, 0.78f), new Vector3(x, 0.06f, 0), rugosidad: 0.25f, metalico: 0.9f);
            tapa.RotationDegrees = new Vector3(0, 0, 90);
            raiz.AddChild(tapa);
        }
        return raiz;
    }

    // ---------------------------------------------------------------
    //  Primitivas provisorias del pedido nuevo (a la espera de sus .glb)
    // ---------------------------------------------------------------

    public static Node3D Potenciometro()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.34f, 0.12f, 0.30f) },
            new Color(0.2f, 0.22f, 0.26f), new Vector3(0, 0.06f, -0.02f)));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.02f },
            MetalPelado, new Vector3(0, 0.125f, -0.02f), rugosidad: 0.3f, metalico: 0.85f));
        // La perilla se llama "Perilla": la capa visual la gira según Perilla (0..100%).
        var perilla = Pieza(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.1f, Height = 0.1f },
            new Color(0.85f, 0.55f, 0.15f), new Vector3(0, 0.18f, -0.02f), rugosidad: 0.4f);
        perilla.Name = "Perilla";
        // Marca blanca hija de Perilla apuntando a −Z (12 en punto al 50%).
        perilla.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.016f, 0.018f, 0.075f) },
            new Color(0.96f, 0.96f, 0.96f), new Vector3(0, 0.054f, -0.042f)));
        raiz.AddChild(perilla);
        // Tres pines metálicos frontales (A, W, B) alineados con las anclas del catálogo.
        foreach (float x in new[] { -0.11f, 0f, 0.11f })
        {
            raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.024f, 0.018f, 0.10f) },
                MetalPelado, new Vector3(x, 0.03f, 0.18f), rugosidad: 0.25f, metalico: 0.95f));
        }
        return raiz;
    }

    public static Node3D Motor()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.3f, 0.06f, 0.24f) },
            new Color(0.2f, 0.22f, 0.26f), new Vector3(0, 0.03f, 0)));
        var cuerpo = Pieza(new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 0.3f },
            new Color(0.6f, 0.62f, 0.66f), new Vector3(0, 0.17f, 0), rugosidad: 0.3f, metalico: 0.85f);
        cuerpo.RotationDegrees = new Vector3(0, 0, 90); // acostado
        raiz.AddChild(cuerpo);
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.01f, 0.04f, 0.04f) },
            Colores.Positivo, new Vector3(-0.145f, 0.17f, 0.05f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.01f, 0.04f, 0.04f) },
            Colores.Negativo, new Vector3(-0.145f, 0.17f, -0.05f)));

        // Pivote "Eje" centrado sobre el eje X con leva naranja y punta blanca para distinguir RPM y sentido.
        var eje = new Node3D { Name = "Eje", Position = new Vector3(0.22f, 0.17f, 0f) };
        var varilla = Pieza(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.14f },
            new Color(0.85f, 0.85f, 0.9f), Vector3.Zero, rugosidad: 0.2f, metalico: 1f);
        varilla.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(varilla);

        var collar = Pieza(new CylinderMesh { TopRadius = 0.052f, BottomRadius = 0.052f, Height = 0.022f },
            new Color(0.14f, 0.16f, 0.20f), new Vector3(0.042f, 0f, 0f), rugosidad: 0.4f);
        collar.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(collar);

        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.018f, 0.145f, 0.022f) },
            new Color(0.95f, 0.45f, 0.08f), new Vector3(0.044f, 0f, 0f), rugosidad: 0.35f));
        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.020f, 0.040f, 0.026f) },
            new Color(0.96f, 0.96f, 0.96f), new Vector3(0.045f, 0.055f, 0f), rugosidad: 0.35f));
        raiz.AddChild(eje);
        return raiz;
    }

    public static Node3D MotorVentilador()
    {
        var raiz = new Node3D();
        // Base aislante y cuello de soporte.
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.28f, 0.05f, 0.28f) },
            new Color(0.16f, 0.18f, 0.22f), new Vector3(0f, 0.025f, 0f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.10f, 0.14f, 0.10f) },
            new Color(0.22f, 0.24f, 0.28f), new Vector3(-0.04f, 0.12f, 0f)));
        // Cuerpo azul acero del motor.
        var cuerpo = Pieza(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.26f },
            new Color(0.18f, 0.38f, 0.62f), new Vector3(-0.04f, 0.22f, 0f), rugosidad: 0.35f, metalico: 0.55f);
        cuerpo.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(cuerpo);
        // Borneras traseras (+ roja / − negra).
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.04f, 0.03f, 0.04f) },
            Colores.Positivo, new Vector3(-0.14f, 0.055f, 0.08f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.04f, 0.03f, 0.04f) },
            Colores.Negativo, new Vector3(-0.14f, 0.055f, -0.08f)));

        // Eje y hélice de 3 aspas (una naranja de referencia + dos celestes).
        var eje = new Node3D { Name = "Eje", Position = new Vector3(0.15f, 0.22f, 0f) };
        var varilla = Pieza(new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.12f },
            MetalPelado, new Vector3(-0.01f, 0f, 0f), rugosidad: 0.2f, metalico: 0.95f);
        varilla.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(varilla);

        var buje = Pieza(new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.048f, Height = 0.05f },
            new Color(0.12f, 0.13f, 0.16f), new Vector3(0.03f, 0f, 0f), rugosidad: 0.35f);
        buje.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(buje);

        for (int i = 0; i < 3; i++)
        {
            var brazo = new Node3D { RotationDegrees = new Vector3(i * 120f, 0f, 0f) };
            Color colorAspa = i == 0
                ? new Color(0.96f, 0.50f, 0.08f)
                : new Color(0.26f, 0.58f, 0.80f);
            var pala = Pieza(new BoxMesh { Size = new Vector3(0.014f, 0.175f, 0.055f) },
                colorAspa, new Vector3(0.028f, 0.125f, 0f), rugosidad: 0.35f);
            pala.RotationDegrees = new Vector3(0f, 22f, 0f);
            brazo.AddChild(pala);
            eje.AddChild(brazo);
        }
        raiz.AddChild(eje);
        return raiz;
    }

    public static Node3D MotorReductor()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.42f, 0.03f, 0.18f) },
            new Color(0.14f, 0.16f, 0.18f), new Vector3(-0.06f, 0.015f, 0f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.40f, 0.06f, 0.12f) },
            new Color(0.20f, 0.22f, 0.25f), new Vector3(-0.06f, 0.06f, 0f)));
        // Caja reductora amarilla + motor metálico trasero.
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.24f, 0.14f, 0.16f) },
            new Color(0.94f, 0.74f, 0.10f), new Vector3(0.03f, 0.15f, 0f), rugosidad: 0.4f));
        var cuerpo = Pieza(new CylinderMesh { TopRadius = 0.065f, BottomRadius = 0.065f, Height = 0.18f },
            new Color(0.72f, 0.74f, 0.78f), new Vector3(-0.18f, 0.15f, 0f), rugosidad: 0.25f, metalico: 0.9f);
        cuerpo.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(cuerpo);
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.02f, 0.03f, 0.02f) },
            Colores.Positivo, new Vector3(-0.275f, 0.15f, 0.05f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.02f, 0.03f, 0.02f) },
            Colores.Negativo, new Vector3(-0.275f, 0.15f, -0.05f)));

        // Eje con rueda tacométrica y puntero rojo bien visible.
        var eje = new Node3D { Name = "Eje", Position = new Vector3(0.17f, 0.15f, 0f) };
        var espiga = Pieza(new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.06f },
            new Color(0.92f, 0.92f, 0.92f), Vector3.Zero, rugosidad: 0.4f);
        espiga.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(espiga);

        var disco = Pieza(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.024f },
            new Color(0.12f, 0.13f, 0.16f), new Vector3(0.03f, 0f, 0f), rugosidad: 0.5f);
        disco.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(disco);

        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.030f, 0.245f, 0.018f) },
            new Color(0.92f, 0.92f, 0.94f), new Vector3(0.03f, 0f, 0f)));
        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.030f, 0.018f, 0.245f) },
            new Color(0.92f, 0.92f, 0.94f), new Vector3(0.03f, 0f, 0f)));
        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.036f, 0.125f, 0.034f) },
            new Color(0.92f, 0.18f, 0.14f), new Vector3(0.03f, 0.062f, 0f), rugosidad: 0.35f));
        raiz.AddChild(eje);
        return raiz;
    }

    public static Node3D MotorAltaVelocidad()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.24f, 0.05f, 0.20f) },
            new Color(0.15f, 0.16f, 0.18f), new Vector3(-0.03f, 0.025f, 0f)));
        var cuerpo = Pieza(new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.085f, Height = 0.24f },
            new Color(0.76f, 0.42f, 0.22f), new Vector3(-0.03f, 0.13f, 0f), rugosidad: 0.25f, metalico: 0.85f);
        cuerpo.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(cuerpo);
        var tapa = Pieza(new CylinderMesh { TopRadius = 0.082f, BottomRadius = 0.082f, Height = 0.04f },
            new Color(0.10f, 0.10f, 0.12f), new Vector3(-0.17f, 0.13f, 0f), rugosidad: 0.5f);
        tapa.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(tapa);
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.025f, 0.026f, 0.022f) },
            Colores.Positivo, new Vector3(-0.205f, 0.13f, 0.045f)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.025f, 0.026f, 0.022f) },
            Colores.Negativo, new Vector3(-0.205f, 0.13f, -0.045f)));

        var eje = new Node3D { Name = "Eje", Position = new Vector3(0.145f, 0.13f, 0f) };
        var varilla = Pieza(new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.11f },
            MetalPelado, new Vector3(-0.005f, 0f, 0f), rugosidad: 0.2f, metalico: 1f);
        varilla.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(varilla);

        var nucleo = Pieza(new CylinderMesh { TopRadius = 0.036f, BottomRadius = 0.036f, Height = 0.026f },
            new Color(0.14f, 0.15f, 0.18f), new Vector3(0.035f, 0f, 0f), rugosidad: 0.35f);
        nucleo.RotationDegrees = new Vector3(0, 0, 90);
        eje.AddChild(nucleo);

        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.022f, 0.10f, 0.022f) },
            new Color(0.96f, 0.86f, 0.12f), new Vector3(0.035f, 0.05f, 0f)));
        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.022f, 0.10f, 0.022f) },
            new Color(0.12f, 0.72f, 0.78f), new Vector3(0.035f, -0.05f, 0f)));
        eje.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.020f, 0.018f, 0.15f) },
            new Color(0.88f, 0.90f, 0.92f), new Vector3(0.035f, 0f, 0f)));
        raiz.AddChild(eje);
        return raiz;
    }

    public static Node3D Disyuntor()
    {
        var raiz = new Node3D();
        // Carcasa modular DIN bicolor (blanco técnico + escalón gris claro).
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.46f, 0.16f, 0.28f) },
            new Color(0.86f, 0.88f, 0.90f), new Vector3(0f, 0.08f, 0f), rugosidad: 0.45f));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.24f, 0.10f, 0.26f) },
            new Color(0.78f, 0.80f, 0.84f), new Vector3(0f, 0.21f, 0f), rugosidad: 0.45f));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.03f, 0.008f, 0.24f) },
            new Color(0.16f, 0.46f, 0.85f), new Vector3(-0.08f, 0.261f, 0f), rugosidad: 0.4f));

        // Ventana luminosa de estado: verde (ON), rojo (DISPARADO), ámbar (OFF manual).
        var ledEstado = Pieza(new CylinderMesh { TopRadius = 0.024f, BottomRadius = 0.024f, Height = 0.012f },
            new Color(0.20f, 0.94f, 0.36f), new Vector3(0.08f, 0.262f, 0.07f), rugosidad: 0.25f);
        ledEstado.Name = "LedEstado";
        raiz.AddChild(ledEstado);

        // Bornes laterales metálicos.
        foreach (float x in new[] { -0.21f, 0.21f })
        {
            var borne = Pieza(new CylinderMesh { TopRadius = 0.032f, BottomRadius = 0.032f, Height = 0.07f },
                MetalPelado, new Vector3(x, 0.09f, 0f), rugosidad: 0.25f, metalico: 0.9f);
            borne.RotationDegrees = new Vector3(0, 0, 90);
            raiz.AddChild(borne);
        }

        // Manija pivotante "Palanca": gira sobre su eje Z local según encendido/disparo.
        var palanca = new Node3D { Name = "Palanca", Position = new Vector3(0f, 0.255f, 0f) };
        var tambor = Pieza(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.14f },
            new Color(0.15f, 0.16f, 0.18f), Vector3.Zero, rugosidad: 0.5f);
        tambor.RotationDegrees = new Vector3(90, 0, 0);
        palanca.AddChild(tambor);
        palanca.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.032f, 0.105f, 0.11f) },
            new Color(0.18f, 0.20f, 0.24f), new Vector3(0f, 0.052f, 0f), rugosidad: 0.45f));
        palanca.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.065f, 0.028f, 0.14f) },
            new Color(0.14f, 0.48f, 0.88f), new Vector3(0f, 0.105f, 0f), rugosidad: 0.35f));
        raiz.AddChild(palanca);
        return raiz;
    }

    public static Node3D Ne555()
    {
        var raiz = new Node3D();
        // Cuerpo epoxi negro DIP-8.
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.46f, 0.12f, 0.28f) },
            new Color(0.09f, 0.09f, 0.11f), new Vector3(0f, 0.09f, 0f), rugosidad: 0.55f));
        // Muesca semicircular de orientación (-X) y punto indicador Pin 1 (GND).
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.042f, BottomRadius = 0.042f, Height = 0.012f },
            new Color(0.22f, 0.23f, 0.25f), new Vector3(-0.205f, 0.152f, 0f), rugosidad: 0.5f));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.020f, BottomRadius = 0.020f, Height = 0.008f },
            new Color(0.92f, 0.92f, 0.94f), new Vector3(-0.17f, 0.152f, 0.09f), rugosidad: 0.3f));
        // Banda superior grabada estilo encapsulado DIP.
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.26f, 0.006f, 0.08f) },
            new Color(0.68f, 0.70f, 0.74f), new Vector3(0.02f, 0.152f, 0f), rugosidad: 0.4f, metalico: 0.4f));

        // 8 patas metálicas DIP (4 por fila Z = ±0.16).
        foreach (float x in new[] { -0.165f, -0.055f, 0.055f, 0.165f })
        {
            foreach (float signoZ in new[] { -1f, 1f })
            {
                raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.028f, 0.018f, 0.045f) },
                    MetalPelado, new Vector3(x, 0.075f, signoZ * 0.155f), rugosidad: 0.22f, metalico: 0.95f));
                raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.028f, 0.075f, 0.028f) },
                    MetalPelado, new Vector3(x, 0.0375f, signoZ * 0.17f), rugosidad: 0.22f, metalico: 0.95f));
            }
        }
        return raiz;
    }

    public static Node3D Transistor()
    {
        var raiz = new Node3D();
        // Cuerpo de epoxi negro tipo TO-92 (aproximado con un cilindro).
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.12f, Height = 0.22f },
            new Color(0.12f, 0.12f, 0.14f), new Vector3(0, 0.18f, 0), rugosidad: 0.5f));
        // El ladito CHATO del TO-92 (el que marca la pata 1 en los de verdad).
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.2f, 0.2f, 0.02f) },
            new Color(0.1f, 0.1f, 0.12f), new Vector3(0, 0.18f, 0.11f), rugosidad: 0.5f));
        // Las tres patitas metálicas (C · B · E).
        foreach (float x in new[] { -0.06f, 0f, 0.06f })
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.14f },
                new Color(0.8f, 0.8f, 0.85f), new Vector3(x, 0.05f, 0.06f), rugosidad: 0.25f, metalico: 0.95f));
        }
        return raiz;
    }

    public static Node3D Inductor()
    {
        var raiz = new Node3D();
        var nucleo = Pieza(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.34f },
            new Color(0.35f, 0.32f, 0.3f), new Vector3(0, 0.12f, 0));
        nucleo.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(nucleo);
        // Las espiras de cobre: cuatro toros.
        foreach (float x in new[] { -0.1f, -0.033f, 0.033f, 0.1f })
        {
            var espira = Pieza(new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.085f },
                new Color(0.8f, 0.45f, 0.2f), new Vector3(x, 0.12f, 0), rugosidad: 0.3f, metalico: 0.8f);
            espira.RotationDegrees = new Vector3(0, 0, 90);
            raiz.AddChild(espira);
        }
        return raiz;
    }

    private static Node3D PilaCilindrica(float radio, float alto, Color cuerpo)
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = radio, BottomRadius = radio, Height = alto },
            cuerpo, new Vector3(0, alto / 2, 0), rugosidad: 0.35f));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = radio * 0.3f, BottomRadius = radio * 0.3f, Height = 0.03f },
            new Color(0.85f, 0.85f, 0.88f), new Vector3(0, alto + 0.012f, 0), metalico: 0.9f)); // la tetita +
        return raiz;
    }

    public static Node3D PilaAA() => PilaCilindrica(0.075f, 0.28f, new Color(0.15f, 0.45f, 0.2f));

    public static Node3D PilaAAA() => PilaCilindrica(0.055f, 0.24f, new Color(0.5f, 0.15f, 0.15f));

    public static Node3D BateriaAuto()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.6f, 0.36f, 0.4f) },
            new Color(0.1f, 0.1f, 0.12f), new Vector3(0, 0.18f, 0)));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.6f, 0.05f, 0.4f) },
            new Color(0.85f, 0.75f, 0.2f), new Vector3(0, 0.385f, 0))); // la tapa amarilla clásica
        foreach (var (x, color) in new[] { (-0.2f, Colores.Positivo), (0.2f, Colores.Negativo) })
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.045f, Height = 0.07f },
                color, new Vector3(x, 0.44f, -0.1f), metalico: 0.7f));
        }
        return raiz;
    }

    public static Node3D Tomacorriente()
    {
        var raiz = new Node3D();
        var placa = Pieza(new BoxMesh { Size = new Vector3(0.36f, 0.5f, 0.05f) },
            new Color(0.92f, 0.9f, 0.85f), new Vector3(0, 0.25f, 0), rugosidad: 0.4f);
        raiz.AddChild(placa);
        foreach (float x in new[] { -0.06f, 0.06f })
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.016f, BottomRadius = 0.016f, Height = 0.03f },
                new Color(0.08f, 0.08f, 0.08f), new Vector3(x, 0.28f, 0.028f))
            );
        }
        return raiz;
    }

    public static Node3D Broche9V()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.26f, 0.08f, 0.16f) },
            new Color(0.1f, 0.1f, 0.12f), new Vector3(0, 0.04f, 0)));
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.03f },
            new Color(0.75f, 0.75f, 0.78f), new Vector3(-0.06f, 0.095f, 0), metalico: 0.9f)); // hembra
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.04f },
            new Color(0.75f, 0.75f, 0.78f), new Vector3(0.06f, 0.1f, 0), metalico: 0.9f));    // macho
        return raiz;
    }

    private static readonly Color MetalPelado = new(0.78f, 0.78f, 0.82f);

    // =================================================================
    //  Primitivas de la tanda de semiconductores y convertidores
    // =================================================================

    /// <summary>Cilindrito acostado con banda en el extremo del cátodo (helper de los dos diodos).</summary>
    private static Node3D DiodoBase(Color cuerpo, Color banda)
    {
        var raiz = new Node3D();
        var cpo = Pieza(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.5f },
            cuerpo, new Vector3(0, 0.16f, 0), rugosidad: 0.45f);
        cpo.RotationDegrees = new Vector3(0, 0, 90); // acostado a lo largo de X
        raiz.AddChild(cpo);
        var marca = Pieza(new CylinderMesh { TopRadius = 0.095f, BottomRadius = 0.095f, Height = 0.07f },
            banda, new Vector3(0.16f, 0.16f, 0), rugosidad: 0.4f);
        marca.RotationDegrees = new Vector3(0, 0, 90);
        raiz.AddChild(marca);
        foreach (float x in new[] { -0.36f, 0.36f })
        {
            var pata = Pieza(new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.22f },
                MetalPelado, new Vector3(x, 0.16f, 0), rugosidad: 0.25f, metalico: 0.95f);
            pata.RotationDegrees = new Vector3(0, 0, 90);
            raiz.AddChild(pata);
        }
        return raiz;
    }

    /// <summary>Diodo rectificador: cuerpo negro, banda plateada (el cátodo).</summary>
    public static Node3D DiodoRectificador() =>
        DiodoBase(new Color(0.1f, 0.1f, 0.12f), new Color(0.82f, 0.82f, 0.85f));

    /// <summary>Diodo Zener: cuerpo vidrio-naranja, banda azul (como los BZX de vidrio).</summary>
    public static Node3D DiodoZener() =>
        DiodoBase(new Color(0.55f, 0.3f, 0.12f), new Color(0.2f, 0.35f, 0.85f));

    /// <summary>Capacitor electrolítico: el cilindro parado con la franja del negativo.</summary>
    public static Node3D Electrolitico()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.44f },
            new Color(0.12f, 0.18f, 0.42f), new Vector3(0, 0.32f, 0), rugosidad: 0.4f));
        // La FRANJA clara del lado del negativo (la marca que te salva).
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.09f, 0.42f, 0.015f) },
            new Color(0.85f, 0.85f, 0.82f), new Vector3(0.1f, 0.32f, 0.128f), rugosidad: 0.5f));
        // La tapa de aluminio con la cruz de alivio (la que revienta).
        raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.155f, BottomRadius = 0.155f, Height = 0.015f },
            new Color(0.78f, 0.8f, 0.83f), new Vector3(0, 0.545f, 0), rugosidad: 0.3f, metalico: 0.85f));
        // Las dos patas.
        foreach (float x in new[] { -0.07f, 0.07f })
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.1f },
                MetalPelado, new Vector3(x, 0.05f, 0), rugosidad: 0.25f, metalico: 0.95f));
        }
        return raiz;
    }

    /// <summary>Primitiva de respaldo del Transformador AC→DC con núcleo, puente, filtro y 4 bornes.</summary>
    public static Node3D TransformadorAcDc()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.84f, 0.04f, 0.56f) },
            new Color(0.118f, 0.141f, 0.169f), new Vector3(0, 0.02f, 0), rugosidad: 0.6f, metalico: 0.2f));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.30f, 0.32f, 0.36f) },
            new Color(0.48f, 0.50f, 0.55f), new Vector3(-0.12f, 0.20f, 0), rugosidad: 0.35f, metalico: 0.75f));
        foreach (float x in new[] { -0.20f, -0.04f })
        {
            raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.12f, 0.22f, 0.42f) },
                new Color(0.76f, 0.44f, 0.18f), new Vector3(x, 0.20f, 0), rugosidad: 0.45f, metalico: 0.45f));
        }
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.10f, 0.08f, 0.12f) },
            new Color(0.12f, 0.12f, 0.14f), new Vector3(0.10f, 0.08f, 0), rugosidad: 0.5f));
        foreach (float z in new[] { 0.13f, -0.13f })
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.20f },
                new Color(0.12f, 0.22f, 0.48f), new Vector3(0.14f, 0.14f, z), rugosidad: 0.4f, metalico: 0.15f));
        }
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.09f, 0.16f, 0.30f) },
            new Color(0.72f, 0.75f, 0.79f), new Vector3(0.24f, 0.12f, -0.02f), rugosidad: 0.3f, metalico: 0.85f));
        var led = Pieza(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.03f },
            new Color(0.18f, 0.85f, 0.32f), new Vector3(0.25f, 0.175f, 0.19f), rugosidad: 0.3f);
        led.Name = "LedEstado";
        raiz.AddChild(led);

        var bornes = new (float x, float z, Color collar)[]
        {
            (-0.36f, 0.16f, new Color(0.82f, 0.42f, 0.14f)),
            (-0.36f, -0.16f, new Color(0.18f, 0.48f, 0.85f)),
            (0.36f, 0.16f, new Color(0.88f, 0.16f, 0.16f)),
            (0.36f, -0.16f, new Color(0.12f, 0.12f, 0.14f)),
        };
        foreach (var (bx, bz, cCollar) in bornes)
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.040f, BottomRadius = 0.040f, Height = 0.06f },
                cCollar, new Vector3(bx, 0.07f, bz), rugosidad: 0.45f));
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.06f },
                MetalPelado, new Vector3(bx, 0.13f, bz), rugosidad: 0.25f, metalico: 0.92f));
        }
        return raiz;
    }

    /// <summary>Primitiva de respaldo del Inversor DC→AC con chasis aleteado, LED de estado y 4 bornes.</summary>
    public static Node3D InversorDcAc()
    {
        var raiz = new Node3D();
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.86f, 0.04f, 0.56f) },
            new Color(0.12f, 0.13f, 0.15f), new Vector3(0, 0.02f, 0), rugosidad: 0.55f, metalico: 0.25f));
        raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.62f, 0.24f, 0.50f) },
            new Color(0.16f, 0.32f, 0.54f), new Vector3(0, 0.16f, 0), rugosidad: 0.38f, metalico: 0.65f));
        foreach (float zFin in new[] { -0.18f, -0.09f, 0f, 0.09f, 0.18f })
        {
            raiz.AddChild(Pieza(new BoxMesh { Size = new Vector3(0.58f, 0.04f, 0.04f) },
                new Color(0.22f, 0.38f, 0.62f), new Vector3(0, 0.30f, zFin), rugosidad: 0.35f, metalico: 0.70f));
        }
        var led = Pieza(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.03f },
            new Color(0.18f, 0.85f, 0.32f), new Vector3(0.24f, 0.295f, 0.225f), rugosidad: 0.3f);
        led.Name = "LedEstado";
        raiz.AddChild(led);

        var bornes = new (float x, float z, Color collar)[]
        {
            (-0.37f, 0.16f, new Color(0.88f, 0.16f, 0.16f)),
            (-0.37f, -0.16f, new Color(0.10f, 0.10f, 0.12f)),
            (0.37f, 0.16f, new Color(0.82f, 0.42f, 0.14f)),
            (0.37f, -0.16f, new Color(0.18f, 0.48f, 0.85f)),
        };
        foreach (var (bx, bz, cCollar) in bornes)
        {
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.07f },
                cCollar, new Vector3(bx, 0.075f, bz), rugosidad: 0.45f));
            raiz.AddChild(Pieza(new CylinderMesh { TopRadius = 0.020f, BottomRadius = 0.020f, Height = 0.07f },
                MetalPelado, new Vector3(bx, 0.145f, bz), rugosidad: 0.25f, metalico: 0.92f));
        }
        return raiz;
    }
}
