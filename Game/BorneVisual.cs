using Godot;
using ElectricSim.Core;

namespace ElectricSim.Game;

/// <summary>Cómo se muestran las esferas de los bornes en el mundo.</summary>
public enum ModoBornes
{
    /// <summary>Siempre visibles (el modo clásico).</summary>
    Siempre,
    /// <summary>Solo cuando importan: al pasar el mouse por un componente, al seleccionarlo, o mientras se cablea (ahí aparecen TODOS, que son los destinos posibles).</summary>
    AlInteractuar,
    /// <summary>Nunca visibles — pero SIGUEN siendo clickeables (la colisión queda viva): ideal para capturas limpias sin perder funcionalidad.</summary>
    Ocultos,
}

/// <summary>
/// La esferita clickeable de un borne. Es un StaticBody3D en la capa
/// "Bornes" para que el raycast del controlador la encuentre al iniciar o
/// terminar un cable. Su color lo decide la Polaridad del Pin (rojo +,
/// negro −, marrón fase, celeste neutro), así se aprende la convención real
/// mirando el componente.
///
/// VISIBILIDAD: responde al ModoBornes global (elegible desde la barra).
/// En "Al interactuar" cada borne tiene además una "pista" que el
/// controlador prende cuando su componente está bajo el mouse, seleccionado,
/// o cuando hay un cableado en curso. La COLISIÓN nunca se apaga: un borne
/// oculto se puede clickear igual si sabés dónde está.
/// </summary>
public partial class BorneVisual : StaticBody3D
{
    // ------------------------------------------------------------------
    //  Modo global (compartido por todos los bornes del mundo)
    // ------------------------------------------------------------------

    /// <summary>El modo actual. Cambialo con CambiarModo, que avisa a todas las esferas vivas.</summary>
    public static ModoBornes ModoGlobal { get; private set; } = ModoBornes.Siempre;

    private static event Action? ModoGlobalCambiado;

    /// <summary>Cambia el modo de visibilidad de TODOS los bornes (lo llama la barra superior).</summary>
    public static void CambiarModo(ModoBornes modo)
    {
        ModoGlobal = modo;
        ModoGlobalCambiado?.Invoke();
    }

    // ------------------------------------------------------------------

    /// <summary>El pin eléctrico real del motor al que representa esta esfera.</summary>
    public Pin Pin { get; private set; } = null!;

    /// <summary>El componente visual dueño de este borne.</summary>
    public ComponenteVisual? Dueno { get; private set; }

    /// <summary>Cables visuales conectados a este borne (para redibujarlos si algo se mueve).</summary>
    public List<CableVisual> Cables { get; } = new();

    private MeshInstance3D _esfera = null!;
    private StandardMaterial3D _material = null!;
    private Label3D? _etiqueta;
    private Color _colorEtiquetaBase = new(1f, 1f, 1f, 0.92f);
    private bool _pista;     // "este borne importa ahora" (hover/selección/cableando)
    private bool _resaltado; // mouse directamente encima

    /// <param name="escala">Tamaño relativo de la esfera (1 = normal). Los componentes
    /// con muchos bornes apretados (el Arduino y sus 24) los usan más chicos.</param>
    public static BorneVisual Crear(Pin pin, ComponenteVisual? dueno, float escala = 1f)
    {
        var borne = new BorneVisual
        {
            Pin = pin,
            Dueno = dueno,
            Name = $"Borne_{pin.Label}",
            CollisionLayer = CapasFisicas.Bornes,
            CollisionMask = 0,
        };

        borne._material = new StandardMaterial3D
        {
            AlbedoColor = Colores.PorPolaridad(pin.Polaridad),
            Roughness = 0.35f,
            Metallic = 0.4f,
            EmissionEnabled = true,
            Emission = Colores.PorPolaridad(pin.Polaridad),
            EmissionEnergyMultiplier = 0.0f, // se prende al pasar el mouse
        };

        borne._esfera = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.045f * escala, Height = 0.09f * escala, RadialSegments = 16, Rings = 8 },
            MaterialOverride = borne._material,
        };
        borne.AddChild(borne._esfera);

        // La esfera de colisión es más grande que la visible: más fácil de
        // clickear. (Y NUNCA se apaga: en modo Ocultos se cablea "a ciegas".)
        var colision = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.08f * escala } };
        borne.AddChild(colision);

        borne.ActualizarVisibilidad();
        return borne;
    }

    public override void _Ready() => ModoGlobalCambiado += ActualizarVisibilidad;

    public override void _ExitTree() => ModoGlobalCambiado -= ActualizarVisibilidad;

    /// <summary>Asocia el cartelito flotante al borne para escalarlo al pasar el mouse y ocultarlo con el modo de bornes.</summary>
    public void VincularEtiqueta(Label3D etiqueta)
    {
        _etiqueta = etiqueta;
        _colorEtiquetaBase = etiqueta.Modulate;
        ActualizarVisibilidad();
    }

    /// <summary>El controlador marca que este borne importa ahora (hover, selección o cableado en curso).</summary>
    public void MarcarPista(bool activa)
    {
        _pista = activa;
        ActualizarVisibilidad();
    }

    /// <summary>Realce al pasar el mouse por encima (feedback de "esto se puede cablear").</summary>
    public void Resaltar(bool activo)
    {
        _resaltado = activo;
        _esfera.Scale = Vector3.One * (activo ? 1.45f : 1f);
        _material.EmissionEnergyMultiplier = activo ? 0.9f : 0f;
        if (_etiqueta != null)
        {
            _etiqueta.Scale = Vector3.One * (activo ? 1.45f : 1f);
            _etiqueta.Modulate = activo ? new Color(1f, 0.92f, 0.35f, 1f) : _colorEtiquetaBase;
        }
        ActualizarVisibilidad();
    }

    private void ActualizarVisibilidad()
    {
        bool visible = ModoGlobal switch
        {
            ModoBornes.Siempre => true,
            ModoBornes.Ocultos => false,
            // Al interactuar: visible si su componente está en foco, si hay un
            // cableado en curso (pista), o si el mouse lo está tocando justo.
            ModoBornes.AlInteractuar => _pista || _resaltado,
            _ => true,
        };
        _esfera.Visible = visible;
        if (_etiqueta != null) _etiqueta.Visible = visible;
    }
}
