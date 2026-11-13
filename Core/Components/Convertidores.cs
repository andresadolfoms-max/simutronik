namespace ElectricSim.Core.Components;

/// <summary>
/// Base común para los convertidores aislados de cuatro terminales:
/// dos bornes de entrada (<see cref="PinEntradaA"/>, <see cref="PinEntradaB"/>)
/// y dos bornes de salida (<see cref="Component.PinA"/>, <see cref="Component.PinB"/>)
/// acoplados por potencia y tipo de corriente sin conexión eléctrica directa.
/// </summary>
public abstract class ConvertidorAislado : FuenteBase, IComponenteCompuesto, IComponenteMultiBorne
{
    /// <summary>Borne de entrada A (AC1 en TransformadorAcDc; IN+ en Inversor).</summary>
    public Pin PinEntradaA { get; }

    /// <summary>Borne de entrada B (AC2 en TransformadorAcDc; IN− en Inversor).</summary>
    public Pin PinEntradaB { get; }

    /// <summary>Tensión absoluta medida entre los bornes de entrada en el último tick (V).</summary>
    public double VoltajeEntrada { get; private set; }

    /// <summary>Diferencia con signo entre la entrada A y B (V_A − V_B).</summary>
    public double DiferenciaEntradaConSigno { get; private set; }

    /// <summary>Corriente que circula por la etapa de entrada (A).</summary>
    public double CorrienteEntrada => Math.Abs(_entrada.Current);

    /// <summary>Tipo de corriente recibido en la etapa de entrada.</summary>
    public TipoCorriente TipoEntrada => _entrada.TipoCorrienteRecibida;

    /// <summary>True cuando el convertidor está activo entregando tensión en la salida.</summary>
    public bool Encendido => EstaEncendida;

    protected override bool EvaluarMinimosDesdeSalida => false;

    private readonly AristaEntrada _entrada;
    private readonly (string A, string B) _etiquetasEntrada;
    private double ResistenciaEntradaActual = CircuitConstants.OpenResistance;

    public IEnumerable<Component> AristasInternas => new[] { (Component)_entrada };

    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra => new[]
    {
        (_etiquetasEntrada.A, PinEntradaA),
        (_etiquetasEntrada.B, PinEntradaB),
    };

    protected ConvertidorAislado(
        string name,
        string etiquetaEntradaA,
        string etiquetaEntradaB,
        double maxPowerEntrada,
        double maxCurrentEntrada,
        double maxVoltageEntrada = 300.0,
        double maxCurrentSalidaRef = 5.0,
        double maxVoltageSalidaRef = 300.0)
        : base(name, 0)
    {
        PinEntradaA = new Pin(this, $"{name}.{etiquetaEntradaA}");
        PinEntradaB = new Pin(this, $"{name}.{etiquetaEntradaB}");
        _etiquetasEntrada = (etiquetaEntradaA, etiquetaEntradaB);
        _entrada = new AristaEntrada(
            this, maxPowerEntrada, maxCurrentEntrada, maxVoltageEntrada,
            maxCurrentSalidaRef, maxVoltageSalidaRef);

        CapacidadTermica = 15;
        ResistenciaTermica = 5;
        MaxTemperature = 120;
        MaxVoltage = maxVoltageSalidaRef;
    }

    public override void FixVoltages()
    {
        if (EstaEncendida && VoltajeEfectivoParaSolver < 0.05 && !EnCortocircuito)
        {
            ActualizarVEfectivo();
        }
        base.FixVoltages();
    }

