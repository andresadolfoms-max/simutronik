using Godot;
using ElectricSim.Core;
using ElectricSim.Core.Components;

namespace ElectricSim.Game;

/// <summary>
/// El puente entre Godot y el motor puro de C# (Core). Es un Node común que:
///
///  - Acumula el tiempo de cada frame y dispara los TICKS de simulación a la
///    frecuencia elegida (por defecto 10 por segundo, modificable en vivo
///    desde la barra superior entre 1 y 120).
///  - En cada tick corre primero el paso ELÉCTRICO (CircuitGraph.Tick) y
///    después el paso TÉRMICO (SimuladorTermico.Paso), y avisa a la capa
///    visual con el evento TickCompletado para que actualice luces, humo,
///    lecturas del inspector, etc.
///  - Ofrece la API que usa el resto del juego para tocar el circuito
///    (agregar / quitar componentes, tender cables, avisar movimientos),
///    traduciendo entre el Vector3 de Godot y el de System.Numerics que usa
///    el motor. El motor NUNCA ve un tipo de Godot — esa frontera es la que
///    permite testear todo el Core sin abrir el juego.
/// </summary>
public partial class Simulacion : Node
{
    /// <summary>El grafo eléctrico del motor (ver Core/CircuitGraph.cs).</summary>
    public CircuitGraph Grafo { get; } = new();

    /// <summary>El sistema de calor del motor (ver Core/SimuladorTermico.cs).</summary>
    public SimuladorTermico Termico { get; } = new();

    /// <summary>Se dispara al terminar cada tick completo (eléctrico + térmico).</summary>
    public event Action? TickCompletado;

    /// <summary>Se dispara por cada avance visual de tiempo simulado (vivo u offline), para animar piezas como rotores de motores.</summary>
    public event Action<double>? PasoVisual;

    private double _ticksPorSegundo = 10;

    /// <summary>Frecuencia de la simulación. Editable en vivo; entre 1 y 120.</summary>
    public double TicksPorSegundo
    {
        get => _ticksPorSegundo;
        set => _ticksPorSegundo = Math.Clamp(value, 1, 120);
    }

    /// <summary>Pausa la simulación (el mundo sigue navegable, los números quedan congelados).</summary>
    public bool EnPausa { get; set; }

    /// <summary>Temperatura del aire, editable en vivo desde la barra superior.</summary>
    public double TemperaturaAmbiente
    {
        get => Termico.TemperaturaAmbiente;
        set => Termico.TemperaturaAmbiente = value;
    }

    // Las placas Arduino vivas: sus CPUs avanzan a FRAME-RATE (acá abajo),
    // no al tick eléctrico — así el chip corre sus 16 millones de ciclos por
    // segundo de tiempo real aunque la simulación eléctrica vaya a 10Hz.
    private readonly List<ArduinoUno> _arduinos = new();

    private double _acumulado;

    public override void _Process(double delta)
    {
        if (EnPausa) return;

        PasoVisual?.Invoke(delta);

        // Primero las CPUs: leen las entradas muestreadas en el último tick,
        // ejecutan su tajada de ciclos y dejan las salidas listas para que el
        // próximo tick eléctrico las use.
        foreach (var placa in _arduinos)
        {
            placa.AvanzarTiempoReal(delta);
        }

        _acumulado += delta;
        double intervalo = 1.0 / _ticksPorSegundo;

        // Tope de ticks por frame: si el juego se traba un segundo entero, no
        // intentamos "recuperar" 120 ticks de golpe (espiral de la muerte).
        int ejecutados = 0;
        while (_acumulado >= intervalo && ejecutados < 5)
        {
            _acumulado -= intervalo;
            ejecutados++;
            EjecutarTick(intervalo);
        }
        if (ejecutados == 5) _acumulado = 0; // descartamos el atraso restante
    }

    private void EjecutarTick(double deltaTime)
    {
        Grafo.Tick(deltaTime);
        Termico.Paso(Grafo.Components, deltaTime);
        TickCompletado?.Invoke();
    }

    /// <summary>
    /// Avanza la simulación EXACTAMENTE deltaTime segundos, ignorando la
    /// pausa. Es el motor del grabador de video (renderizado offline): el
    /// mismo flujo que _Process — primero las CPUs, después los ticks que
    /// caigan según TicksPorSegundo, sobre el MISMO acumulador (la grabación
    /// continúa el tiempo desde el estado congelado, tal cual quedó) — pero
    /// manejado a mano, cuadro a cuadro. Acá no hace falta el tope de ticks:
    /// no corremos detrás del reloj de pared, y deltaTime es chico y
    /// conocido (peor caso: 120 ticks/s a 30 fps = 4 ticks por cuadro).
    /// </summary>
    public void AvanzarOffline(double deltaTime)
    {
        PasoVisual?.Invoke(deltaTime);

        foreach (var placa in _arduinos)
        {
            placa.AvanzarTiempoReal(deltaTime);
        }

        _acumulado += deltaTime;
        double intervalo = 1.0 / _ticksPorSegundo;
        while (_acumulado >= intervalo)
        {
            _acumulado -= intervalo;
            EjecutarTick(intervalo);
        }
    }

    // =====================================================================
    //  API para la capa visual
    // =====================================================================

    /// <summary>Registra un componente nuevo en el circuito, ya ubicado en el mundo.</summary>
    public void AgregarComponente(Component componente, Vector3 posicionGlobal)
    {
        componente.PosicionFisica = ANumerics(posicionGlobal);
        componente.Temperatura = Termico.TemperaturaAmbiente; // nace a temperatura ambiente

        // Las aristas internas de los compuestos (transistor, Arduino) viven
        // en el mismo lugar físico que su dueño: si no, el sistema térmico
        // las cree en el origen del mundo y los vecindarios de calor mienten.
        if (componente is IComponenteCompuesto compuesto)
        {
            foreach (var arista in compuesto.AristasInternas)
            {
                arista.PosicionFisica = componente.PosicionFisica;
                arista.Temperatura = Termico.TemperaturaAmbiente;
            }
        }

        if (componente is ArduinoUno placa) _arduinos.Add(placa);

        Grafo.Agregar(componente);
        Termico.MarcarPosicionesCambiadas();
    }

    /// <summary>Saca un componente del circuito (los cables los maneja quien llama).</summary>
    public void QuitarComponente(Component componente)
    {
        if (componente is ArduinoUno placa) _arduinos.Remove(placa);
        Grafo.Quitar(componente);
        Termico.MarcarPosicionesCambiadas();
    }

    /// <summary>Tiende un cable (conexión perfecta editable) entre dos bornes existentes.</summary>
    public Wire ConectarPines(Pin a, Pin b, string nombre = "Cable")
    {
        var cable = Grafo.Conectar(a, b, nombre);
        Termico.MarcarPosicionesCambiadas();
        return cable;
    }

    /// <summary>Avisale al motor que algo se movió (para recalcular vecindades de calor).</summary>
    public void NotificarMovimiento(Component componente, Vector3 nuevaPosicionGlobal)
    {
        componente.PosicionFisica = ANumerics(nuevaPosicionGlobal);
        if (componente is IComponenteCompuesto compuesto)
        {
            foreach (var arista in compuesto.AristasInternas)
            {
                arista.PosicionFisica = componente.PosicionFisica;
            }
        }
        Termico.MarcarPosicionesCambiadas();
    }

    private static System.Numerics.Vector3 ANumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
