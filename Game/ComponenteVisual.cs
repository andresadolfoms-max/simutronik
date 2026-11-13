using Godot;
using ElectricSim.Core;
using ElectricSim.Core.Components;

namespace ElectricSim.Game;

/// <summary>
/// La representación 3D de un componente en el mundo. Une tres cosas:
///
///  - La LÓGICA del motor (Component), que es la única fuente de verdad
///    eléctrica. Este nodo nunca calcula nada: lee y muestra.
///  - El MODELO (un .glb importado o una primitiva del catálogo),
///    auto-escalado por caja envolvente a la altura pedida — así los .glb
///    exportados con escalas distintas conviven sin retocarlos a mano.
///  - Los BORNES clickeables, colocados por fracciones de esa caja ajustada.
///
/// También traduce el estado del motor a feedback visual en cada tick:
/// brillo de LEDs y lámparas (con luz real), palanca del interruptor, botón
/// del pulsador hundiéndose, filamento del fusible, y el humo + tiznado de
/// cualquier cosa que se queme.
/// </summary>
public partial class ComponenteVisual : Node3D
{
    public DefinicionComponente Def { get; private set; } = null!;
    public Component Logica { get; private set; } = null!;
    public BorneVisual BorneA { get; private set; } = null!;
    public BorneVisual BorneB { get; private set; } = null!;

    /// <summary>Los bornes de los componentes multi-borne (los 22 GPIO/rieles extra del Arduino). Vacío para los de dos patas.</summary>
    public List<BorneVisual> BornesExtra { get; } = new();

    /// <summary>
    /// La bolsa de estado con la que este componente vino de un archivo de
    /// proyecto. Sirve para PRESERVAR claves que este build no conoce: si el
    /// proyecto lo guardó una versión más nueva del simulador, sus datos
    /// extra viajan intactos y se vuelven a escribir al guardar.
    /// </summary>
    public EstadoGuardado EstadoPreservado { get; set; } = new();

    private Simulacion _sim = null!;
    private Node3D _modelo = null!;
    private Node3D? _modeloAlterno;           // interruptor: palanca abajo
    private readonly List<MeshInstance3D> _meshes = new();
    private Vector3 _tamano;                   // caja del modelo ya ajustado
    private MeshInstance3D _anillo = null!;    // aro de selección en el piso
    private StaticBody3D _cuerpo = null!;

    // Piezas especiales que se buscan por nombre dentro del modelo:
    private Node3D? _boton;                    // pulsador
    private Node3D? _perilla;                  // potenciómetro
    private Node3D? _eje;                      // motor: eje/rotor giratorio
    private Node3D? _palanca;                  // disyuntor: manija de accionamiento
    private float _anguloEje;
    private MeshInstance3D? _filamento;        // fusible
    private MeshInstance3D? _bulbo;            // lámpara (primitiva)
    private MeshInstance3D? _ledDomo;          // parte superior de LED.glb
    private StandardMaterial3D? _ledDomoMat;
    private Color _ledDomoColorOriginal = new(0.8f, 0f, 0f);
    private readonly Color _bulboColorOriginal = new(1f, 0.93f, 0.75f, 0.85f);

    // Luz y brillo para LED / lámpara:
    private MeshInstance3D? _glow;
    private StandardMaterial3D? _glowMat;
    private OmniLight3D? _luz;

    // El LED "L" integrado de la placa Arduino (espeja el estado de D13):
    private StandardMaterial3D? _ledPlacaMat;
    // LED indicador de encendido en convertidores (Transformador AC→DC / Inversor DC→AC) y disyuntor:
    private StandardMaterial3D? _ledEstadoMat;

    // Las 3 bandas de valor del código de colores de la resistencia (la 4ta es dorada fija):
    private StandardMaterial3D[]? _bandasResistencia;
    private double _ultimoOhmsBandas = double.NaN;

    // Pantalla superior de la CajaConPantalla:
    private Label3D? _pantallaCaja;

    private bool _quemadoAplicado;
    private static readonly Dictionary<string, int> _contadores = new();

    /// <summary>Crea el componente completo: lógica registrada en la simulación + visual en el mundo.</summary>
    public static ComponenteVisual Crear(DefinicionComponente def, Simulacion sim, Vector3 posicionGlobal, Node padre)
    {
        var visual = new ComponenteVisual { Def = def, _sim = sim };

        // Nombre único y amigable: "LED 3", "Batería 9V 1"...
        int n = _contadores.GetValueOrDefault(def.Id) + 1;
        _contadores[def.Id] = n;

        visual.Logica = def.CrearLogica();
        visual.Logica.Name = $"{def.Nombre} {n}";
        visual.Name = $"{def.Id}_{n}";

        padre.AddChild(visual);
        visual.GlobalPosition = posicionGlobal;
        visual.ConstruirVisual();

        sim.AgregarComponente(visual.Logica, visual.CentroGlobal());
        return visual;
    }

    /// <summary>El punto que representa al componente para el sistema térmico (su centro, no su base).</summary>
    public Vector3 CentroGlobal() => GlobalPosition + Vector3.Up * (_tamano.Y * 0.5f);