    protected sealed override void ActualizarEstado(double deltaTime)
    {
        _entrada.SincronizarConDueno();

        if (_entrada.IsBurnt && !IsBurnt)
        {
            string motivo = _entrada.MotivoQuemado.Length > 0
                ? _entrada.MotivoQuemado
                : "sobrecarga en etapa de entrada";
            Quemar(motivo);
            return;
        }

        DiferenciaEntradaConSigno = PinEntradaA.Voltage - PinEntradaB.Voltage;
        VoltajeEntrada = Math.Abs(DiferenciaEntradaConSigno);

        double corrienteTotalEntrada = Math.Max(CorrienteEntrada, Math.Abs(Current));
        double potenciaRef = Math.Max(VoltajeEntrada * CorrienteEntrada, Power);
        EvaluarMinimosOperacion(VoltajeEntrada, corrienteTotalEntrada, potenciaRef);

        var (voltajeSalida, resistenciaEntradaObjetivo) =
            Recalcular(VoltajeEntrada, DiferenciaEntradaConSigno, _entrada.TipoCorrienteRecibida, deltaTime);

        if (MinVoltage > 0 && VoltajeEntrada + 1e-6 < MinVoltage)
        {
            voltajeSalida = 0;
        }

        if (voltajeSalida <= 0.05)
        {
            Voltage = 0;
            ActualizarVEfectivo();
        }
        else
        {
            Voltage = voltajeSalida;
        }

        ResistenciaEntradaActual = ResistenciaEntradaActual >= CircuitConstants.OpenResistance * 0.5
            ? resistenciaEntradaObjetivo
            : 0.2 * ResistenciaEntradaActual + 0.8 * resistenciaEntradaObjetivo;
        ResistenciaEntradaActual = Math.Clamp(
            ResistenciaEntradaActual, 0.5, CircuitConstants.OpenResistance);
    }

    public override double PotenciaTermica
    {
        get
        {
            if (IsBurnt) return 0;
            double potenciaEntrada = CorrienteEntrada * VoltajeEntrada;
            double potenciaSalida = Math.Abs(CorrienteEntregada) * Math.Abs(Voltage);
            return Math.Max(0, potenciaEntrada - potenciaSalida);
        }
    }

    protected abstract (double VoltajeSalida, double ResistenciaEntrada) Recalcular(
        double voltajeEntrada, double difConSigno, TipoCorriente tipoEntrada, double deltaTime);

    internal void QuemarPorEntrada(string motivo)
    {
        if (!IsBurnt)
        {
            Quemar(string.IsNullOrEmpty(motivo) ? "sobrecarga en etapa de entrada" : motivo);
        }
    }

    protected override void Quemar()
    {
        base.Quemar();
        Voltage = 0;
        if (!_entrada.IsBurnt) _entrada.MatarPorDueno(MotivoQuemado);
    }

    protected override void Reparado()
    {
        base.Reparado();
        Voltage = 0;
        ResistenciaEntradaActual = CircuitConstants.OpenResistance;
        _entrada.RevivirPorDueno(Temperatura);
    }

    internal sealed class AristaEntrada : Component
    {
        private readonly ConvertidorAislado _dueno;
        private readonly double _maxCurrentBase;
        private readonly double _maxVoltageBase;
        private readonly double _maxTemperatureBase;
        private readonly double _maxCurrentDuenoRef;
        private readonly double _maxVoltageDuenoRef;
        private bool _sincronizandoQuemado;

        public AristaEntrada(
            ConvertidorAislado dueno,
            double maxPower,
            double maxCurrent,
            double maxVoltage,
            double maxCurrentDuenoRef,
            double maxVoltageDuenoRef)
            : base($"{dueno.Name}.Entrada", dueno.PinEntradaA, dueno.PinEntradaB)
        {
            _dueno = dueno;
            _maxCurrentBase = maxCurrent;
            _maxVoltageBase = maxVoltage;
            _maxCurrentDuenoRef = Math.Max(maxCurrentDuenoRef, 1e-6);
            _maxVoltageDuenoRef = Math.Max(maxVoltageDuenoRef, 1e-6);
            _maxTemperatureBase = 130;

            MaxPower = maxPower;
            MaxCurrent = maxCurrent;
            MaxVoltage = maxVoltage;
            MaxTemperature = _maxTemperatureBase;
            CapacidadTermica = 8;
            SegundosParaQuemarse = 0.5;
            FraccionCalorDisipado = 0.05;
        }

