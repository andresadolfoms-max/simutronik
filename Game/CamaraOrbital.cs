using Godot;

namespace ElectricSim.Game;

/// <summary>
/// Cámara libre estilo editor 3D, sin restricciones raras:
///
///  - BOTÓN DERECHO + mover  -> orbitar alrededor del punto de interés
///  - BOTÓN DEL MEDIO + mover -> desplazar el punto de interés (pan)
///  - RUEDA                   -> acercar / alejar
///  - W A S D                 -> desplazarse por el piso
///
/// Es un pivote (este Node3D) con la Camera3D real colgando a cierta
/// distancia; orbitar es rotar el pivote, hacer pan es moverlo. El ángulo
/// vertical se limita para no meterse abajo del piso ni quedar en cenital
/// perfecto (donde orbitar marea).
/// </summary>
public partial class CamaraOrbital : Node3D
{
    public Camera3D Camara { get; private set; } = null!;

    private float _distancia = 7f;
    private float _anguloHorizontal = 35f;   // grados
    private float _anguloVertical = 42f;     // grados
    private bool _orbitando;
    private bool _paneando;

    private const float SensibilidadOrbita = 0.35f;
    private const float SensibilidadPan = 0.012f;
    private const float VelocidadTeclas = 5.5f;

    public override void _Ready()
    {
        Position = new Vector3(0, 0, 0);
        Camara = new Camera3D { Current = true };
        AddChild(Camara);
        ActualizarCamara();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton boton:
                switch (boton.ButtonIndex)
                {
                    case MouseButton.Right:
                        _orbitando = boton.Pressed;
                        break;
                    case MouseButton.Middle:
                        _paneando = boton.Pressed;
                        break;
                    case MouseButton.WheelUp when boton.Pressed:
                        _distancia = Mathf.Clamp(_distancia * 0.9f, 1.5f, 30f);
                        ActualizarCamara();
                        break;
                    case MouseButton.WheelDown when boton.Pressed:
                        _distancia = Mathf.Clamp(_distancia * 1.1f, 1.5f, 30f);
                        ActualizarCamara();
                        break;
                }
                break;

            case InputEventMouseMotion movimiento when _orbitando:
                _anguloHorizontal -= movimiento.Relative.X * SensibilidadOrbita;
                _anguloVertical = Mathf.Clamp(
                    _anguloVertical + movimiento.Relative.Y * SensibilidadOrbita, 12f, 85f);
                ActualizarCamara();
                break;

            case InputEventMouseMotion movimiento2 when _paneando:
                Panear(movimiento2.Relative);
                break;
        }
    }

    public override void _Process(double delta)
    {
        // Si el usuario está tipeando en un SpinBox/LineEdit del inspector,
        // WASD es texto, no cámara. Sin este guard, editar "watts" pasea la
        // mesa por toda la pantalla.
        if (GetViewport().GuiGetFocusOwner() != null) return;

        // WASD desplaza el punto de interés en el plano del piso, siguiendo
        // hacia dónde mira la cámara.
        Vector2 entrada = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) entrada.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) entrada.Y += 1;
        if (Input.IsKeyPressed(Key.A)) entrada.X -= 1;
        if (Input.IsKeyPressed(Key.D)) entrada.X += 1;
        if (entrada == Vector2.Zero) return;

        entrada = entrada.Normalized();
        float rad = Mathf.DegToRad(_anguloHorizontal);
        var adelante = new Vector3(-Mathf.Sin(rad), 0, -Mathf.Cos(rad));
        var derecha = new Vector3(Mathf.Cos(rad), 0, -Mathf.Sin(rad));

        Position += (adelante * -entrada.Y + derecha * entrada.X) * VelocidadTeclas * (float)delta;
        LimitarAlPiso();
    }

    private void Panear(Vector2 relativo)
    {
        float rad = Mathf.DegToRad(_anguloHorizontal);
        var adelante = new Vector3(-Mathf.Sin(rad), 0, -Mathf.Cos(rad));
        var derecha = new Vector3(Mathf.Cos(rad), 0, -Mathf.Sin(rad));

        float factor = SensibilidadPan * _distancia * 0.35f;
        Position += (derecha * -relativo.X + adelante * relativo.Y) * factor;
        LimitarAlPiso();
    }

    private void LimitarAlPiso()
    {
        Position = new Vector3(Mathf.Clamp(Position.X, -10, 10), 0, Mathf.Clamp(Position.Z, -10, 10));
    }

    private void ActualizarCamara()
    {
        float h = Mathf.DegToRad(_anguloHorizontal);
        float v = Mathf.DegToRad(_anguloVertical);

        var offset = new Vector3(
            Mathf.Sin(h) * Mathf.Cos(v),
            Mathf.Sin(v),
            Mathf.Cos(h) * Mathf.Cos(v)) * _distancia;

        Camara.Position = offset;
        Camara.LookAt(GlobalPosition, Vector3.Up);
    }

    /// <summary>
    /// Calcula la Transform3D global que tendría la cámara al rotar el ángulo
    /// horizontal en <paramref name="deltaGradosH"/> y multiplicar la distancia
    /// al pivote por <paramref name="factorDistancia"/>. Usado por el grabador
    /// de video para movimientos cinematográficos (paneo orbital, giro 360°,
    /// zoom-in y zoom-out) alrededor del punto de interés actual.
    /// </summary>
    public Transform3D CalcularTransformOrbital(float deltaGradosH, float factorDistancia = 1f)
    {
        float h = Mathf.DegToRad(_anguloHorizontal + deltaGradosH);
        float v = Mathf.DegToRad(_anguloVertical);
        float dist = Mathf.Clamp(_distancia * factorDistancia, 1.2f, 35f);

        var offset = new Vector3(
            Mathf.Sin(h) * Mathf.Cos(v),
            Mathf.Sin(v),
            Mathf.Cos(h) * Mathf.Cos(v)) * dist;

        Vector3 origenCamara = GlobalPosition + offset;
        return new Transform3D(Basis.Identity, origenCamara).LookingAt(GlobalPosition, Vector3.Up);
    }
}
