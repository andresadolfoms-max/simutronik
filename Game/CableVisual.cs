using Godot;
using ElectricSim.Core;
using ElectricSim.Core.Components;

namespace ElectricSim.Game;

/// <summary>
/// Un cable tendido entre dos bornes. Por dentro es un Wire del motor; por
/// fuera, una de DOS variantes:
///
///  · SIMPLE (el de siempre): un tubo que sigue una Bézier con comba, sin
///    presencia física más allá del click. Ideal para cablear rápido.
///  · FÍSICO: un cable RÍGIDO de verdad. Se dobla agarrando sus esferitas de
///    control (y las curvas quedan donde las dejás), tiene cuerpo de colisión
///    en cada tramo, grosor en milímetros que define su resistencia de cobre
///    real (ρ·L/A), su AMPACIDAD (la corriente que aguanta antes de quemarse)
///    y su masa térmica; el aislante muere a los ~105 °C. Sobrecalentado se
///    pone incandescente; quemado queda carbonizado y DEJA DE CONDUCIR (el
///    motor abre la arista). El color se elige desde el inspector.
///
/// El color inicial lo hereda de la polaridad del borne DESDE el que se
/// tendió (rojo positivo, negro negativo, marrón fase, celeste neutro) — así
/// el cableado queda "leíble" de un vistazo.
/// </summary>
public partial class CableVisual : Node3D
{
    public Wire Logica { get; private set; } = null!;
    public BorneVisual BorneA { get; private set; } = null!;
    public BorneVisual BorneB { get; private set; } = null!;

    /// <summary>Cuánto cuelga el cable en el medio (solo variante simple). Negativo = automático (18% de la distancia).</summary>
    public double Comba { get; set; } = -1;

    /// <summary>Radio del tubo, en metros del mundo (variante simple; en la física lo manda GrosorMm).</summary>
    public double Grosor { get; set; } = 0.016;

    /// <summary>True = cable FÍSICO: rígido, doblable, con colisión, cobre real y muerte térmica.</summary>
    public bool EsFisico { get; private set; }

    /// <summary>
    /// True si este "cable" no lo puso el usuario: lo creó el sistema de
    /// contacto porque dos bornes quedaron pegados. Se regenera solo, así que
    /// NO se guarda en el proyecto (ver GestorGuardado) ni se puede doblar.
    /// </summary>
    public bool EsContacto { get; private set; }

    /// <summary>
    /// Lo llama ContactoEntreBornes al crearlo. Además de la marca, le cambia
    /// la pinta: un contacto NO es un cable y tiene que notarse, sobre todo
    /// con los bornes en modo Ocultos, donde es lo único que ves de la unión.
    /// </summary>
    public void MarcarComoContacto()
    {
        EsContacto = true;
        ColorCable = Colores.Contacto;
        if (_material != null)
        {
            _material.AlbedoColor = Colores.Contacto;
            _material.EmissionEnabled = true;
            _material.Emission = Colores.Contacto;
            _material.EmissionEnergyMultiplier = 0.6f;   // un puntito que brilla
        }
    }

    /// <summary>Puntos de doblez (en coordenadas de mundo) entre los dos bornes. Solo variante física.</summary>
    public List<Vector3> PuntosControl { get; } = new();

    /// <summary>Grosor del conductor en milímetros (solo variante física): define resistencia, ampacidad y aguante.</summary>
    public double GrosorMm { get; private set; } = 1.5;

    /// <summary>Color elegido para el cable físico (editable desde el inspector).</summary>
    public Color ColorCable { get; private set; }

    /// <summary>Idem ComponenteVisual: preserva las claves de guardado que este build no conoce.</summary>
    public EstadoGuardado EstadoPreservado { get; set; } = new();

    private Simulacion _sim = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private StaticBody3D _colision = null!;
    private bool _quemadoAplicado;
    private Node3D? _contenedorManijas;
    private bool _manijasVisibles;

    private const int Segmentos = 14;   // puntos a lo largo de la curva
    private const int Lados = 6;        // caras alrededor del tubo