        public void SincronizarConDueno()
        {
            PosicionFisica = _dueno.PosicionFisica;
            MaxPower = _dueno.MaxPower;
            MaxCurrent = double.IsPositiveInfinity(_dueno.MaxCurrent)
                ? double.PositiveInfinity
                : _dueno.MaxCurrent * (_maxCurrentBase / _maxCurrentDuenoRef);
            MaxVoltage = double.IsPositiveInfinity(_dueno.MaxVoltage)
                ? double.PositiveInfinity
                : _dueno.MaxVoltage * (_maxVoltageBase / _maxVoltageDuenoRef);
            MaxTemperature = double.IsPositiveInfinity(_dueno.MaxTemperature)
                ? double.PositiveInfinity
                : Math.Max(_dueno.MaxTemperature, _maxTemperatureBase);
            FraccionCalorDisipado = _dueno is TransformadorAcDc { DcEnPrimario: true } ? 1.0 : 0.05;
        }

        public override double GetResistance() =>
            _dueno.IsBurnt || IsBurnt
                ? CircuitConstants.OpenResistance
                : _dueno.ResistenciaEntradaActual;

        protected override void Quemar()
        {
            base.Quemar();
            if (_sincronizandoQuemado) return;
            _sincronizandoQuemado = true;
            try
            {
                _dueno.QuemarPorEntrada(MotivoQuemado);
            }
            finally
            {
                _sincronizandoQuemado = false;
            }
        }

        public void MatarPorDueno(string motivo)
        {
            if (IsBurnt || _sincronizandoQuemado) return;
            _sincronizandoQuemado = true;
            try
            {
                Quemar(string.IsNullOrEmpty(motivo) ? "se quemó el convertidor" : motivo);
            }
            finally
            {
                _sincronizandoQuemado = false;
            }
        }

        public void RevivirPorDueno(double temperatura) => Reparar(temperatura);
    }
}

/// <summary>
/// Transformador con etapa rectificadora y filtro (AC → DC):
/// recibe corriente alterna en AC1/AC2 y entrega corriente continua regulada en +/−.
/// Conserva la potencia según su eficiencia e impide funcionar con DC en el primario.
/// </summary>
public class TransformadorAcDc : ConvertidorAislado
{
    /// <summary>Tensión continua nominal de salida cuando la entrada coincide con VoltajeEntradaNominal (V).</summary>
    public double VoltajeSalidaNominal { get; set; } = 12.0;

    /// <summary>Tensión alterna RMS nominal esperada en el primario (V).</summary>
    public double VoltajeEntradaNominal { get; set; } = 220.0;

    /// <summary>Eficiencia global de transformación y rectificación (0-1).</summary>
    public double Eficiencia { get; set; } = 0.85;

    /// <summary>Impedancia equivalente del primario en vacío con corriente alterna (Ω).</summary>
    public double ResistenciaVacio { get; set; } = 4_000.0;

    /// <summary>Resistencia óhmica pura del bobinado primario cuando se aplica DC por error (Ω).</summary>
    public double ResistenciaCobrePrimario { get; set; } = 2.0;

    /// <summary>Relación efectiva de transformación V_salida / V_entrada.</summary>
    public double Relacion => VoltajeSalidaNominal / Math.Max(VoltajeEntradaNominal, 1.0);

    /// <summary>True si el primario está recibiendo corriente continua (fallo de operación).</summary>
    public bool DcEnPrimario { get; private set; }

    public override TipoCorriente Tipo => TipoCorriente.DC;

    public TransformadorAcDc(string name)
        : base(name, "AC1", "AC2", maxPowerEntrada: 60, maxCurrentEntrada: 2.0, maxVoltageEntrada: 300.0)
    {
        PinEntradaA.Polaridad = Polaridad.Fase;
        PinEntradaB.Polaridad = Polaridad.Neutro;
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;
        ResistenciaInternaNominal = 0.6;
        MaxCurrent = 5.0;
        MaxPower = 60.0;
    }

    protected override (double, double) Recalcular(
        double voltajeEntrada, double difConSigno, TipoCorriente tipoEntrada, double deltaTime)
    {
        bool tieneAC = (tipoEntrada & TipoCorriente.AC) != 0;
        bool tieneDC = (tipoEntrada & TipoCorriente.DC) != 0;

        if (tieneDC)
        {
            DcEnPrimario = true;
            return (0, ResistenciaCobrePrimario);
        }

        DcEnPrimario = false;
        if (!tieneAC || voltajeEntrada < 0.5)
        {
            return (0, ResistenciaVacio);
        }

        double voltajeSalida = voltajeEntrada * Relacion;
        double potenciaSalida = voltajeSalida * Math.Abs(CorrienteEntregada);
        double corrienteUtilEntrada = potenciaSalida / (Math.Clamp(Eficiencia, 0.1, 1.0) * Math.Max(voltajeEntrada, 0.5));
        double corrienteVacio = voltajeEntrada / Math.Max(ResistenciaVacio, 1.0);
        double corrienteTotal = corrienteUtilEntrada + corrienteVacio;

        double resistenciaEntrada = corrienteTotal > 1e-9
            ? voltajeEntrada / corrienteTotal
            : ResistenciaVacio;

        return (voltajeSalida, resistenciaEntrada);
    }
}

