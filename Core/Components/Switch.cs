namespace ElectricSim.Core.Components;

/// <summary>
/// Interruptor (palanca). Cerrado = pasa la corriente (conexión perfecta:
/// sus dos bornes se fusionan en un nodo). Abierto = corta el circuito.
/// Soporta accionamiento manual y temporizador cíclico automático configurable
/// en segundos o en ticks.
/// </summary>
public class Switch : Component
{
    private bool _isClosed = true;
    private bool _temporizadorActivo;
    private bool _usarTicks;
    private double _intervaloSegundos = 2.0;
    private int _intervaloTicks = 20;

    private double _segundosRestantes = 2.0;
    private int _ticksRestantes = 20;

    public bool IsClosed
    {
        get => _isClosed;
        set
        {
            _isClosed = value;
            ReiniciarTemporizador();
        }
    }

    /// <summary>Si es true, la palanca cambia de estado automáticamente según el temporizador.</summary>
    public bool TemporizadorActivo
    {
        get => _temporizadorActivo;
        set
        {
            if (_temporizadorActivo == value) return;
            _temporizadorActivo = value;
            ReiniciarTemporizador();
        }
    }

    /// <summary>False = cuenta en segundos; True = cuenta en ticks del simulador.</summary>
    public bool UsarTicks
    {
        get => _usarTicks;
        set
        {
            if (_usarTicks == value) return;
            _usarTicks = value;
            ReiniciarTemporizador();
        }
    }

    /// <summary>Cada cuántos segundos se acciona la palanca (cuando UsarTicks es false).</summary>
    public double IntervaloSegundos
    {
        get => _intervaloSegundos;
        set
        {
            _intervaloSegundos = Math.Max(0.05, value);
            if (_segundosRestantes > _intervaloSegundos) _segundosRestantes = _intervaloSegundos;
        }
    }

    /// <summary>Cada cuántos ticks se acciona la palanca (cuando UsarTicks es true).</summary>
    public int IntervaloTicks
    {
        get => _intervaloTicks;
        set
        {
            _intervaloTicks = Math.Max(1, value);
            if (_ticksRestantes > _intervaloTicks) _ticksRestantes = _intervaloTicks;
        }
    }

    /// <summary>Segundos restantes para el próximo cambio automático.</summary>
    public double SegundosParaCambio => _segundosRestantes;

    /// <summary>Ticks restantes para el próximo cambio automático.</summary>
    public int TicksParaCambio => _ticksRestantes;

    public Switch(string name = "Interruptor", bool startClosed = true) : base(name)
    {
        _isClosed = startClosed;
        ReiniciarTemporizador();
    }

    public void ReiniciarTemporizador()
    {
        _segundosRestantes = Math.Max(0.05, _intervaloSegundos);
        _ticksRestantes = Math.Max(1, _intervaloTicks);
    }

    public override bool EsConexionPerfecta() => _isClosed;

    public override double GetResistance() =>
        _isClosed ? CircuitConstants.WireResistance : CircuitConstants.OpenResistance;

    protected override void ActualizarEstado(double deltaTime)
    {
        if (!_temporizadorActivo || IsBurnt) return;

        if (_usarTicks)
        {
            _ticksRestantes--;
            if (_ticksRestantes <= 0)
            {
                _isClosed = !_isClosed;
                _ticksRestantes = Math.Max(1, _intervaloTicks);
            }
        }
        else
        {
            _segundosRestantes -= deltaTime;
            if (_segundosRestantes <= 0)
            {
                _isClosed = !_isClosed;
                _segundosRestantes = Math.Max(0.05, _intervaloSegundos);
            }
        }
    }

    public string DescribirTemporizador()
    {
        if (!_temporizadorActivo) return "manual (desactivado)";
        return _usarTicks
            ? $"activo · cambia en {_ticksRestantes} de {_intervaloTicks} ticks"
            : $"activo · cambia en {_segundosRestantes:F2} s (cada {_intervaloSegundos:F2} s)";
    }
}