    public static CableVisual Crear(Simulacion sim, BorneVisual desde, BorneVisual hasta, Node padre, bool fisico = false)
    {
        var cable = new CableVisual
        {
            _sim = sim,
            BorneA = desde,
            BorneB = hasta,
            EsFisico = fisico,
            Name = fisico ? "CableFisico" : "Cable",
        };
        cable.Logica = sim.ConectarPines(desde.Pin, hasta.Pin);
        cable.ColorCable = Colores.PorPolaridad(desde.Pin.Polaridad);

        desde.Cables.Add(cable);
        hasta.Cables.Add(cable);

        cable._material = new StandardMaterial3D
        {
            AlbedoColor = cable.ColorCable,
            Roughness = 0.55f,
        };
        cable._mesh = new MeshInstance3D { MaterialOverride = cable._material };
        cable.AddChild(cable._mesh);

        cable._colision = new StaticBody3D
        {
            CollisionLayer = CapasFisicas.Cables,
            CollisionMask = 0,
        };
        cable.AddChild(cable._colision);

        padre.AddChild(cable);

        if (fisico)
        {
            // Arranca RECTO, con un punto de agarre en el medio para que haya
            // algo que doblar desde el primer momento.
            cable.PuntosControl.Add((desde.GlobalPosition + hasta.GlobalPosition) * 0.5f);
        }

        cable.Reconstruir();
        return cable;
    }

    /// <summary>Recalcula la curva, el tubo, la colisión y la posición térmica. Barato: solo corre cuando algo se movió.</summary>
    public void Reconstruir()
    {
        if (!IsInstanceValid(BorneA) || !IsInstanceValid(BorneB)) return;

        Vector3 p0 = BorneA.GlobalPosition;
        Vector3 p2 = BorneB.GlobalPosition;
        GlobalPosition = Vector3.Zero; // el mesh se genera en coordenadas de mundo

        if (EsFisico)
        {
            ReconstruirFisico(p0, p2);
            return;
        }

        float distancia = p0.DistanceTo(p2);
        float comba = Comba >= 0 ? (float)Comba : distancia * 0.18f;

        // Punto de control al medio, colgando hacia abajo (pero nunca bajo el piso).
        Vector3 medio = (p0 + p2) * 0.5f;
        Vector3 p1 = medio + Vector3.Down * comba;
        p1.Y = Mathf.Max(p1.Y, 0.03f);

        // Muestreo de la Bézier cuadrática.
        var puntos = new Vector3[Segmentos + 1];
        double largo = 0;
        for (int i = 0; i <= Segmentos; i++)
        {
            float t = i / (float)Segmentos;
            puntos[i] = Bezier(p0, p1, p2, t);
            if (i > 0) largo += puntos[i].DistanceTo(puntos[i - 1]);
        }
        Logica.Longitud = largo;

        ConstruirTubo(puntos);
        ConstruirColision(puntos);

        // Para el calor, el cable "vive" en el medio de su tendido.
        _sim.NotificarMovimiento(Logica, medio);
    }

    /// <summary>
    /// La variante FÍSICA: una polilínea rígida borne → dobleces → borne. De
    /// acá salen el tubo, una cápsula de colisión POR TRAMO (para poder
    /// clickear cualquier parte del cable), y las propiedades de cobre del
    /// motor: R = ρ·L/A, ampacidad por grosor, masa térmica y los 105 °C del
    /// aislante. Los tramos gordos y cortos redondean a conexión perfecta
    /// (fusionan: corriente exacta, protegidos por ampacidad); los finos o
    /// largos son aristas resistivas que roban voltaje y se calientan solas.
    /// </summary>
    private void ReconstruirFisico(Vector3 p0, Vector3 p2)
    {
        var puntos = new Vector3[PuntosControl.Count + 2];
        puntos[0] = p0;
        for (int i = 0; i < PuntosControl.Count; i++)
        {
            var p = PuntosControl[i];
            p.Y = Mathf.Max(p.Y, RadioVisual() + 0.005f); // nunca hundido en la mesa
            PuntosControl[i] = p;
            puntos[i + 1] = p;
        }
        puntos[^1] = p2;

        double largo = 0;
        Vector3 centro = Vector3.Zero;
        for (int i = 0; i < puntos.Length; i++)
        {
            if (i > 0) largo += puntos[i].DistanceTo(puntos[i - 1]);
            centro += puntos[i];
        }
        centro /= puntos.Length;

        // El cobre manda: resistencia, ampacidad, masa térmica y aislante.
        Logica.Longitud = largo;
        Logica.GrosorMm = GrosorMm;
        double cobre = Wire.ResistenciaDeCobre(largo, GrosorMm);
        Logica.Resistencia = cobre < Wire.ResistenciaDespreciable
            ? CircuitConstants.WireResistance   // gordo y corto: conexión perfecta
            : cobre;                            // fino o largo: arista resistiva real
        Logica.MaxCurrent = Wire.AmpacidadDeCobre(GrosorMm);
        Logica.CapacidadTermica = Wire.CapacidadTermicaDeCable(largo, GrosorMm);
        Logica.MaxTemperature = 105; // el aislante de PVC no perdona mucho más

        ConstruirTubo(puntos);
        ConstruirColisionFisica(puntos);
        RefrescarManijas();

        _sim.NotificarMovimiento(Logica, centro);
    }