/// <summary>
/// Inversor (DC → AC):
/// convierte corriente continua de entrada (IN+/IN−) en corriente alterna RMS (F~/N~),
/// conservando la potencia según su eficiencia, con consumo de standby y corte por baja tensión.
/// </summary>
public class Inversor : ConvertidorAislado
{
    /// <summary>Tensión eficaz (RMS) de salida en corriente alterna (V).</summary>
    public double VoltajeSalidaRMS { get; set; } = 220.0;

    /// <summary>Frecuencia de la onda alterna entregada a la salida (Hz).</summary>
    public double FrecuenciaSalida { get; set; } = 50.0;

    /// <summary>Tensión mínima de entrada DC requerida para encender (corte por batería baja, V).</summary>
    public double VoltajeMinimoEntrada { get; set; } = 10.0;

    /// <summary>Eficiencia de conversión de la etapa inversora (0-1).</summary>
    public double Eficiencia { get; set; } = 0.85;

    /// <summary>Consumo propio en vacío cuando el inversor está encendido (A).</summary>
    public double CorrienteStandby { get; set; } = 0.05;

    /// <summary>True si la entrada DC está conectada con polaridad invertida.</summary>
    public bool PolaridadInvertida { get; private set; }

    /// <summary>True si la entrada está recibiendo corriente alterna en lugar de continua.</summary>
    public bool EntradaIncompatibleAC { get; private set; }

    private bool _activo;

    public override TipoCorriente Tipo => TipoCorriente.AC;

    public Inversor(string name)
        : base(name, "IN+", "IN−", maxPowerEntrada: 260, maxCurrentEntrada: 25.0, maxVoltageEntrada: 60.0,
            maxCurrentSalidaRef: 2.0, maxVoltageSalidaRef: 300.0)
    {
        PinEntradaA.Polaridad = Polaridad.Positivo;
        PinEntradaB.Polaridad = Polaridad.Negativo;
        PinA.Polaridad = Polaridad.Fase;
        PinB.Polaridad = Polaridad.Neutro;
        ResistenciaInternaNominal = 2.0;
        MaxCurrent = 2.0;
        MaxPower = 260.0;
    }

    protected override (double, double) Recalcular(
        double voltajeEntrada, double difConSigno, TipoCorriente tipoEntrada, double deltaTime)
    {
        bool tieneDC = (tipoEntrada & TipoCorriente.DC) != 0;
        bool tieneAC = (tipoEntrada & TipoCorriente.AC) != 0;

        EntradaIncompatibleAC = tieneAC;
        PolaridadInvertida = tieneDC && !tieneAC && difConSigno < -0.5;

        if (!tieneDC || tieneAC || PolaridadInvertida)
        {
            _activo = false;
            return (0, CircuitConstants.OpenResistance);
        }

        if (voltajeEntrada >= VoltajeMinimoEntrada) _activo = true;
        else if (voltajeEntrada < VoltajeMinimoEntrada - 0.5) _activo = false;

        if (!_activo)
        {
            return (0, CircuitConstants.OpenResistance);
        }

        double voltajeSalida = VoltajeSalidaRMS;
        double potenciaSalida = voltajeSalida * Math.Abs(CorrienteEntregada);
        double corrienteEntrada = potenciaSalida / (Math.Clamp(Eficiencia, 0.1, 1.0) * Math.Max(voltajeEntrada, 1.0))
                                  + CorrienteStandby;
        double resistenciaEntrada = voltajeEntrada / Math.Max(corrienteEntrada, 1e-6);

        return (voltajeSalida, resistenciaEntrada);
    }
}

