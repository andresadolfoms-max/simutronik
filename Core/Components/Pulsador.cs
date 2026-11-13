namespace ElectricSim.Core.Components;

/// <summary>
/// Botón momentáneo (pulsador): al presionarlo cierra el circuito durante el
/// tiempo configurado (en segundos o ticks) y luego se suelta solo.
///
/// Con TemporizadorActivo = true, ejecuta un ciclo de dos fases estrictamente
/// separadas para evitar cualquier bucle accidental:
///   1) FASE SUELTO (espera): cuenta el intervalo de espera (IntervaloSegundos
///      o IntervaloTicks). Al llegar a 0, presiona el botón.
///   2) FASE PRESIONADO (activo): mientras el botón está presionado, el
///      contador de espera NO corre. Solo descuenta el tiempo de retención
///      (DuracionPulso o DuracionPulsoTicks). Recién en el instante en que el
///      botón se suelta, el contador de espera se reinicia desde cero para el
///      próximo ciclo.
/// </summary>
public class Pulsador : Component
{
    private double _duracionPulso = 0.5;
    private int _duracionPulsoTicks = 5;
    private bool _temporizadorActivo;
    private bool _usarTicks;
    private double _intervaloSegundos = 2.0;
    private int _intervaloTicks = 20;

    // Contadores de retención (mientras está presionado):
    private double _segundosPresionadoRestantes;
    private int _ticksPresionadoRestantes;

    // Contadores de espera (mientras está suelto):
    private double _segundosEsperaRestantes = 2.0;
    private int _ticksEsperaRestantes = 20;

    /// <summary>Cuántos segundos queda cerrado después de cada pulsación (en modo segundos).</summary>
    public double DuracionPulso
    {
        get => _duracionPulso;
        set
        {
            _duracionPulso = Math.Max(0.05, value);
            if (_segundosPresionadoRestantes > _duracionPulso)
                _segundosPresionadoRestantes = _duracionPulso;
        }
    }

    /// <summary>Cuántos ticks queda cerrado después de cada pulsación (en modo ticks).</summary>
    public int DuracionPulsoTicks
    {
        get => _duracionPulsoTicks;
        set
        {
            _duracionPulsoTicks = Math.Max(1, value);
            if (_ticksPresionadoRestantes > _duracionPulsoTicks)
                _ticksPresionadoRestantes = _duracionPulsoTicks;
        }
    }

    /// <summary>Activa o desactiva la pulsación automática periódica.</summary>
    public bool TemporizadorActivo
    {
        get => _temporizadorActivo;
        set
        {
            if (_temporizadorActivo == value) return;
            _temporizadorActivo = value;
            ReiniciarContadorEspera();
        }
    }

    /// <summary>False = temporización en segundos; True = temporización en ticks.</summary>
    public bool UsarTicks
    {
        get => _usarTicks;
        set
        {
            if (_usarTicks == value) return;
            _usarTicks = value;
            ReiniciarContadorEspera();
            if (Presionado)
            {
                _segundosPresionadoRestantes = Math.Max(0.05, _duracionPulso);
                _ticksPresionadoRestantes = Math.Max(1, _duracionPulsoTicks);
            }
        }
    }

    /// <summary>Segundos de espera (con el botón suelto) antes de volver a presionarse.</summary>
    public double IntervaloSegundos
    {
        get => _intervaloSegundos;
        set
        {
            _intervaloSegundos = Math.Max(0.05, value);
            if (_segundosEsperaRestantes > _intervaloSegundos)
                _segundosEsperaRestantes = _intervaloSegundos;
        }
    }

    /// <summary>Ticks de espera (con el botón suelto) antes de volver a presionarse.</summary>
    public int IntervaloTicks
    {
        get => _intervaloTicks;
        set
        {
            _intervaloTicks = Math.Max(1, value);
            if (_ticksEsperaRestantes > _intervaloTicks)
                _ticksEsperaRestantes = _intervaloTicks;
        }
    }

    /// <summary>True mientras el botón está haciendo contacto.</summary>
    public bool Presionado { get; private set; }

    public Pulsador(string name = "Pulsador") : base(name)
    {
        ReiniciarContadorEspera();
    }

    /// <summary>Presiona el botón: cierra el contacto durante el tiempo o ticks configurados.</summary>
    public void Presionar()
    {
        if (IsBurnt) return;
        Presionado = true;
        _segundosPresionadoRestantes = Math.Max(0.05, _duracionPulso);
        _ticksPresionadoRestantes = Math.Max(1, _duracionPulsoTicks);
    }

    /// <summary>Suelta el contacto y reinicia desde cero el contador de espera hasta la próxima activación.</summary>
    public void Soltar()
    {
        Presionado = false;
        _segundosPresionadoRestantes = 0;
        _ticksPresionadoRestantes = 0;
        ReiniciarContadorEspera();
    }

    private void ReiniciarContadorEspera()
    {
        _segundosEsperaRestantes = Math.Max(0.05, _intervaloSegundos);
        _ticksEsperaRestantes = Math.Max(1, _intervaloTicks);
    }

    public override bool EsConexionPerfecta() => Presionado;

    public override double GetResistance() =>
        Presionado ? CircuitConstants.WireResistance : CircuitConstants.OpenResistance;

    protected override void ActualizarEstado(double deltaTime)
    {
        if (IsBurnt)
        {
            Presionado = false;
            return;
        }

        // FASE 1: El botón está PRESIONADO.
        // El contador de espera hacia el próximo ciclo NO avanza mientras está
        // presionado. Recién cuando termina el tiempo de retención y se suelta,
        // se reinicia el contador de espera y se sale del tick (sin re-disparo).
        if (Presionado)
        {
            if (_usarTicks)
            {
                _ticksPresionadoRestantes--;
                if (_ticksPresionadoRestantes <= 0)
                {
                    Soltar();
                }
            }
            else
            {
                _segundosPresionadoRestantes -= deltaTime;
                if (_segundosPresionadoRestantes <= 0)
                {
                    Soltar();
                }
            }
            return;
        }

        // FASE 2: El botón está SUELTO.
        // Si el temporizador automático está activo, descuenta la espera y, al
        // cumplirse, presiona el botón.
        if (!_temporizadorActivo) return;

        if (_usarTicks)
        {
            _ticksEsperaRestantes--;
            if (_ticksEsperaRestantes <= 0)
            {
                Presionar();
            }
        }
        else
        {
            _segundosEsperaRestantes -= deltaTime;
            if (_segundosEsperaRestantes <= 0)
            {
                Presionar();
            }
        }
    }

    public string DescribirTemporizador()
    {
        if (Presionado)
        {
            return _usarTicks
                ? $"presionado (se suelta en {_ticksPresionadoRestantes} ticks)"
                : $"presionado (se suelta en {_segundosPresionadoRestantes:F2} s)";
        }

        if (!_temporizadorActivo) return "manual (suelto)";

        return _usarTicks
            ? $"suelto · se presiona en {_ticksEsperaRestantes} de {_intervaloTicks} ticks (por {_duracionPulsoTicks} ticks)"
            : $"suelto · se presiona en {_segundosEsperaRestantes:F2} s (cada {_intervaloSegundos:F2} s por {_duracionPulso:F2} s)";
    }
}