    private float RadioVisual() => EsFisico
        ? 0.005f + (float)GrosorMm * 0.004f
        : (float)Grosor;

    private static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
    {
        float u = 1 - t;
        return u * u * a + 2 * u * t * control + t * t * b;
    }

    private void ConstruirTubo(Vector3[] puntos)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Anillo de vértices alrededor de cada punto de la curva.
        var anillos = new Vector3[puntos.Length][];
        for (int i = 0; i < puntos.Length; i++)
        {
            Vector3 tangente;
            if (i == 0) tangente = (puntos[1] - puntos[0]).Normalized();
            else if (i == puntos.Length - 1) tangente = (puntos[i] - puntos[i - 1]).Normalized();
            else tangente = (puntos[i + 1] - puntos[i - 1]).Normalized();

            Vector3 referencia = Mathf.Abs(tangente.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
            Vector3 lado = tangente.Cross(referencia).Normalized();
            Vector3 arriba = lado.Cross(tangente).Normalized();

            anillos[i] = new Vector3[Lados];
            for (int j = 0; j < Lados; j++)
            {
                float ang = Mathf.Tau * j / Lados;
                anillos[i][j] = puntos[i] + (lado * Mathf.Cos(ang) + arriba * Mathf.Sin(ang)) * RadioVisual();
            }
        }

        // Dos triángulos por cara entre anillos consecutivos.
        for (int i = 0; i < puntos.Length - 1; i++)
        {
            for (int j = 0; j < Lados; j++)
            {
                int j2 = (j + 1) % Lados;
                Vector3 a = anillos[i][j], b = anillos[i][j2];
                Vector3 c = anillos[i + 1][j], d = anillos[i + 1][j2];
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(b);
                st.AddVertex(b); st.AddVertex(c); st.AddVertex(d);
            }
        }

        st.GenerateNormals();
        _mesh.Mesh = st.Commit();
    }

    /// <summary>Tres cápsulas a lo largo de la curva alcanzan para poder clickear el cable.</summary>
    private void ConstruirColision(Vector3[] puntos)
    {
        foreach (var hijo in _colision.GetChildren())
        {
            hijo.QueueFree();
        }

        int n = puntos.Length - 1;
        foreach (var (desde, hasta) in new[] { (0, n / 3), (n / 3, 2 * n / 3), (2 * n / 3, n) })
        {
            AgregarCapsula(puntos[desde], puntos[hasta], indiceSegmento: -1);
        }
    }

    /// <summary>
    /// La variante física lleva UNA cápsula por tramo recto, con el índice
    /// del tramo anotado: así el click sabe exactamente dónde insertar un
    /// doblez nuevo, y el cable tiene cuerpo de verdad en toda su extensión.
    /// </summary>
    private void ConstruirColisionFisica(Vector3[] puntos)
    {
        foreach (var hijo in _colision.GetChildren())
        {
            hijo.QueueFree();
        }
        for (int i = 0; i < puntos.Length - 1; i++)
        {
            AgregarCapsula(puntos[i], puntos[i + 1], indiceSegmento: i);
        }
    }

    private void AgregarCapsula(Vector3 a, Vector3 b, int indiceSegmento)
    {
        Vector3 dir = b - a;
        float largo = dir.Length();
        if (largo < 0.01f) return;

        var forma = new CollisionShape3D
        {
            Shape = new CapsuleShape3D
            {
                Radius = Mathf.Max(RadioVisual() * 2.5f, 0.035f),
                Height = largo,
            },
            Position = (a + b) * 0.5f,
            Basis = OrientarEjeY(dir / largo),
        };
        if (indiceSegmento >= 0) forma.SetMeta("segmento", indiceSegmento);
        _colision.AddChild(forma);
    }

