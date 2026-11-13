namespace ElectricSim.Core.Components;

/// <summary>
/// Disyuntor termomagnético rearmable (breaker). Protege al circuito abriendo la
/// conexión cuando detecta sobrecorriente (con curva inversa rápida en cortos y
/// térmica en sobrecargas leves) o sobretensión en la línea, SIN destruirse.
/// A diferencia de un fusible, una vez despejada la falla puede volver a
/// cerrarse/rearmarse manualmente con un clic o desde el inspector.
/// </summary>
public class Disyuntor : Component
{
    /// <summary>Corriente nominal de disparo (Amps). Infinito = protección por corriente deshabilitada.</summary>
    public double CorrienteDisparo { get; set; } = 1.0;

    /// <summary>Tensión máxima permitida antes de disparar por sobretensión (Volts). Infinito = deshabilitada.</summary>
    public double VoltajeDisparo { get; set; } = 15.0;

    /// <summary>Retardo térmico base (segundos) para sobrecargas cercanas al umbral; cortos severos disparan al instante.</summary>
    public double TiempoDisparo { get; set; } = 0.15;

    /// <summary>Resistencia de contacto en estado cerrado (Ohms). Pequeña para permitir medir la corriente con precisión.</summary>
    public double ResistenciaInterna { get; set; } = 0.04;

    /// <summary>Estado de la llave manual: true = habilitado/cerrado por el usuario; false = abierto manualmente.</summary>
    public bool CerradoManual { get; set; } = true;

    /// <summary>True si el mecanismo automático disparó el disyuntor ante una falla.</summary>
    public bool Disparado { get; private set; }

    /// <summary>Descripción legible de la última falla que provocó el disparo ("sobrecorriente ...", "sobretensión ...").</summary>
    public string MotivoDisparo { get; private set; } = "";

    /// <summary>Tensión máxima detectada en el último tick (línea respecto a referencia o caída en bornes).</summary>
    public double VoltajeObservado { get; private set; }

    /// <summary>True únicamente cuando está cerrado manualmente, no disparado y sano.</summary>
    public bool EstaConduciendo => !IsBurnt && !Disparado && CerradoManual;

    private double _segundosEnFalla;

    public Disyuntor(string name = "Disyuntor", double corrienteDisparo = 1.0, double voltajeDisparo = 15.0)
        : base(name)
    {
        CorrienteDisparo = corrienteDisparo;
        VoltajeDisparo = voltajeDisparo;

        // Límites físicos extremos de la carcasa (el disyuntor normalmente
        // abre la línea antes de llegar a destruirse).
        MaxCurrent = 100;
        MaxVoltage = 1000;
        MaxPower = 5000;
        MaxTemperature = 160;
        CapacidadTermica = 2.0;
    }

    public override double GetResistance() =>
        EstaConduciendo ? Math.Max(ResistenciaInterna, CircuitConstants.WireResistance) : CircuitConstants.OpenResistance;

    /// <summary>Rearma un disyuntor disparado y lo deja nuevamente conectado.</summary>
    public void Rearmar()
    {
        if (IsBurnt) return;
        Disparado = false;
        MotivoDisparo = "";
        CerradoManual = true;
        _segundosEnFalla = 0;
    }

    /// <summary>Alterna apertura/cierre manual, o rearma directamente si estaba disparado.</summary>
    public void AlternarManual()
    {
        if (IsBurnt) return;
        if (Disparado)
        {
            Rearmar();
            return;
        }
        CerradoManual = !CerradoManual;
        _segundosEnFalla = 0;
    }

    /// <summary>Restaura el estado persistido al cargar un proyecto guardado.</summary>
    public void RestaurarEstado(bool cerradoManual, bool disparado, string motivoDisparo)
    {
        CerradoManual = cerradoManual;
        Disparado = disparado;
        MotivoDisparo = disparado ? motivoDisparo : "";
        _segundosEnFalla = 0;
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        double vLinea = Math.Max(Math.Abs(PinA.Voltage), Math.Abs(PinB.Voltage));
        double vRms = ResultadoAC?.VoltajeRms ?? 0;
        VoltajeObservado = Math.Max(VoltajeEnBornes, Math.Max(vLinea, vRms));

        if (!EstaConduciendo)
        {
            _segundosEnFalla = 0;
            return;
        }

        double corriente = Math.Abs(Current);
        bool haySobretension = VoltajeDisparo > 0
            && !double.IsPositiveInfinity(VoltajeDisparo)
            && VoltajeObservado > VoltajeDisparo
            && (corriente > 1e-6 || vLinea > 0.1);

        if (haySobretension)
        {
            _segundosEnFalla += deltaTime;
            double umbralTiempo = Math.Clamp(TiempoDisparo * 0.2, 0.015, 0.06);
            if (_segundosEnFalla >= umbralTiempo)
            {
                Disparado = true;
                MotivoDisparo = $"sobretensión ({VoltajeObservado:F1} V > {VoltajeDisparo:F1} V)";
                _segundosEnFalla = 0;
            }
            return;
        }

        bool haySobrecorriente = CorrienteDisparo > 0
            && !double.IsPositiveInfinity(CorrienteDisparo)
            && corriente > CorrienteDisparo;

        if (haySobrecorriente)
        {
            double factor = corriente / CorrienteDisparo;
            double tiempoObjetivo = factor >= 2.5
                ? 0.015
                : Math.Max(0.02, TiempoDisparo / (factor * factor));
            _segundosEnFalla += deltaTime;
            if (_segundosEnFalla >= tiempoObjetivo)
            {
                Disparado = true;
                MotivoDisparo = $"sobrecorriente ({corriente:F2} A > {CorrienteDisparo:F2} A)";
                _segundosEnFalla = 0;
            }
            return;
        }

        _segundosEnFalla = 0;
    }

    protected override void Reparado()
    {
        Disparado = false;
        MotivoDisparo = "";
        CerradoManual = true;
        _segundosEnFalla = 0;
    }
}

