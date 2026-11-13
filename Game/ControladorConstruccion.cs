using Godot;

namespace ElectricSim.Game;

/// <summary>
/// El cerebro de la interacción con el mundo. Es una máquina de estados:
///
///  NORMAL     -> click en borne: empieza un cable. Click en un cuerpo:
///                selecciona (y acciona interruptores/pulsadores). Arrastrar
///                un cuerpo: lo mueve por el piso. Click en cable: lo
///                selecciona para editarlo.
///  COLOCANDO  -> hay un "fantasma" azulado siguiendo el mouse; click en el
///                piso lo materializa. Con Shift se colocan varios seguidos.
///  CABLEANDO  -> hay un cable elástico desde un borne siguiendo el mouse;
///                click en OTRO borne lo tiende de verdad.
///  MOVIENDO   -> un componente viaja pegado al mouse hasta soltar el botón.
///
/// R rota (el fantasma o la selección), Supr elimina, Esc/click derecho
/// cancela el modo actual. Los raycasts usan las capas físicas de
/// Constantes.cs para chocar SOLO contra lo que le interesa a cada estado.
/// </summary>
public partial class ControladorConstruccion : Node
{
    private enum Estado { Normal, Colocando, Cableando, Moviendo, DoblandoCable }

    /// <summary>Todos los componentes visuales vivos (para refrescarlos por tick).</summary>
    public List<ComponenteVisual> Visuales { get; } = new();

    /// <summary>Todos los cables visuales vivos.</summary>
    public List<CableVisual> Cables { get; } = new();

    /// <summary>Ajustar la colocación a una grilla de 10cm (configurable desde la barra).</summary>
    public bool SnapActivado { get; set; } = true;

    /// <summary>Tipo del PRÓXIMO cable a tender: false = simple (tubo liviano), true = FÍSICO (rígido, doblable, con grosor). Se alterna con la tecla C.</summary>
    public bool CableFisicoPorDefecto { get; private set; }

    /// <summary>Se dispara cuando cambia la selección (ComponenteVisual, CableVisual o null).</summary>
    public event Action<GodotObject?>? SeleccionCambiada;

    /// <summary>Texto del modo actual, para el cartelito de la barra superior.</summary>
    public event Action<string>? ModoCambiado;

    private Simulacion _sim = null!;
    private Camera3D _camara = null!;
    private Node3D _mundo = null!;

    private Estado _estado = Estado.Normal;
    private GodotObject? _seleccion;

    // Colocando:
    private DefinicionComponente? _defColocando;
    private Node3D? _fantasma;
    private float _rotacionFantasma;
    private float _volcadoFantasma;

    // Visibilidad de bornes "Al interactuar": el componente bajo el mouse.
    private ComponenteVisual? _visualEnPista;

    // Cableando:
    private BorneVisual? _borneOrigen;
    private MeshInstance3D? _previewCable;
    private BorneVisual? _borneResaltado;

    // Doblando un cable físico (arrastrando una de sus manijas):
    private CableVisual? _cableDoblando;
    private int _indiceManija;
    private Vector3 _ultimoPuntoManija;

    // Moviendo (y distinción click vs arrastre):
    private ComponenteVisual? _candidatoArrastre;
    private Vector2 _posClickInicial;
    private const float UmbralArrastrePx = 7f;

    // Altura de lo que está "en la mano" (el fantasma al colocar, o el
    // componente durante un arrastre). Se controla con la rueda del mouse o
    // RePág/AvPág; Inicio lo apoya de vuelta en el piso. Así nada queda
    // condenado a hundirse en la mesa: los objetos flotan a la altura que
    // el usuario quiera.
    private float _altura;
    private const float PasoAltura = 0.05f;
    private const float PasoAlturaFino = 0.01f;

    public void Init(Simulacion sim, Camera3D camara, Node3D mundo)
    {
        _sim = sim;
        _camara = camara;
        _mundo = mundo;
    }