    // =====================================================================
    //  Manijas de doblez (solo cable físico)
    // =====================================================================

    /// <summary>Muestra u oculta las esferitas de agarre de los puntos de doblez.</summary>
    public void MostrarManijas(bool visibles)
    {
        _manijasVisibles = visibles && EsFisico;
        RefrescarManijas();
    }

    private void RefrescarManijas()
    {
        if (_contenedorManijas != null)
        {
            RemoveChild(_contenedorManijas);
            _contenedorManijas.QueueFree();
            _contenedorManijas = null;
        }
        if (!_manijasVisibles || !EsFisico) return;

        _contenedorManijas = new Node3D { Name = "Manijas" };
        AddChild(_contenedorManijas);

        float radio = Mathf.Max(RadioVisual() * 2.2f, 0.045f);
        for (int i = 0; i < PuntosControl.Count; i++)
        {
            var cuerpo = new StaticBody3D
            {
                CollisionLayer = CapasFisicas.Manijas,
                CollisionMask = 0,
                Position = PuntosControl[i],
            };
            cuerpo.SetMeta("manija", i);
            cuerpo.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = radio } });
            cuerpo.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radio * 0.8f, Height = radio * 1.6f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(Colores.Seleccion, 0.85f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            });
            _contenedorManijas.AddChild(cuerpo);
        }
    }

    /// <summary>Si el nodo golpeado por un rayo es una manija de ESTE cable, devuelve su índice.</summary>
    public int? IndiceDeManija(Node? golpeado)
    {
        while (golpeado != null)
        {
            if (golpeado is StaticBody3D cuerpo && cuerpo.HasMeta("manija") &&
                cuerpo.GetParent()?.GetParent() == this)
            {
                return (int)cuerpo.GetMeta("manija");
            }
            golpeado = golpeado.GetParent();
        }
        return null;
    }

    /// <summary>Mueve un punto de doblez y reconstruye el tendido (queda rígido donde lo dejes).</summary>
    public void MoverPunto(int indice, Vector3 posicion)
    {
        if (!EsFisico || indice < 0 || indice >= PuntosControl.Count) return;
        PuntosControl[indice] = posicion;
        Reconstruir();
    }

    /// <summary>Elimina un doblez (el cable se endereza entre sus vecinos). Siempre queda al menos el agarre central.</summary>
    public void EliminarPunto(int indice)
    {
        if (!EsFisico || indice < 0 || indice >= PuntosControl.Count) return;
        PuntosControl.RemoveAt(indice);
        if (PuntosControl.Count == 0)
        {
            PuntosControl.Add((BorneA.GlobalPosition + BorneB.GlobalPosition) * 0.5f);
        }
        Reconstruir();
    }

    /// <summary>
    /// Inserta un doblez nuevo sobre el tramo indicado (índice de la cápsula
    /// clickeada), en el punto del impacto. Devuelve el índice del punto
    /// nuevo, listo para arrastrarlo.
    /// </summary>
    public int InsertarPuntoEnSegmento(int segmento, Vector3 posicion)
    {
        // El tramo i va del punto de polilínea i al i+1; los dobleces ocupan
        // los índices 1..N de la polilínea, así que insertar "después del
        // tramo i" es insertar en PuntosControl[i].
        int indice = Math.Clamp(segmento, 0, PuntosControl.Count);
        PuntosControl.Insert(indice, posicion);
        Reconstruir();
        return indice;
    }

    /// <summary>Cambia el grosor del conductor (mm): tubo, colisión, resistencia, ampacidad y masa térmica, todo junto.</summary>
    public void CambiarGrosor(double grosorMm)
    {
        GrosorMm = Math.Clamp(grosorMm, 0.3, 6.0);
        Reconstruir();
    }

    /// <summary>Cambia el color del cable físico.</summary>
    public void CambiarColor(Color color)
    {
        ColorCable = color;
        if (!Logica.IsBurnt) _material.AlbedoColor = color;
    }

    /// <summary>Base cuya Y local apunta en la dirección pedida (las cápsulas de Godot son verticales).</summary>
    public static Basis OrientarEjeY(Vector3 direccion)
    {
        Vector3 arriba = Mathf.Abs(direccion.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        var mirando = Basis.LookingAt(direccion, arriba);     // -Z apunta a la dirección
        return mirando * new Basis(Vector3.Right, -Mathf.Pi / 2f); // Y -> -Z -> dirección
    }

    /// <summary>
    /// Feedback visual tick a tick: el quemado (carbonizado, ambas variantes)
    /// y, en el cable físico, la INCANDESCENCIA — el color vira hacia un
    /// naranja al rojo vivo a medida que se acerca a los 105 °C del aislante,
    /// el aviso perfecto de "este cable está por morir".
    /// </summary>
    public void ActualizarDesdeLogica()
    {
        if (Logica.IsBurnt)
        {
            if (!_quemadoAplicado)
            {
                _quemadoAplicado = true;
                _material.AlbedoColor = new Color(0.05f, 0.05f, 0.05f);
                _material.EmissionEnabled = false;
            }
            return;
        }

        if (EsFisico)
        {
            float calor = Mathf.Clamp(((float)Logica.Temperatura - 45f) / 60f, 0f, 1f);
            _material.AlbedoColor = calor > 0.01f
                ? ColorCable.Lerp(new Color(1.0f, 0.35f, 0.1f), calor * 0.85f)
                : ColorCable;
        }

        AnimarFlujo();
    }

    /// <summary>
    /// LA CORRIENTE SE VE. El cable late, y late MÁS RÁPIDO cuanto más
    /// corriente lo atraviesa. Es la respuesta a "¿por dónde está pasando?"
    /// sin tener que abrir el inspector de cada cosa, y hace visible algo que
    /// hasta ahora sólo era un número.
    ///
    /// Deliberadamente NO intenta mostrar la propagación interna del solver:
    /// eso exigiría exponer las iteraciones de la relajación, o sea tocar
    /// CircuitGraph. Esto es capa Game pura — el pulso sale de la corriente
    /// que YA calculó el motor, y si lo apagás no cambia un solo número.
    /// </summary>
    private void AnimarFlujo()
    {
        if (!FlujoVisible || _seleccionado)
        {
            if (_flujoAplicado)
            {
                _flujoAplicado = false;
                if (!_seleccionado) _material.EmissionEnabled = EsContacto;
                _material.EmissionEnergyMultiplier = EsContacto ? 0.6f : 0f;
            }
            return;
        }

        double corriente = Math.Abs(Logica.Current);
        if (corriente < UmbralFlujo)
        {
            if (_flujoAplicado)
            {
                _flujoAplicado = false;
                _material.EmissionEnabled = EsContacto;
                _material.EmissionEnergyMultiplier = EsContacto ? 0.6f : 0f;
            }
            return;
        }

        // Escala logarítmica: entre 1 mA y 10 A hay cuatro órdenes de magnitud,
        // y en lineal el 99% de los circuitos quedaría en el mismo pulso.
        float intensidad = Mathf.Clamp((float)(Math.Log10(corriente / UmbralFlujo) / 4.0), 0.05f, 1f);

        _faseFlujo += VelocidadFlujoBase * (0.25f + intensidad);
        if (_faseFlujo > Mathf.Tau) _faseFlujo -= Mathf.Tau;

        float pulso = 0.5f + 0.5f * Mathf.Sin(_faseFlujo);

        _flujoAplicado = true;
        _material.EmissionEnabled = true;
        _material.Emission = EsContacto ? Colores.Contacto : Colores.Flujo;
        _material.EmissionEnergyMultiplier = (EsContacto ? 0.35f : 0.15f) + pulso * intensidad * 0.9f;
    }

    /// <summary>Interruptor global del pulso de corriente. Es puramente visual.</summary>
    public static bool FlujoVisible { get; set; } = true;

    /// <summary>Por debajo de esta corriente (1 mA) el cable no late: es fuga, no flujo.</summary>
    public const double UmbralFlujo = 0.001;

    private static float VelocidadFlujoBase => 0.55f;

    private float _faseFlujo;
    private bool _flujoAplicado;

    public void MostrarSeleccion(bool activo)
    {
        // Se guarda el estado porque el pulso de corriente tiene que cederle el
        // material: si no, el cable seleccionado late en amarillo y celeste a
        // la vez y no se entiende nada.
        _seleccionado = activo;
        _flujoAplicado = false;
        _material.EmissionEnabled = activo;
        _material.Emission = Colores.Seleccion;
        _material.EmissionEnergyMultiplier = activo ? 0.5f : 0f;
        if (EsFisico) MostrarManijas(activo);
    }

    private bool _seleccionado;

    /// <summary>Repara el cable al instante: se le va el carbonizado y vuelve a conducir. (Los físicos pueden quemarse como fusible por ampacidad.)</summary>
    public void Reparar(double temperaturaAmbiente)
    {
        Logica.Reparar(temperaturaAmbiente);
        _quemadoAplicado = false;
        _material.EmissionEnabled = false;
        _material.AlbedoColor = ColorCable;
        ActualizarDesdeLogica();
    }

    /// <summary>Índice del tramo de la polilínea más cercano a un punto del mundo (para insertar dobleces con doble click).</summary>
    public int SegmentoMasCercano(Vector3 punto)
    {
        var poli = new List<Vector3>(PuntosControl.Count + 2) { BorneA.GlobalPosition };
        poli.AddRange(PuntosControl);
        poli.Add(BorneB.GlobalPosition);

        int mejor = 0;
        float mejorDistancia = float.MaxValue;
        for (int i = 0; i < poli.Count - 1; i++)
        {
            Vector3 a = poli[i];
            Vector3 ab = poli[i + 1] - a;
            float t = ab.LengthSquared() < 1e-8f
                ? 0f
                : Mathf.Clamp((punto - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float distancia = punto.DistanceTo(a + ab * t);
            if (distancia < mejorDistancia)
            {
                mejorDistancia = distancia;
                mejor = i;
            }
        }
        return mejor;
    }

    /// <summary>
    /// QUÉ GUARDA UN CABLE. Vive acá a propósito: si mañana le agregás una
    /// propiedad nueva (material, etiqueta, lo que sea), sumás una línea en
    /// este método y otra en CargarEstado, y el sistema de guardado se entera
    /// solo — no hay que tocarlo.
    /// </summary>
    public void GuardarEstado(EstadoGuardado e)
    {
        if (EsFisico)
        {
            e.Poner("grosorMm", GrosorMm);
            e.PonerColor("color", ColorCable);
            e.Poner("puntos", PuntosControl.Select(p => new[] { p.X, p.Y, p.Z }).ToList());
        }
        else
        {
            e.Poner("comba", Comba);
            e.Poner("grosor", Grosor);
            e.Poner("resistencia", Logica.Resistencia);
            e.Poner("limites", new[]
            {
                Logica.MaxVoltage, Logica.MaxCurrent, Logica.MaxPower, Logica.MaxTemperature,
            });
        }
    }

    /// <summary>Restaura lo que guardó GuardarEstado. Lo que no esté en el archivo se queda con su valor por defecto.</summary>
    public void CargarEstado(EstadoGuardado e)
    {
        if (EsFisico)
        {
            CambiarGrosor(e.Numero("grosorMm", GrosorMm));
            CambiarColor(e.ColorDe("color", ColorCable));
            var puntos = e.Vectores("puntos");
            if (puntos.Count > 0)
            {
                PuntosControl.Clear();
                PuntosControl.AddRange(puntos);
            }
        }
        else
        {
            Comba = e.Numero("comba", Comba);
            Grosor = e.Numero("grosor", Grosor);
            Logica.Resistencia = e.Numero("resistencia", Logica.Resistencia);
            var limites = e.Numeros("limites");
            if (limites.Length >= 4)
            {
                Logica.MaxVoltage = limites[0];
                Logica.MaxCurrent = limites[1];
                Logica.MaxPower = limites[2];
                Logica.MaxTemperature = limites[3];
            }
        }
        Reconstruir();
    }

    /// <summary>Saca el cable del motor, de sus bornes y del mundo.</summary>
    public void QuitarSe()
    {
        BorneA.Cables.Remove(this);
        BorneB.Cables.Remove(this);
        _sim.QuitarComponente(Logica);
        QueueFree();
    }
}