    private void ConstruirVisual()
    {
        // --- Modelo principal (glb o primitiva), auto-ajustado ---
        _modelo = InstanciarModeloAjustado(Def, principal: true, out _tamano, _meshes);
        AddChild(_modelo);

        // --- Modelo alterno (interruptor con palanca abajo) ---
        if (Def.RutaModeloAlterno != null)
        {
            _modeloAlterno = InstanciarModeloAjustado(Def, Def.RutaModeloAlterno, out _, _meshes);
            AddChild(_modeloAlterno);
            _modeloAlterno.Visible = false;
        }

        // --- Piezas especiales por nombre ---
        _boton = _modelo.FindChild("Boton", recursive: true, owned: false) as Node3D;
        _perilla = _modelo.FindChild("Perilla", recursive: true, owned: false) as Node3D;
        _eje = _modelo.FindChild("Eje", recursive: true, owned: false) as Node3D;
        _palanca = _modelo.FindChild("Palanca", recursive: true, owned: false) as Node3D;
        _filamento = _modelo.FindChild("Filamento", recursive: true, owned: false) as MeshInstance3D;
        _bulbo = _modelo.FindChild("Bulbo", recursive: true, owned: false) as MeshInstance3D;
        if (Logica is LED)
        {
            _ledDomo = _meshes.Find(m => m.Name.ToString().Contains("parte", StringComparison.OrdinalIgnoreCase))
                       ?? _meshes.Find(m => !m.Name.ToString().StartsWith("pata", StringComparison.OrdinalIgnoreCase));
            if (_ledDomo != null)
            {
                var original = (_ledDomo.GetActiveMaterial(0) as StandardMaterial3D)
                               ?? (_ledDomo.Mesh?.SurfaceGetMaterial(0) as StandardMaterial3D);
                _ledDomoMat = original != null
                    ? (StandardMaterial3D)original.Duplicate()
                    : new StandardMaterial3D { AlbedoColor = _ledDomoColorOriginal, Roughness = 0.25f };
                _ledDomoColorOriginal = _ledDomoMat.AlbedoColor;
                _ledDomo.MaterialOverride = _ledDomoMat;
            }
        }

        // --- Bandas del código de colores sobre el cuerpo de la resistencia ---
        if (Logica is Resistor && _modelo.FindChild("Cilindro", recursive: true, owned: false) is Node3D cuerpoResistencia)
        {
            ConstruirBandasResistencia(cuerpoResistencia);
        }

        // --- Pantalla digital superior de la Caja con pantalla ---
        if (Logica is CajaConPantalla)
        {
            ConstruirPantallaCaja();
        }

        // --- LED de estado en convertidores aislados y disyuntor ---
        if ((Logica is ConvertidorAislado || Logica is Disyuntor)
            && _modelo.FindChild("LedEstado", recursive: true, owned: false) is MeshInstance3D ledEstadoMesh)
        {
            _ledEstadoMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.18f, 0.85f, 0.32f),
                Roughness = 0.25f,
                EmissionEnabled = true,
                Emission = new Color(0.22f, 1.0f, 0.38f),
                EmissionEnergyMultiplier = 0f,
            };
            ledEstadoMesh.MaterialOverride = _ledEstadoMat;
        }

        // --- Bornes ---
        var etiquetasPendientes = new List<(BorneVisual Borne, string Texto)>();

        BorneA = BorneVisual.Crear(Logica.PinA, this, Def.EscalaBornes);
        BorneA.Position = FraccionALocal(Def.AnclaBorneA);
        AddChild(BorneA);
        if (Def.EtiquetaBorneA != null) etiquetasPendientes.Add((BorneA, Def.EtiquetaBorneA));

        BorneB = BorneVisual.Crear(Logica.PinB, this, Def.EscalaBornes);
        BorneB.Position = FraccionALocal(Def.AnclaBorneB);
        AddChild(BorneB);
        if (Def.EtiquetaBorneB != null) etiquetasPendientes.Add((BorneB, Def.EtiquetaBorneB));

        // --- Bornes EXTRA (componentes multi-borne: el Arduino y sus pines extra) ---
        // La lógica publica (etiqueta, pin); el catálogo dice DÓNDE va cada
        // etiqueta. Si un pin no tiene ancla definida, se avisa y se saltea
        // (mejor un borne menos que el juego roto).
        if (Logica is IComponenteMultiBorne multiBorne)
        {
            foreach (var (etiqueta, pin) in multiBorne.BornesExtra)
            {
                int indiceAncla = Def.AnclasBornesExtra.FindIndex(a => a.Clave == etiqueta);
                if (indiceAncla < 0)
                {
                    GD.PushWarning($"El borne '{etiqueta}' de {Logica.Name} no tiene ancla en el catálogo.");
                    continue;
                }

                var borne = BorneVisual.Crear(pin, this, Def.EscalaBornes);
                borne.Position = FraccionALocal(Def.AnclasBornesExtra[indiceAncla].Ancla);
                AddChild(borne);
                BornesExtra.Add(borne);

                // Cartelito flotante: "GND2" y "GND3" se muestran como "GND"
                // (son pines físicos separados puenteados al mismo GND).
                string textoEtiqueta = etiqueta is "GND2" or "GND3" ? "GND" : etiqueta;
                etiquetasPendientes.Add((borne, textoEtiqueta));
            }
        }

        AgregarEtiquetasBornes(etiquetasPendientes);

        // --- LED "L" integrado (la placa Arduino espeja D13 en su lucecita) ---
        if (Def.AnclaLedInterno is Vector3 anclaLed)
        {
            _ledPlacaMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.6f, 0.1f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.75f, 0.2f),
                EmissionEnergyMultiplier = 0f,
            };
            var tamanoLed = new Vector3(
                Mathf.Clamp(0.025f * _tamano.X, 0.010f, 0.035f),
                Mathf.Clamp(0.050f * _tamano.Y, 0.005f, 0.014f),
                Mathf.Clamp(0.020f * _tamano.Z, 0.008f, 0.022f));
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = tamanoLed },
                Position = FraccionALocal(anclaLed),
                MaterialOverride = _ledPlacaMat,
            });
        }

        // --- Cuerpo clickeable (seleccionar / interactuar / arrastrar) ---
        _cuerpo = new StaticBody3D
        {
            CollisionLayer = CapasFisicas.Cuerpos,
            CollisionMask = 0,
            Position = new Vector3(0, _tamano.Y * 0.5f, 0),
        };
        var caja = new Vector3(
            Mathf.Max(_tamano.X * 1.1f, 0.16f),
            Mathf.Max(_tamano.Y * 1.05f, 0.14f),
            Mathf.Max(_tamano.Z * 1.1f, 0.16f));
        _cuerpo.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = caja } });
        AddChild(_cuerpo);

        // --- Anillo de selección (apagado hasta que lo elijan) ---
        float radio = Mathf.Max(_tamano.X, _tamano.Z) * 0.62f + 0.08f;
        _anillo = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radio - 0.03f, OuterRadius = radio },
            Position = new Vector3(0, 0.012f, 0),
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colores.Seleccion,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(_anillo);

        // --- Luz + esfera de brillo para los que iluminan ---
        if (Logica is LED || Logica is Lampara)
        {
            float alturaLuz = Logica is Lampara ? _tamano.Y * 0.62f : _tamano.Y * 0.82f;
            _glowMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.5f, 0.0f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.8f, 0.45f),
                EmissionEnergyMultiplier = 0f,
            };
            _glow = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = Mathf.Max(_tamano.X, _tamano.Z) * 0.3f, Height = Mathf.Max(_tamano.X, _tamano.Z) * 0.6f },
                Position = new Vector3(0, alturaLuz, 0),
                MaterialOverride = _glowMat,
            };
            AddChild(_glow);

            _luz = new OmniLight3D
            {
                Position = new Vector3(0, alturaLuz, 0),
                OmniRange = 1.6f,
                LightColor = new Color(1f, 0.85f, 0.55f),
                LightEnergy = 0f,
                ShadowEnabled = false, // una sombra por foquito sería carísimo
            };
            AddChild(_luz);
        }

        ActualizarDesdeLogica();
    }

    // =====================================================================
    //  Ajuste automático del modelo por caja envolvente
    // =====================================================================

    /// <summary>
    /// Instancia el modelo de una definición (glb o primitiva), le aplica la
    /// rotación base, mide su caja envolvente real recorriendo todas sus
    /// mallas, y lo escala/centra para que: quede apoyado en Y=0, centrado
    /// en XZ, y con la altura pedida por el catálogo. Devuelve el tamaño de
    /// la caja final (que usan bornes, colisión y anillo).
    /// </summary>
    private static Node3D InstanciarModeloAjustado(DefinicionComponente def, bool principal,
        out Vector3 tamano, List<MeshInstance3D>? acumularMeshes)
    {
        Node3D crudo = CargarCrudo(def, principal ? def.RutaModelo : null);
        return Ajustar(crudo, def, out tamano, acumularMeshes);
    }

    /// <summary>Variante para el modelo alterno (segunda ruta de glb del interruptor).</summary>
    private static Node3D InstanciarModeloAjustado(DefinicionComponente def, string rutaAlterna,
        out Vector3 tamano, List<MeshInstance3D>? acumularMeshes)
    {
        Node3D crudo = CargarGlb(rutaAlterna) ?? new Node3D();
        return Ajustar(crudo, def, out tamano, acumularMeshes);
    }

    private static Node3D CargarCrudo(DefinicionComponente def, string? ruta)
    {
        if (ruta != null)
        {
            var glb = CargarGlb(ruta);
            if (glb != null) return glb;
            GD.PushWarning($"No se encontró el modelo {ruta}; se usa un cubo de emergencia.");
        }
        if (def.CrearPrimitiva != null) return def.CrearPrimitiva();

        // Último recurso: un cubo, para que el juego nunca explote por un asset.
        var caja = new Node3D();
        caja.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 0.3f }, Position = new Vector3(0, 0.15f, 0) });
        return caja;
    }

    private static Node3D? CargarGlb(string ruta)
    {
        string rutaReal = ProjectSettings.GlobalizePath(ruta);
        bool glbEnDisco = System.IO.File.Exists(rutaReal);
        if (ResourceLoader.Exists(ruta) && (!glbEnDisco || ImportacionGlbAlDia(rutaReal)))
        {
            var escena = GD.Load<PackedScene>(ruta);
            if (escena != null) return escena.Instantiate<Node3D>();
        }

        // Respaldo directo con GltfDocument por si el .glb recién se copió a
        // models/ y el editor de Godot todavía no generó o actualizó su .import/.scn.
        if (!glbEnDisco) return null;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(rutaReal, state) != Error.Ok) return null;
        return doc.GenerateScene(state) as Node3D;
    }

    private static bool ImportacionGlbAlDia(string rutaReal)
    {
        string rutaImport = rutaReal + ".import";
        if (!System.IO.File.Exists(rutaImport)) return false;

        var cfg = new ConfigFile();
        if (cfg.Load(rutaImport) != Error.Ok) return false;

        string rutaScn = cfg.GetValue("remap", "path", "").AsString();
        if (string.IsNullOrEmpty(rutaScn)) return false;

        string scnReal = ProjectSettings.GlobalizePath(rutaScn);
        if (!System.IO.File.Exists(scnReal)) return false;

        string rutaMd5 = System.IO.Path.ChangeExtension(scnReal, ".md5");
        if (!System.IO.File.Exists(rutaMd5)) return false;

        foreach (string linea in System.IO.File.ReadLines(rutaMd5))
        {
            const string prefijo = "source_md5=\"";
            if (!linea.StartsWith(prefijo, StringComparison.Ordinal)) continue;
            string esperado = linea[prefijo.Length..].TrimEnd('"', '\r', ' ');
            using var flujo = System.IO.File.OpenRead(rutaReal);
            string actual = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(flujo)).ToLowerInvariant();
            return string.Equals(actual, esperado, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static Node3D Ajustar(Node3D crudo, DefinicionComponente def,
        out Vector3 tamano, List<MeshInstance3D>? acumularMeshes)
    {
        var envoltorio = new Node3D { Name = "Modelo" };
        envoltorio.AddChild(crudo);
        crudo.Transform = new Transform3D(def.RotacionBase, Vector3.Zero);

        // Medimos la caja real (con la rotación base ya aplicada).
        Aabb? caja = null;
        MedirAabb(crudo, crudo.Transform, ref caja, acumularMeshes);
        var box = caja ?? new Aabb(Vector3.Zero, Vector3.One * 0.3f);

        float escala = def.AlturaObjetivo / Mathf.Max(box.Size.Y, 0.001f);
        var centro = box.Position + box.Size * 0.5f;

        // Escalamos el envoltorio y corremos el modelo para que la caja quede
        // centrada en XZ y apoyada en Y=0.
        envoltorio.Scale = Vector3.One * escala;
        crudo.Position = new Vector3(-centro.X, -box.Position.Y, -centro.Z);

        tamano = box.Size * escala;
        return envoltorio;
    }

    /// <summary>Recorre el árbol acumulando transformaciones y expandiendo la caja con las 8 esquinas de cada malla.</summary>
    private static void MedirAabb(Node nodo, Transform3D acumulada, ref Aabb? total, List<MeshInstance3D>? meshes)
    {
        if (nodo is MeshInstance3D mi)
        {
            meshes?.Add(mi);
            var local = mi.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                var esquina = acumulada * local.GetEndpoint(i);
                total = total?.Expand(esquina) ?? new Aabb(esquina, Vector3.Zero);
            }
        }

        foreach (var hijo in nodo.GetChildren())
        {
            var siguiente = hijo is Node3D n3 ? acumulada * n3.Transform : acumulada;
            MedirAabb(hijo, siguiente, ref total, meshes);
        }
    }

    private Vector3 FraccionALocal(Vector3 fraccion) => new(
        fraccion.X * _tamano.X,
        fraccion.Y * _tamano.Y,
        fraccion.Z * _tamano.Z);

    /// <summary>
    /// Crea las etiquetas flotantes ("D13", "5V", "A0"...) y las vincula a su
    /// borne. En conectores densos (Arduino) reduce el tamaño del texto y
    /// alterna dos alturas en pines vecinos para que los nombres no se pisen.
    /// </summary>
    private void AgregarEtiquetasBornes(List<(BorneVisual Borne, string Texto)> pendientes)
    {
        if (pendientes.Count == 0) return;

        bool denso = Def.EscalaBornes < 0.6f;
        if (!denso)
        {
            foreach (var (borne, texto) in pendientes)
            {
                var etiqueta = CrearEtiquetaBorne(texto, borne.Position, 0.055f, 0.0011f, 10);
                AddChild(etiqueta);
                borne.VincularEtiqueta(etiqueta);
            }
            return;
        }

        // Ordenamos por fila en Z (paso grueso de 4 cm) y de izquierda a
        // derecha en X para escalonar automáticamente cualquier pin vecino.
        var ordenados = new List<(BorneVisual Borne, string Texto)>(pendientes);
        ordenados.Sort((a, b) =>
        {
            int filaA = Mathf.RoundToInt(a.Borne.Position.Z / 0.04f);
            int filaB = Mathf.RoundToInt(b.Borne.Position.Z / 0.04f);
            int cmp = filaA.CompareTo(filaB);
            return cmp != 0 ? cmp : a.Borne.Position.X.CompareTo(b.Borne.Position.X);
        });

        int? filaAnterior = null;
        float xAnterior = float.NegativeInfinity;
        bool nivelAlto = false;
        foreach (var (borne, texto) in ordenados)
        {
            int fila = Mathf.RoundToInt(borne.Position.Z / 0.04f);
            if (filaAnterior != fila || borne.Position.X - xAnterior > 0.05f)
            {
                nivelAlto = false;
            }
            else
            {
                nivelAlto = !nivelAlto;
            }
            filaAnterior = fila;
            xAnterior = borne.Position.X;

            float offsetY = nivelAlto ? 0.032f : 0.017f;
            var etiqueta = CrearEtiquetaBorne(texto, borne.Position, offsetY, 0.00036f, 8);
            AddChild(etiqueta);
            borne.VincularEtiqueta(etiqueta);
        }
    }

    /// <summary>El cartelito 3D que flota sobre un borne ("D13", "5V", "A0"): siempre mira a cámara.</summary>
    private static Label3D CrearEtiquetaBorne(string texto, Vector3 posicionBorne,
        float offsetY, float pixelSize, int outlineSize) => new()
    {
        Text = texto,
        Position = posicionBorne + new Vector3(0, offsetY, 0),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FontSize = 40,
        PixelSize = pixelSize,
        OutlineSize = outlineSize,
        Modulate = new Color(1f, 1f, 1f, 0.92f),
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
    };

    /// <summary>Todos los bornes del componente: A, B y los extra (si los hay).</summary>
    public IEnumerable<BorneVisual> TodosLosBornes()
    {
        yield return BorneA;
        yield return BorneB;
        foreach (var extra in BornesExtra) yield return extra;
    }

    // =====================================================================
    //  Estado visual tick a tick
    // =====================================================================

    /// <summary>Sincroniza lo que se VE con lo que el motor CALCULÓ. Se llama en cada TickCompletado.</summary>
    public void ActualizarDesdeLogica()
    {
        if (Logica.IsBurnt)
        {
            AplicarQuemado();
            return;
        }

        switch (Logica)
        {
            case Switch sw:
                _modelo.Visible = sw.IsClosed;
                if (_modeloAlterno != null) _modeloAlterno.Visible = !sw.IsClosed;
                break;

            case Pulsador p:
                if (_boton != null)
                {
                    var pos = _boton.Position;
                    _boton.Position = new Vector3(pos.X, p.Presionado ? 0.1f : 0.14f, pos.Z);
                }
                break;

            case LED led:
            {
                bool personalizado = led.ColorPersonalizado != null;
                Color colorLuz = ColorDesdeRgb(led.ColorPersonalizado, new Color(1f, 0.35f, 0.25f));
                AplicarBrillo((float)led.Brillo, colorLuz);
                if (_ledDomoMat != null)
                {
                    _ledDomoMat.AlbedoColor = personalizado
                        ? new Color(colorLuz.R, colorLuz.G, colorLuz.B, 1f)
                        : _ledDomoColorOriginal;
                    _ledDomoMat.EmissionEnabled = personalizado;
                    _ledDomoMat.Emission = colorLuz;
                    _ledDomoMat.EmissionEnergyMultiplier = personalizado ? 1.6f * (float)led.Brillo : 0f;
                }
                break;
            }

            case Lampara lamp:
            {
                Color colorLuz = ColorDesdeRgb(lamp.ColorPersonalizado, new Color(1f, 0.82f, 0.5f));
                Color colorEmision = ColorDesdeRgb(lamp.ColorPersonalizado, new Color(1f, 0.85f, 0.55f));
                AplicarBrillo((float)lamp.Brillo, colorLuz);
                if (_bulbo?.MaterialOverride is StandardMaterial3D matBulbo)
                {
                    matBulbo.AlbedoColor = lamp.ColorPersonalizado is { } c
                        ? new Color(c.R, c.G, c.B, 0.85f)
                        : _bulboColorOriginal;
                    matBulbo.EmissionEnabled = true;
                    matBulbo.Emission = colorEmision;
                    matBulbo.EmissionEnergyMultiplier = 2.2f * (float)lamp.Brillo;
                }
                break;
            }

            case Potenciometro pote when _perilla != null:
            {
                float frac = Mathf.Clamp((float)(pote.Perilla / 100.0), 0f, 1f);
                _perilla.RotationDegrees = new Vector3(0f, 135f * (1f - 2f * frac), 0f);
                break;
            }

            case ArduinoUno uno:
                // El LED "L" de la placa espeja a D13: brilla según su duty
                // cuando es salida, o según el voltaje externo aplicado si está
                // como entrada sin pull-up.
                if (_ledPlacaMat != null)
                {
                    var d13 = uno.EstadoPin(13);
                    float brilloL = d13.EsSalida
                        ? (float)d13.Duty
                        : (!uno.PinesDanados[13] && !d13.PullupActivo
                            ? Mathf.Clamp(((float)uno.VoltajePin(13) - 1.5f) / 3.5f, 0f, 1f)
                            : 0f);
                    _ledPlacaMat.EmissionEnergyMultiplier = 3.0f * brilloL;
                }
                break;

            case Resistor res when _bandasResistencia != null && res.Resistance != _ultimoOhmsBandas:
                _ultimoOhmsBandas = res.Resistance;
                var (d1, d2, exp) = CodigoColores.Bandas(res.Resistance);
                _bandasResistencia[0].AlbedoColor = ColorBandaDigito(d1);
                _bandasResistencia[1].AlbedoColor = ColorBandaDigito(d2);
                _bandasResistencia[2].AlbedoColor = ColorBandaMultiplicador(exp);
                _bandasResistencia[2].Metallic = exp < 0 ? 0.85f : 0.1f;
                break;

            case CajaConPantalla caja when _pantallaCaja != null:
                ActualizarPantallaCaja(caja);
                break;

            case Disyuntor dis:
                if (_palanca != null)
                {
                    float angulo = dis.Disparado ? 42f : (dis.CerradoManual ? -34f : 26f);
                    _palanca.RotationDegrees = new Vector3(0f, 0f, angulo);
                }
                if (_ledEstadoMat != null)
                {
                    Color c = dis.Disparado
                        ? new Color(0.98f, 0.16f, 0.14f)
                        : dis.CerradoManual
                            ? new Color(0.20f, 0.94f, 0.36f)
                            : new Color(0.72f, 0.50f, 0.16f);
                    _ledEstadoMat.AlbedoColor = c;
                    _ledEstadoMat.Emission = c;
                    _ledEstadoMat.EmissionEnergyMultiplier = dis.Disparado ? 3.2f : (dis.CerradoManual ? 2.4f : 0.6f);
                }
                break;

            case ConvertidorAislado conv when _ledEstadoMat != null:
                _ledEstadoMat.AlbedoColor = conv.Encendido
                    ? new Color(0.22f, 0.96f, 0.38f)
                    : new Color(0.12f, 0.30f, 0.16f);
                _ledEstadoMat.EmissionEnergyMultiplier = conv.Encendido ? 2.6f : 0f;
                break;
        }
    }

    /// <summary>
    /// Avanza la animación continua por cuadro (rotores y hélices de motores) según su RPM y sentido real.
    /// </summary>
    public void AvanzarAnimacion(double delta)
    {
        if (delta <= 0 || _eje == null || !IsInstanceValid(_eje)) return;
        if (Logica is not Motor motor || motor.IsBurnt || motor.EsIncompatible) return;

        double rpm = motor.RPM;
        if (Math.Abs(rpm) <= 0.1) return;

        // Curva monótona perceptual: fiel 1:1 hasta 120 RPM y comprimida logarítmicamente
        // a altas RPM para evitar inversión aparente (aliasing estroboscópico) en pantalla y video.
        double rpsReales = Math.Abs(rpm) / 60.0;
        double rpsVisuales = rpsReales <= 2.0
            ? rpsReales
            : 2.0 + 1.15 * Math.Log(1.0 + (rpsReales - 2.0));
        rpsVisuales = Math.Min(rpsVisuales, 5.8);

        float signoVisual = rpm >= 0 ? -1f : 1f;
        float paso = signoVisual * (float)(rpsVisuales * Math.Tau * delta);
        paso = Mathf.Clamp(paso, -Mathf.Pi * 0.42f, Mathf.Pi * 0.42f);

        _anguloEje = Mathf.Wrap(_anguloEje + paso, 0f, Mathf.Tau);
        _eje.Rotation = new Vector3(_anguloEje, 0f, 0f);
    }

    private void ActualizarPantallaCaja(CajaConPantalla caja)
    {
        if (_pantallaCaja == null) return;
        string texto = caja.TextoPantalla3D();
        _pantallaCaja.Text = texto;

        string[] lineas = texto.Split('\n');
        int maxCaracteres = 1;
        foreach (string linea in lineas)
        {
            if (linea.Length > maxCaracteres) maxCaracteres = linea.Length;
        }

        // Aprovecha ~82% del visor negro superior sin desbordar bordes ni en modo DC ni en modo AC.
        float ladoUtil = Mathf.Min(_tamano.X, _tamano.Z) * 0.82f;
        float anchoPx = Math.Max(1, maxCaracteres) * 37f;
        float altoPx = Math.Max(1, lineas.Length) * 76f;
        _pantallaCaja.PixelSize = Mathf.Clamp(Mathf.Min(ladoUtil / anchoPx, ladoUtil / altoPx), 0.00030f, 0.00095f);
    }

    /// <summary>
    /// Crea el texto 3D sobre el visor negro superior de cajaconpantalla.glb.
    /// </summary>
    private void ConstruirPantallaCaja()
    {
        _pantallaCaja = new Label3D
        {
            Position = new Vector3(0f, _tamano.Y + 0.002f, 0f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            FontSize = 64,
            PixelSize = 0.00060f,
            LineSpacing = 0f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.42f, 0.98f, 0.62f, 0.99f),
            OutlineSize = 8,
            OutlineModulate = new Color(0.02f, 0.06f, 0.04f, 0.95f),
            Shaded = false,
            DoubleSided = false,
        };
        AddChild(_pantallaCaja);
    }

    /// <summary>
    /// Coloca los 4 anillos del código de colores alrededor del nodo Cilindro
    /// de resistencia.glb (radio local 1.0, eje Y local entre -3.75 y +3.22).
    /// </summary>
    private void ConstruirBandasResistencia(Node3D cuerpoResistencia)
    {
        _bandasResistencia = new StandardMaterial3D[3];
        float[] posicionesY = { -2.1f, -0.95f, 0.2f };
        for (int i = 0; i < 3; i++)
        {
            var mat = new StandardMaterial3D { Roughness = 0.35f, Metallic = 0.1f };
            _bandasResistencia[i] = mat;
            var anillo = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.022f, BottomRadius = 1.022f, Height = 0.52f },
                Position = new Vector3(0, posicionesY[i], 0),
                MaterialOverride = mat,
            };
            cuerpoResistencia.AddChild(anillo);
            _meshes.Add(anillo);
        }

        // 4ta banda: tolerancia ±5% (dorada) hacia el otro extremo del cuerpo.
        var matOro = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.68f, 0.18f),
            Roughness = 0.25f,
            Metallic = 0.85f,
        };
        var anilloOro = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.022f, BottomRadius = 1.022f, Height = 0.45f },
            Position = new Vector3(0, 1.85f, 0),
            MaterialOverride = matOro,
        };
        cuerpoResistencia.AddChild(anilloOro);
        _meshes.Add(anilloOro);
    }

    private static Color ColorBandaDigito(int digito) => digito switch
    {
        0 => new Color(0.08f, 0.08f, 0.09f), // negro
        1 => new Color(0.42f, 0.22f, 0.08f), // marrón
        2 => new Color(0.85f, 0.12f, 0.10f), // rojo
        3 => new Color(0.95f, 0.48f, 0.08f), // naranja
        4 => new Color(0.95f, 0.85f, 0.12f), // amarillo
        5 => new Color(0.15f, 0.68f, 0.22f), // verde
        6 => new Color(0.14f, 0.38f, 0.88f), // azul
        7 => new Color(0.58f, 0.20f, 0.78f), // violeta
        8 => new Color(0.50f, 0.52f, 0.55f), // gris
        _ => new Color(0.95f, 0.95f, 0.96f), // blanco
    };

    private static Color ColorBandaMultiplicador(int exponente) => exponente switch
    {
        -1 => new Color(0.85f, 0.68f, 0.18f), // dorado (×0,1)
        -2 => new Color(0.78f, 0.80f, 0.84f), // plateado (×0,01)
        _ => ColorBandaDigito(Math.Clamp(exponente, 0, 9)),
    };

    private static Color ColorDesdeRgb(ColorLuzRgb? rgb, Color predeterminado) =>
        rgb is { } c ? new Color(c.R, c.G, c.B) : predeterminado;

    private void AplicarBrillo(float brillo, Color color)
    {
        if (_glowMat != null)
        {
            _glowMat.Emission = color;
            _glowMat.EmissionEnergyMultiplier = 3.5f * brillo;
            _glowMat.AlbedoColor = new Color(color.R, color.G, color.B, 0.35f * brillo);
        }
        if (_luz != null)
        {
            _luz.LightColor = color;
            _luz.LightEnergy = 2.2f * brillo;
        }
    }

    private void AplicarQuemado()
    {
        if (_quemadoAplicado) return;
        _quemadoAplicado = true;

        // Tiznado universal: una capa negra semitransparente sobre TODAS las
        // mallas del modelo (funciona igual para glb y primitivas, sin tocar
        // sus materiales originales).
        var tizne = new StandardMaterial3D
        {
            AlbedoColor = Colores.Quemado,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        foreach (var mesh in _meshes)
        {
            if (IsInstanceValid(mesh)) mesh.MaterialOverlay = tizne;
        }

        if (_filamento != null) _filamento.Visible = false; // el fusible se ve cortado
        if (_ledPlacaMat != null) _ledPlacaMat.EmissionEnergyMultiplier = 0f;
        if (_ledEstadoMat != null) _ledEstadoMat.EmissionEnergyMultiplier = 0f;
        if (_ledDomoMat != null) _ledDomoMat.EmissionEnergyMultiplier = 0f;
        if (_bulbo?.MaterialOverride is StandardMaterial3D matBulbo) matBulbo.EmissionEnergyMultiplier = 0f;
        AplicarBrillo(0, Colors.White);
        EmitirHumo();
    }

    /// <summary>Deshace el tiznado del quemado: le saca la capa negra a las mallas y devuelve el filamento a la vista.</summary>
    private void QuitarQuemado()
    {
        if (!_quemadoAplicado) return;
        _quemadoAplicado = false;
        foreach (var mesh in _meshes)
        {
            if (IsInstanceValid(mesh)) mesh.MaterialOverlay = null;
        }
        if (_filamento != null) _filamento.Visible = true;
    }

    /// <summary>Repara el componente al instante (lógica + visual) y refresca. La temperatura la fija la del ambiente.</summary>
    public void Reparar(double temperaturaAmbiente)
    {
        Logica.Reparar(temperaturaAmbiente);
        QuitarQuemado();
        ActualizarDesdeLogica();
    }

    private void EmitirHumo()
    {
        var humo = new CpuParticles3D
        {
            Amount = 18,
            Lifetime = 1.8f,
            OneShot = true,
            Explosiveness = 0.15f,
            Direction = Vector3.Up,
            Spread = 18f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 0.9f,
            Gravity = new Vector3(0, 0.25f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            Color = new Color(0.25f, 0.25f, 0.27f, 0.8f),
            Position = new Vector3(0, _tamano.Y * 0.8f, 0),
            Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 6, Rings = 4 },
            Emitting = true,
        };
        AddChild(humo);

        var timer = GetTree().CreateTimer(3.0);
        timer.Timeout += () => { if (IsInstanceValid(humo)) humo.QueueFree(); };
    }

    // =====================================================================
    //  Interacción
    // =====================================================================

    /// <summary>Click sobre el cuerpo: los componentes "accionables" reaccionan.</summary>
    public void AlternarInteraccion()
    {
        if (Logica.IsBurnt) return;
        switch (Logica)
        {
            case Switch sw:
                sw.IsClosed = !sw.IsClosed;
                ActualizarDesdeLogica();
                break;
            case Disyuntor dis:
                dis.AlternarManual();
                ActualizarDesdeLogica();
                break;
            case Pulsador p:
                p.Presionar();
                ActualizarDesdeLogica();
                break;

            case ArduinoUno uno when !uno.ProgramaCargado:
                // Placa virgen: el click abre directo el selector de .hex.
                // (Con programa cargado, los controles están en el inspector.)
                DialogoCargarHex.Abrir(uno, this);
                break;
        }
    }

    public void MostrarSeleccion(bool activo) => _anillo.Visible = activo;

    /// <summary>Mueve el componente (arrastre) y reacomoda todo lo que depende de su posición.</summary>
    public void Mover(Vector3 nuevaPosicionGlobal)
    {
        GlobalPosition = nuevaPosicionGlobal;
        _sim.NotificarMovimiento(Logica, CentroGlobal());
        ReconstruirCables();
    }

    public void RotarY(float grados)
    {
        RotationDegrees += new Vector3(0, grados, 0);
        ReconstruirCables();
    }

    /// <summary>Vuelca el componente sobre su eje local (T: acostarlo, pararlo de canto...).</summary>
    public void Volcar(float grados)
    {
        RotateObjectLocal(Vector3.Right, Mathf.DegToRad(grados));
        ReconstruirCables();
    }

    /// <summary>Rotación absoluta en los tres ejes (los campos numéricos del inspector). Libertad total: el usuario manda.</summary>
    public void FijarRotacion(Vector3 grados)
    {
        RotationDegrees = grados;
        ReconstruirCables();
    }

    /// <summary>Prende/apaga la "pista" de TODOS los bornes (modo de visibilidad Al interactuar).</summary>
    public void MarcarPistaBornes(bool activa)
    {
        foreach (var borne in TodosLosBornes())
        {
            borne.MarcarPista(activa);
        }
    }

    private void ReconstruirCables()
    {
        foreach (var cable in CablesConectados())
        {
            cable.Reconstruir();
        }
    }

    /// <summary>Todos los cables que tocan cualquier borne del componente (sin repetidos).</summary>
    public List<CableVisual> CablesConectados()
    {
        var lista = new List<CableVisual>();
        foreach (var borne in TodosLosBornes())
        {
            foreach (var c in borne.Cables)
            {
                if (!lista.Contains(c)) lista.Add(c);
            }
        }
        return lista;
    }

    /// <summary>Elimina el componente y todos sus cables, del motor y del mundo.</summary>
    public void Eliminar()
    {
        foreach (var cable in CablesConectados())
        {
            cable.QuitarSe();
        }
        _sim.QuitarComponente(Logica);
        QueueFree();
    }

    // =====================================================================
    //  Fantasma de colocación (vista previa semitransparente)
    // =====================================================================

    /// <summary>Crea solo la parte visual (sin lógica ni colisiones) para previsualizar dónde se va a colocar.</summary>
    public static Node3D CrearFantasma(DefinicionComponente def)
    {
        var meshes = new List<MeshInstance3D>();
        var nodo = InstanciarModeloAjustado(def, principal: true, out _, meshes);

        var azulado = new StandardMaterial3D
        {
            AlbedoColor = Colores.Fantasma,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        foreach (var mesh in meshes)
        {
            mesh.MaterialOverlay = azulado;
        }
        return nodo;
    }
}