    // =====================================================================
    //  Entradas
    // =====================================================================

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton boton:
                ManejarBoton(boton);
                break;
            case InputEventMouseMotion movimiento:
                ManejarMovimiento(movimiento);
                break;
            case InputEventKey tecla when tecla.Pressed && !tecla.Echo:
                ManejarTecla(tecla);
                break;
        }
    }

    private void ManejarBoton(InputEventMouseButton boton)
    {
        // La rueda, MIENTRAS hay algo en la mano, sube y baja en vez de hacer
        // zoom (este nodo procesa el input antes que la cámara, así que el
        // SetInputAsHandled se lo esconde). En estado Normal la rueda sigue
        // siendo de la cámara, intacta.
        if (boton.Pressed &&
            (_estado == Estado.Colocando || _estado == Estado.Moviendo || _estado == Estado.DoblandoCable) &&
            (boton.ButtonIndex == MouseButton.WheelUp || boton.ButtonIndex == MouseButton.WheelDown))
        {
            float paso = boton.ShiftPressed ? PasoAlturaFino : PasoAltura;
            AjustarAltura(boton.ButtonIndex == MouseButton.WheelUp ? paso : -paso);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Click DERECHO sobre una esferita de doblez: se borra ese doblez (el
        // cable se endereza). Solo robamos el botón derecho de la cámara si el
        // rayo pegó de verdad en una manija.
        if (boton.ButtonIndex == MouseButton.Right && boton.Pressed && _estado == Estado.Normal &&
            Raycast(boton.Position, CapasFisicas.Manijas) is Node3D manijaBorrar &&
            BuscarDueno<CableVisual>(manijaBorrar) is CableVisual cableBorrar &&
            cableBorrar.IndiceDeManija(manijaBorrar) is int indiceBorrar)
        {
            cableBorrar.EliminarPunto(indiceBorrar);
            GetViewport().SetInputAsHandled();
            return;
        }

        // DOBLE click sobre un cable físico: se agrega un doblez justo ahí,
        // listo para arrastrar.
        if (boton.ButtonIndex == MouseButton.Left && boton.Pressed && boton.DoubleClick &&
            _estado == Estado.Normal &&
            RaycastConPosicion(boton.Position, CapasFisicas.Cables) is (Node3D golpeado, Vector3 impacto) &&
            BuscarDueno<CableVisual>(golpeado) is CableVisual cableDoble && cableDoble.EsFisico)
        {
            cableDoble.InsertarPuntoEnSegmento(cableDoble.SegmentoMasCercano(impacto), impacto);
            Seleccionar(cableDoble); // deja las manijas a la vista
            GetViewport().SetInputAsHandled();
            return;
        }

        if (boton.ButtonIndex == MouseButton.Right && boton.Pressed && _estado != Estado.Normal)
        {
            // La cámara también usa el botón derecho: solo lo "robamos" para
            // cancelar cuando hay un modo activo.
            CancelarModo();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (boton.ButtonIndex != MouseButton.Left) return;

        if (boton.Pressed)
        {
            ClickIzquierdo(boton.Position);
        }
        else
        {
            SoltarIzquierdo(boton.Position);
        }
    }

    private void ClickIzquierdo(Vector2 posPantalla)
    {
        switch (_estado)
        {
            case Estado.Colocando:
                ColocarComponente(posPantalla);
                GetViewport().SetInputAsHandled();
                break;

            case Estado.Cableando:
                IntentarTerminarCable(posPantalla);
                GetViewport().SetInputAsHandled();
                break;

            case Estado.Normal:
                // Prioridad: manija de doblez > borne (cablear) > cuerpo > cable.
                if (Raycast(posPantalla, CapasFisicas.Manijas) is Node3D manija &&
                    BuscarDueno<CableVisual>(manija) is CableVisual cableManija &&
                    cableManija.IndiceDeManija(manija) is int indiceManija)
                {
                    _cableDoblando = cableManija;
                    _indiceManija = indiceManija;
                    _ultimoPuntoManija = cableManija.PuntosControl[indiceManija];
                    _altura = _ultimoPuntoManija.Y;
                    Seleccionar(cableManija);
                    CambiarEstado(Estado.DoblandoCable);
                    GetViewport().SetInputAsHandled();
                }
                else if (Raycast(posPantalla, CapasFisicas.Bornes) is BorneVisual borne)
                {
                    IniciarCable(borne);
                    GetViewport().SetInputAsHandled();
                }
                else if (BuscarDueno<ComponenteVisual>(Raycast(posPantalla, CapasFisicas.Cuerpos)) is ComponenteVisual visual)
                {
                    // Todavía no sabemos si es un click (seleccionar/accionar)
                    // o el comienzo de un arrastre: lo decide el movimiento.
                    _candidatoArrastre = visual;
                    _posClickInicial = posPantalla;
                    GetViewport().SetInputAsHandled();
                }
                else if (BuscarDueno<CableVisual>(Raycast(posPantalla, CapasFisicas.Cables)) is CableVisual cable)
                {
                    Seleccionar(cable);
                    GetViewport().SetInputAsHandled();
                }
                else
                {
                    Seleccionar(null); // click al vacío: deselecciona
                }
                break;
        }
    }

    private void SoltarIzquierdo(Vector2 posPantalla)
    {
        if (_estado == Estado.DoblandoCable)
        {
            // El doblez queda RÍGIDO donde lo soltaste; el cable sigue seleccionado.
            _cableDoblando = null;
            CambiarEstado(Estado.Normal);
            return;
        }

        if (_estado == Estado.Moviendo)
        {
            CambiarEstado(Estado.Normal);
            _candidatoArrastre = null;
            GeometriaCambiada();   // lo soltaste en otro lado: puede tocar (o dejar de tocar) bornes
            return;
        }

        if (_candidatoArrastre != null)
        {
            // Nunca llegó a arrastrarse: fue un click común -> seleccionar y accionar.
            Seleccionar(_candidatoArrastre);
            _candidatoArrastre.AlternarInteraccion();
            _candidatoArrastre = null;
        }
    }

    private void ManejarMovimiento(InputEventMouseMotion movimiento)
    {
        switch (_estado)
        {
            case Estado.Colocando:
                if (_fantasma != null && PuntoEnPiso(movimiento.Position) is Vector3 p)
                {
                    _fantasma.GlobalPosition = AplicarSnap(p);
                }
                break;

            case Estado.Cableando:
                ActualizarPreviewCable(movimiento.Position);
                ResaltarBorneBajoMouse(movimiento.Position);
                break;

            case Estado.Moviendo:
                if (_candidatoArrastre != null && PuntoEnPiso(movimiento.Position) is Vector3 destino)
                {
                    _candidatoArrastre.Mover(AplicarSnap(destino));
                }
                break;

            case Estado.DoblandoCable:
                if (_cableDoblando != null && GodotObject.IsInstanceValid(_cableDoblando) &&
                    PuntoEnPiso(movimiento.Position) is Vector3 plano)
                {
                    // El doblez viaja en el plano horizontal a su altura; la
                    // altura se cambia con la rueda / RePág / AvPág. Sin snap:
                    // doblar pide pulso libre.
                    _ultimoPuntoManija = new Vector3(plano.X, _altura, plano.Z);
                    _cableDoblando.MoverPunto(_indiceManija, _ultimoPuntoManija);
                }
                break;

            case Estado.Normal:
                // ¿Empieza un arrastre?
                if (_candidatoArrastre != null &&
                    movimiento.Position.DistanceTo(_posClickInicial) > UmbralArrastrePx)
                {
                    // El componente arranca el viaje A SU ALTURA actual: uno
                    // que estaba flotando sigue flotando mientras se mueve.
                    _altura = Mathf.Max(0, _candidatoArrastre.GlobalPosition.Y);
                    Seleccionar(_candidatoArrastre);
                    CambiarEstado(Estado.Moviendo);
                }
                ResaltarBorneBajoMouse(movimiento.Position);
                ActualizarPistaHover(movimiento.Position);
                break;
        }
    }

    private void ManejarTecla(InputEventKey tecla)
    {
        switch (tecla.Keycode)
        {
            case Key.R:
                // R: giro rápido de 45°. Shift+R: giro fino de 15°.
                RotarSeleccion(tecla.ShiftPressed ? 15 : 45);
                break;
            case Key.T:
                // T: volcar 90° (acostarlo, pararlo de canto, darlo vuelta).
                VolcarSeleccion();
                break;
            case Key.Pageup:
                // RePág/AvPág: subir y bajar lo que esté en la mano O la
                // selección quieta. Shift = paso fino de 1cm.
                AjustarAltura(tecla.ShiftPressed ? PasoAlturaFino : PasoAltura);
                break;
            case Key.Pagedown:
                AjustarAltura(tecla.ShiftPressed ? -PasoAlturaFino : -PasoAltura);
                break;
            case Key.Home:
                AjustarAltura(-999f);   // Inicio: al piso de un saque (el clamp hace el resto)
                break;
            case Key.C:
                // C: alterna el tipo del PRÓXIMO cable (simple <-> físico).
                CableFisicoPorDefecto = !CableFisicoPorDefecto;
                ModoCambiado?.Invoke(CableFisicoPorDefecto
                    ? "Próximo cable: FÍSICO — trayectoria rígida con dobleces, grosor, color y ampacidad (C cambia a simple)"
                    : "Próximo cable: SIMPLE — conexión ideal directa entre bornes (C cambia a físico)");
                break;
            case Key.Delete:
                EliminarSeleccion();
                break;
            case Key.Escape:
                if (_estado != Estado.Normal) CancelarModo();
                else Seleccionar(null);
                break;
        }
    }

    // =====================================================================
    //  Acciones públicas (también las usa la interfaz)
    // =====================================================================

    /// <summary>La barra superior eligió un componente: entra en modo colocación con fantasma.</summary>
    public void IniciarColocacion(DefinicionComponente def)
    {
        CancelarModo();
        _defColocando = def;
        _rotacionFantasma = 0;
        _volcadoFantasma = 0;
        _altura = 0;
        _fantasma = ComponenteVisual.CrearFantasma(def);
        _mundo.AddChild(_fantasma);
        _fantasma.Visible = false; // hasta que el mouse toque el piso
        CambiarEstado(Estado.Colocando);
    }

    public void RotarSeleccion(float grados = 45)
    {
        if (_estado == Estado.Colocando && _fantasma != null)
        {
            _rotacionFantasma += grados;
            _fantasma.RotationDegrees = new Vector3(_volcadoFantasma, _rotacionFantasma, 0);
        }
        else if (_seleccion is ComponenteVisual visual)
        {
            visual.RotarY(grados);
            GeometriaCambiada();
        }
    }

    /// <summary>Vuelca 90° el fantasma o la selección (tecla T).</summary>
    public void VolcarSeleccion()
    {
        if (_estado == Estado.Colocando && _fantasma != null)
        {
            _volcadoFantasma = (_volcadoFantasma + 90) % 360;
            _fantasma.RotationDegrees = new Vector3(_volcadoFantasma, _rotacionFantasma, 0);
        }
        else if (_seleccion is ComponenteVisual visual)
        {
            visual.Volcar(90);
            GeometriaCambiada();
        }
    }

    /// <summary>
    /// Sube o baja (delta en metros) lo que corresponda según el estado: el
    /// fantasma al colocar, el componente durante un arrastre, o la selección
    /// quieta en modo Normal. El piso (Y=0) es el límite de abajo — para
    /// hundir algo a propósito está el campo Y del inspector, que no juzga.
    /// </summary>
    public void AjustarAltura(float delta)
    {
        switch (_estado)
        {
            case Estado.Colocando when _fantasma != null:
                _altura = Mathf.Max(0, _altura + delta);
                if (_fantasma.Visible)
                {
                    var f = _fantasma.GlobalPosition;
                    _fantasma.GlobalPosition = new Vector3(f.X, _altura, f.Z);
                }
                break;

            case Estado.Moviendo when _candidatoArrastre != null:
                _altura = Mathf.Max(0, _altura + delta);
                var p = _candidatoArrastre.GlobalPosition;
                _candidatoArrastre.Mover(new Vector3(p.X, _altura, p.Z));
                break;

            case Estado.DoblandoCable when _cableDoblando != null && GodotObject.IsInstanceValid(_cableDoblando):
                _altura = Mathf.Max(0, _altura + delta);
                _ultimoPuntoManija = new Vector3(_ultimoPuntoManija.X, _altura, _ultimoPuntoManija.Z);
                _cableDoblando.MoverPunto(_indiceManija, _ultimoPuntoManija);
                break;

            case Estado.Normal when _seleccion is ComponenteVisual visual:
                var pos = visual.GlobalPosition;
                visual.Mover(new Vector3(pos.X, Mathf.Max(0, pos.Y + delta), pos.Z));
                GeometriaCambiada();   // quieto y seleccionado: el cambio de altura ya es definitivo
                break;
        }
    }

    public void EliminarSeleccion()
    {
        switch (_seleccion)
        {
            case ComponenteVisual visual:
                foreach (var cable in visual.CablesConectados())
                {
                    Cables.Remove(cable);
                }
                Visuales.Remove(visual);
                if (_visualEnPista == visual) _visualEnPista = null;
                visual.Eliminar();
                GeometriaCambiada();   // se fue: soltar los contactos que tenía
                break;

            case CableVisual cable:
                // Un contacto no se borra: lo mantiene la geometría, así que
                // volvería solo en el próximo recálculo y parecería un bug.
                // Para soltarlo hay que SEPARAR los componentes.
                if (cable.EsContacto) return;
                Cables.Remove(cable);
                cable.QuitarSe();
                break;

            default:
                return;
        }
        Seleccionar(null);
    }

    /// <summary>Borra TODO el circuito (componentes y cables). Para cargar un proyecto nuevo encima.</summary>
    public void LimpiarCircuito()
    {
        CancelarModo();
        Seleccionar(null);
        // Los cables PRIMERO (todavía con sus bornes vivos), después los cuerpos.
        foreach (var cable in Cables.ToArray())
        {
            if (GodotObject.IsInstanceValid(cable)) cable.QuitarSe();
        }
        Cables.Clear();
        foreach (var visual in Visuales.ToArray())
        {
            if (GodotObject.IsInstanceValid(visual)) visual.Eliminar();
        }
        Visuales.Clear();
        _visualEnPista = null;
        _candidatoArrastre = null;
    }

    /// <summary>Crea un componente desde datos guardados (posición + rotación) y lo registra. Devuelve el visual, para poder engancharle sus cables.</summary>
    public ComponenteVisual CrearComponenteCargado(DefinicionComponente def, Vector3 posicion, Vector3 rotacionGrados)
    {
        var visual = ComponenteVisual.Crear(def, _sim, posicion, _mundo);
        visual.RotationDegrees = rotacionGrados;
        Visuales.Add(visual);
        return visual;
    }

    /// <summary>Tiende un cable entre dos bornes ya existentes (para cargar un proyecto). Devuelve el cable.</summary>
    public CableVisual CrearCableCargado(BorneVisual a, BorneVisual b, bool fisico)
    {
        var cable = CableVisual.Crear(_sim, a, b, _mundo, fisico);
        Cables.Add(cable);
        return cable;
    }

    /// <summary>
    /// Avisa que cambió la GEOMETRÍA del tablero (se colocó, movió, rotó o
    /// borró algo) y hay que rever qué bornes están tocándose. Es O(n²) sobre
    /// los bornes, así que se llama en estos momentos y NO cada frame.
    /// </summary>
    public void GeometriaCambiada() => ContactoEntreBornes.Recalcular(this);

    // =====================================================================
    //  Interno
    // =====================================================================

    private void ColocarComponente(Vector2 posPantalla)
    {
        if (_defColocando == null || PuntoEnPiso(posPantalla) is not Vector3 punto) return;

        var visual = ComponenteVisual.Crear(_defColocando, _sim, AplicarSnap(punto), _mundo);
        visual.RotationDegrees = new Vector3(_volcadoFantasma, _rotacionFantasma, 0);
        // Primero a la lista y DESPUÉS el recálculo: el recálculo recorre
        // Visuales, y si el recién colocado todavía no está ahí, sus bornes
        // no participan y nunca conectan por contacto al colocarlo.
        Visuales.Add(visual);
        GeometriaCambiada();   // recién colocado: puede estar tocando algo
        Seleccionar(visual);

        // Con Shift apretado se sigue colocando en cadena.
        if (!Input.IsKeyPressed(Key.Shift))
        {
            CancelarModo();
        }
    }

    private void IniciarCable(BorneVisual origen)
    {
        _borneOrigen = origen;
        origen.Resaltar(true);

        _previewCable = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 1f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(Colores.PorPolaridad(origen.Pin.Polaridad), 0.6f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _mundo.AddChild(_previewCable);
        CambiarEstado(Estado.Cableando);
    }

    private void IntentarTerminarCable(Vector2 posPantalla)
    {
        if (_borneOrigen == null) return;
        // El usuario puede borrar el componente de origen (Supr) MIENTRAS
        // arrastra el cable: el nodo queda liberado y tocarlo revienta.
        if (!GodotObject.IsInstanceValid(_borneOrigen))
        {
            _borneOrigen = null;
            CancelarModo();
            return;
        }

        if (Raycast(posPantalla, CapasFisicas.Bornes) is BorneVisual destino && destino != _borneOrigen)
        {
            var cable = CableVisual.Crear(_sim, _borneOrigen, destino, _mundo, CableFisicoPorDefecto);
            Cables.Add(cable);
            Seleccionar(cable);
            CancelarModo();
        }
        else if (Raycast(posPantalla, CapasFisicas.Bornes) == null)
        {
            CancelarModo(); // click al vacío: se descarta el cable
        }
    }

    /// <summary>Estira el cilindro de vista previa desde el borne de origen hasta el mouse.</summary>
    private void ActualizarPreviewCable(Vector2 posPantalla)
    {
        if (_previewCable == null || _borneOrigen == null) return;
        if (!GodotObject.IsInstanceValid(_borneOrigen))
        {
            _borneOrigen = null;   // ídem: el origen pudo haber sido borrado
            CancelarModo();
            return;
        }

        Vector3 desde = _borneOrigen.GlobalPosition;
        Vector3 hasta;
        if (Raycast(posPantalla, CapasFisicas.Bornes) is BorneVisual borne && borne != _borneOrigen)
        {
            hasta = borne.GlobalPosition; // imantado al borne bajo el mouse
        }
        else if (PuntoEnPiso(posPantalla) is Vector3 piso)
        {
            hasta = piso + Vector3.Up * 0.05f;
        }
        else return;

        Vector3 dir = hasta - desde;
        float largo = dir.Length();
        _previewCable.Visible = largo > 0.02f;
        if (!_previewCable.Visible) return;

        ((CylinderMesh)_previewCable.Mesh).Height = largo;
        _previewCable.GlobalPosition = (desde + hasta) * 0.5f;
        _previewCable.Basis = CableVisual.OrientarEjeY(dir / largo);
    }

    private void ResaltarBorneBajoMouse(Vector2 posPantalla)
    {
        var borne = Raycast(posPantalla, CapasFisicas.Bornes) as BorneVisual;
        if (borne == _borneResaltado) return;

        // Ojo de no apagar el resaltado del borne de origen del cable en curso.
        if (_borneResaltado != null && _borneResaltado != _borneOrigen && GodotObject.IsInstanceValid(_borneResaltado))
        {
            _borneResaltado.Resaltar(false);
        }
        _borneResaltado = borne;
        _borneResaltado?.Resaltar(true);
    }

    private void CancelarModo()
    {
        _fantasma?.QueueFree();
        _fantasma = null;
        _defColocando = null;

        _previewCable?.QueueFree();
        _previewCable = null;
        if (_borneOrigen != null && GodotObject.IsInstanceValid(_borneOrigen))
        {
            _borneOrigen.Resaltar(false);
        }
        _borneOrigen = null;

        _candidatoArrastre = null;
        _cableDoblando = null;
        CambiarEstado(Estado.Normal);
    }

    private void Seleccionar(GodotObject? nuevo)
    {
        // Apagar el realce del anterior.
        switch (_seleccion)
        {
            case ComponenteVisual v when GodotObject.IsInstanceValid(v):
                v.MostrarSeleccion(false);
                break;
            case CableVisual c when GodotObject.IsInstanceValid(c):
                c.MostrarSeleccion(false);
                break;
        }

        _seleccion = nuevo;

        switch (_seleccion)
        {
            case ComponenteVisual v:
                v.MostrarSeleccion(true);
                break;
            case CableVisual c:
                c.MostrarSeleccion(true);
                break;
        }

        SeleccionCambiada?.Invoke(_seleccion);
        RefrescarPistas();
    }

    private void CambiarEstado(Estado nuevo)
    {
        _estado = nuevo;
        RefrescarPistas();
        ModoCambiado?.Invoke(nuevo switch
        {
            Estado.Colocando => $"Colocando: {_defColocando?.Nombre} (R rota · T vuelca · rueda o RePág/AvPág sube y baja · Shift encadena · Esc cancela)",
            Estado.Cableando => "Cableando: haga clic en otro borne (Esc cancela)",
            Estado.Moviendo => "Moviendo (rueda o RePág/AvPág ajusta altura · Inicio apoya en la mesa)",
            Estado.DoblandoCable => "Doblando cable: suelte para fijar la posición (rueda o RePág/AvPág ajusta altura)",
            _ => "",
        });
    }

    // =====================================================================
    //  Pistas de bornes (modo de visibilidad "Al interactuar")
    // =====================================================================

    /// <summary>Marca como "en pista" los bornes del componente bajo el mouse.</summary>
    private void ActualizarPistaHover(Vector2 posPantalla)
    {
        var visual = BuscarDueno<ComponenteVisual>(Raycast(posPantalla, CapasFisicas.Cuerpos));
        if (visual == _visualEnPista) return;
        _visualEnPista = visual;
        RefrescarPistas();
    }

    /// <summary>
    /// Recalcula qué bornes "importan" ahora: TODOS mientras se cablea (son
    /// los destinos posibles), y si no, los del componente bajo el mouse y
    /// los del seleccionado. Solo tiene efecto visible en el modo de bornes
    /// "Al interactuar" — en "Siempre" y "Ocultos" la esfera no mira la pista.
    /// </summary>
    private void RefrescarPistas()
    {
        bool todos = _estado == Estado.Cableando;
        foreach (var visual in Visuales)
        {
            if (!GodotObject.IsInstanceValid(visual)) continue;
            bool activa = todos || visual == _visualEnPista || ReferenceEquals(visual, _seleccion);
            visual.MarcarPistaBornes(activa);
        }
    }

    // =====================================================================
    //  Rayos
    // =====================================================================

    /// <summary>Dispara un rayo desde la cámara por la posición del mouse contra UNA capa física y devuelve el nodo golpeado.</summary>
    private Node3D? Raycast(Vector2 posPantalla, uint mascara)
    {
        Vector3 origen = _camara.ProjectRayOrigin(posPantalla);
        Vector3 destino = origen + _camara.ProjectRayNormal(posPantalla) * 200f;

        var consulta = PhysicsRayQueryParameters3D.Create(origen, destino, mascara);
        var resultado = _camara.GetWorld3D().DirectSpaceState.IntersectRay(consulta);

        if (resultado.Count == 0) return null;
        return resultado["collider"].As<Node3D>();
    }

    /// <summary>Como Raycast, pero devolviendo también el PUNTO exacto del impacto (para insertar dobleces donde se hizo click).</summary>
    private (Node3D nodo, Vector3 punto)? RaycastConPosicion(Vector2 posPantalla, uint mascara)
    {
        Vector3 origen = _camara.ProjectRayOrigin(posPantalla);
        Vector3 destino = origen + _camara.ProjectRayNormal(posPantalla) * 200f;

        var consulta = PhysicsRayQueryParameters3D.Create(origen, destino, mascara);
        var resultado = _camara.GetWorld3D().DirectSpaceState.IntersectRay(consulta);

        if (resultado.Count == 0) return null;
        return (resultado["collider"].As<Node3D>(), resultado["position"].As<Vector3>());
    }

    /// <summary>Sube por el árbol desde el nodo golpeado hasta encontrar el dueño del tipo pedido.</summary>
    private static T? BuscarDueno<T>(Node? nodo) where T : Node
    {
        while (nodo != null)
        {
            if (nodo is T objetivo) return objetivo;
            nodo = nodo.GetParent();
        }
        return null;
    }

    /// <summary>Intersección del rayo del mouse con el plano del piso (Y=0).</summary>
    private Vector3? PuntoEnPiso(Vector2 posPantalla)
    {
        Vector3 origen = _camara.ProjectRayOrigin(posPantalla);
        Vector3 direccion = _camara.ProjectRayNormal(posPantalla);
        var piso = new Plane(Vector3.Up, 0);
        var punto = piso.IntersectsRay(origen, direccion);

        if (punto is Vector3 p && _fantasma != null)
        {
            _fantasma.Visible = true;
        }
        return punto;
    }

    private Vector3 AplicarSnap(Vector3 punto)
    {
        // El snap es SOLO de la grilla horizontal: la Y la manda la altura
        // "en la mano" (rueda / RePág / AvPág), nunca se aplasta al piso.
        float x = SnapActivado ? Mathf.Round(punto.X / 0.1f) * 0.1f : punto.X;
        float z = SnapActivado ? Mathf.Round(punto.Z / 0.1f) * 0.1f : punto.Z;
        return new Vector3(x, _altura, z);
    }
}
