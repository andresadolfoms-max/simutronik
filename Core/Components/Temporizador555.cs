namespace ElectricSim.Core.Components;

/// <summary>
/// Temporizador NE555 de 8 pines con topología interna fiel al circuito integrado real:
///   - Pin 8 (VCC) / Pin 1 (GND): alimentación DC (4.5 V a 16 V típicamente)
///   - Divisor resistivo interno 5 kΩ − 5 kΩ − 5 kΩ accesible en Pin 5 (CTRL ≈ 2/3 Vcc)
///   - Comparador superior en Pin 6 (THRES): resetea la salida a BAJO si V_THRES > V_CTRL
///   - Comparador inferior en Pin 2 (TRIG): setea la salida a ALTO si V_TRIG < ½ V_CTRL
///   - Pin 4 (RESET): activo en bajo (&lt; 0.7 V) con pull-up interno suave a VCC
///   - Pin 7 (DISCH): transistor NPN a colector abierto a GND (conduce cuando OUT está en BAJO)
///   - Pin 3 (OUT): etapa push-pull entre VCC y GND (hasta ±200 mA nominales)
///
/// Soporta modo astable (oscilador de onda cuadrada para LEDs o tonos), monoestable
/// (temporizador/retardo por disparo en TRIG) y modulación por CTRL.
/// </summary>
public class Temporizador555 : Component, IComponenteCompuesto, IComponenteMultiBorne
{
    private const double ResistenciaEntradaAltaZ = 100_000_000.0; // 100 MΩ para no cargar redes RC externas
    private const double ResistenciaPullupReset = 100_000.0;
    private const double ResistenciaDivisorSuperior = 5_000.0;
    private const double ResistenciaDivisorInferior = 10_000.0;

    /// <summary>Pines adicionales más allá de VCC (PinA) y GND (PinB).</summary>
    public Pin PinTrig { get; }
    public Pin PinOut { get; }
    public Pin PinReset { get; }
    public Pin PinCtrl { get; }
    public Pin PinThres { get; }
    public Pin PinDisch { get; }

    /// <summary>Tensión mínima de alimentación (V) para que el oscilador y las salidas se habiliten.</summary>
    public double VoltajeMinimoOperacion { get; set; } = 4.5;

    /// <summary>Impedancia equivalente de la etapa push-pull de salida en pin 3 (Ohms).</summary>
    public double ResistenciaSalida { get; set; } = 15.0;

    /// <summary>Resistencia de saturación del transistor de descarga en pin 7 cuando está activo (Ohms).</summary>
    public double ResistenciaDescarga { get; set; } = 10.0;

    /// <summary>Consumo propio interno en reposo (Ohms equivalentes entre VCC y GND).</summary>
    public double ResistenciaQuiescente { get; set; } = 1000.0;

    /// <summary>True cuando la etapa de salida Q está en estado ALTO.</summary>
    public bool SalidaAlta => _salidaAlta;

    /// <summary>True cuando la tensión VCC−GND alcanza el umbral de operación.</summary>
    public bool Energizado { get; private set; }

    /// <summary>Lecturas vivas relativas a GND para el inspector.</summary>
    public double VoltajeAlimentacion { get; private set; }
    public double VoltajeSalida { get; private set; }
    public double UmbralSuperior { get; private set; }
    public double UmbralInferior { get; private set; }
    public double CorrienteSalida { get; private set; }
    public double CorrienteDescarga { get; private set; }
    public double FrecuenciaMedidaHz { get; private set; }

    private bool _salidaAlta;
    private double _tiempoDesdeUltimoFlanco;
    private double _tiempoAcumuladoSobrecarga;

    private readonly AristaInterna _rDivSup;
    private readonly AristaInterna _rDivInf;
    private readonly AristaInterna _pullReset;
    private readonly AristaInterna _inTrig;
    private readonly AristaInterna _inThres;
    private readonly AristaInterna _driverAlto;
    private readonly AristaInterna _driverBajo;
    private readonly AristaInterna _discharge;

    public IEnumerable<Component> AristasInternas => new Component[]
    {
        _rDivSup,
        _rDivInf,
        _pullReset,
        _inTrig,
        _inThres,
        _driverAlto,
        _driverBajo,
        _discharge,
    };

    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra => new[]
    {
        ("TRIG", PinTrig),
        ("OUT", PinOut),
        ("RESET", PinReset),
        ("CTRL", PinCtrl),
        ("THRES", PinThres),
        ("DISCH", PinDisch),
    };

    public Temporizador555(string name = "NE555") : base(name)
    {
        PinA.Polaridad = Polaridad.Positivo; // Pin 8: VCC
        PinB.Polaridad = Polaridad.Negativo; // Pin 1: GND

        PinTrig = new Pin(this, $"{name}.TRIG") { Polaridad = Polaridad.Senal };
        PinOut = new Pin(this, $"{name}.OUT") { Polaridad = Polaridad.Senal };
        PinReset = new Pin(this, $"{name}.RESET") { Polaridad = Polaridad.Senal };
        PinCtrl = new Pin(this, $"{name}.CTRL") { Polaridad = Polaridad.Senal };
        PinThres = new Pin(this, $"{name}.THRES") { Polaridad = Polaridad.Senal };
        PinDisch = new Pin(this, $"{name}.DISCH") { Polaridad = Polaridad.Senal };

        _rDivSup = new AristaInterna($"{name}.DivSup", PinA, PinCtrl, ResistenciaDivisorSuperior);
        _rDivInf = new AristaInterna($"{name}.DivInf", PinCtrl, PinB, ResistenciaDivisorInferior);
        _pullReset = new AristaInterna($"{name}.PullRst", PinA, PinReset, ResistenciaPullupReset);
        _inTrig = new AristaInterna($"{name}.InTrig", PinA, PinTrig, ResistenciaEntradaAltaZ);
        _inThres = new AristaInterna($"{name}.InThres", PinThres, PinB, ResistenciaEntradaAltaZ);
        _driverAlto = new AristaInterna($"{name}.OutHigh", PinA, PinOut, CircuitConstants.OpenResistance);
        _driverBajo = new AristaInterna($"{name}.OutLow", PinOut, PinB, CircuitConstants.OpenResistance);
        _discharge = new AristaInterna($"{name}.Disch", PinDisch, PinB, CircuitConstants.OpenResistance);

        Compatibilidad = CompatibilidadCorriente.SoloDC;
        MaxVoltage = 16.5;   // NE555 bipolar: máx absoluto 16-18 V
        MaxCurrent = 0.25;   // corriente total / salida ~200 mA + consumo interno
        MaxPower = 0.8;      // encapsulado DIP-8 estándar (~600-800 mW)
        CapacidadTermica = 0.8;
        ResistenciaTermica = 95;
    }

    public override double GetResistance()
    {
        if (IsBurnt || EsIncompatible) return CircuitConstants.OpenResistance;
        return Energizado ? Math.Max(ResistenciaQuiescente, 50.0) : 500_000.0;
    }

    /// <summary>Fija el estado inicial de salida al cargar un proyecto guardado.</summary>
    public void RestaurarSalida(bool salidaAlta)
    {
        _salidaAlta = salidaAlta;
        if (!IsBurnt && !EsIncompatible && Energizado)
        {
            AplicarDrivers();
        }
    }

    public override void PostSolve(double deltaTime)
    {
        if (IsBurnt)
        {
            ApagarInterno();
            Current = 0;
            Power = 0;
            VoltajeEnBornes = 0;
            VoltajeAlimentacion = 0;
            VoltajeSalida = 0;
            CorrienteSalida = 0;
            CorrienteDescarga = 0;
            FrecuenciaMedidaHz = 0;
            return;
        }

        double deltaSup = PinA.Voltage - PinB.Voltage;
        VoltajeAlimentacion = deltaSup;
        VoltajeEnBornes = Math.Abs(deltaSup);

        if (EsIncompatible)
        {
            ApagarInterno();
            Current = 0;
            Power = 0;
            VoltajeSalida = 0;
            CorrienteSalida = 0;
            CorrienteDescarga = 0;
            FrecuenciaMedidaHz = 0;
            return;
        }

        // Protección por polaridad invertida en VCC/GND.
        if (deltaSup < -0.7 && !double.IsPositiveInfinity(MaxVoltage))
        {
            Energizado = false;
            _salidaAlta = false;
            AplicarDrivers();
            Current = deltaSup / ResistenciaQuiescente;
            Power = Math.Abs(deltaSup * Current);
            _tiempoAcumuladoSobrecarga += deltaTime;
            if (_tiempoAcumuladoSobrecarga >= SegundosParaQuemarse)
            {
                Quemar("polaridad inversa");
            }
            return;
        }

        // Mantener red interna pasiva conectada mientras el integrado no esté quemado.
        RestaurarRedPasiva();

        double corrienteTentativa = deltaSup / Math.Max(GetResistance(), CircuitConstants.MinResistance);
        double potenciaTentativa = Math.Abs(deltaSup * corrienteTentativa);
        double voltajeMinRequerido = Math.Max(VoltajeMinimoOperacion, MinVoltage);

        if (deltaSup + 1e-6 < voltajeMinRequerido)
        {
            Energizado = false;
            _salidaAlta = false;
            AplicarDrivers();
            Current = corrienteTentativa;
            Power = potenciaTentativa;
            EvaluarMinimosOperacion(deltaSup, Current, Power);
            VoltajeSalida = Math.Max(0, PinOut.Voltage - PinB.Voltage);
            UmbralSuperior = Math.Max(0, PinCtrl.Voltage - PinB.Voltage);
            UmbralInferior = UmbralSuperior * 0.5;
            CorrienteSalida = 0;
            CorrienteDescarga = 0;
            FrecuenciaMedidaHz = 0;
            _tiempoDesdeUltimoFlanco = 0;
            _tiempoAcumuladoSobrecarga = 0;
            return;
        }

        Energizado = true;

        double vGnd = PinB.Voltage;
        double vCtrl = Math.Clamp(PinCtrl.Voltage - vGnd, 0.2, Math.Max(0.2, deltaSup - 0.1));
        UmbralSuperior = vCtrl;
        UmbralInferior = vCtrl * 0.5;

        double vReset = PinReset.Voltage - vGnd;
        double vTrig = PinTrig.Voltage - vGnd;
        double vThres = PinThres.Voltage - vGnd;

        bool salidaAnterior = _salidaAlta;

        // Tabla de verdad del flip-flop SR interno del NE555:
        // RESET (<0.7 V) domina; luego TRIG (<1/2 CTRL) domina sobre THRES (>CTRL).
        if (vReset < 0.7)
        {
            _salidaAlta = false;
        }
        else if (vTrig < UmbralInferior)
        {
            _salidaAlta = true;
        }
        else if (vThres > UmbralSuperior)
        {
            _salidaAlta = false;
        }

        // Medición de frecuencia entre flancos ascendentes de OUT.
        _tiempoDesdeUltimoFlanco += deltaTime;
        if (!salidaAnterior && _salidaAlta)
        {
            if (_tiempoDesdeUltimoFlanco > 1e-4)
            {
                double fNueva = 1.0 / _tiempoDesdeUltimoFlanco;
                FrecuenciaMedidaHz = FrecuenciaMedidaHz > 0
                    ? FrecuenciaMedidaHz * 0.4 + fNueva * 0.6
                    : fNueva;
            }
            _tiempoDesdeUltimoFlanco = 0;
        }
        else if (_tiempoDesdeUltimoFlanco > 5.0)
        {
            FrecuenciaMedidaHz = 0;
        }

        // Corrientes reales antes de conmutar los drivers al próximo estado.
        double iAlto = (PinA.Voltage - PinOut.Voltage) / _driverAlto.GetResistance();
        double iBajo = (PinOut.Voltage - PinB.Voltage) / _driverBajo.GetResistance();
        CorrienteSalida = iAlto - iBajo;
        CorrienteDescarga = (PinDisch.Voltage - PinB.Voltage) / _discharge.GetResistance();
        VoltajeSalida = PinOut.Voltage - vGnd;

        double potenciaDrivers =
            iAlto * iAlto * _driverAlto.GetResistance()
            + iBajo * iBajo * _driverBajo.GetResistance()
            + CorrienteDescarga * CorrienteDescarga * _discharge.GetResistance();
        double iQuiescente = deltaSup / GetResistance();
        Current = iQuiescente + Math.Max(0, iAlto) + Math.Max(0, deltaSup / (ResistenciaDivisorSuperior + ResistenciaDivisorInferior));
        Power = Math.Abs(deltaSup * iQuiescente) + potenciaDrivers;

        if (!EvaluarMinimosOperacion(deltaSup, Current, Power))
        {
            Energizado = false;
            _salidaAlta = false;
        }

        // Aplicar los nuevos estados de salida y descarga para el próximo tick.
        AplicarDrivers();

        // Chequeo unificado de límites eléctricos (respeta ∞ por separado).
        double picoCorriente = Math.Max(Math.Abs(Current), Math.Max(Math.Abs(CorrienteSalida), Math.Abs(CorrienteDescarga)));
        string? motivo = null;
        if (Power > MaxPower) motivo = "potencia";
        else if (picoCorriente > MaxCurrent) motivo = "corriente";
        else if (deltaSup > MaxVoltage) motivo = "voltaje";

        if (motivo != null)
        {
            _tiempoAcumuladoSobrecarga += deltaTime;
            if (_tiempoAcumuladoSobrecarga >= SegundosParaQuemarse)
            {
                Quemar(motivo);
            }
        }
        else
        {
            _tiempoAcumuladoSobrecarga = 0;
        }
    }

    private void AplicarDrivers()
    {
        if (IsBurnt || EsIncompatible || !Energizado)
        {
            _driverAlto.FijarResistencia(CircuitConstants.OpenResistance);
            _driverBajo.FijarResistencia(CircuitConstants.OpenResistance);
            _discharge.FijarResistencia(CircuitConstants.OpenResistance);
            return;
        }

        double rOut = Math.Max(ResistenciaSalida, 1.0);
        double rDisch = Math.Max(ResistenciaDescarga, 1.0);
        if (_salidaAlta)
        {
            _driverAlto.FijarResistencia(rOut);
            _driverBajo.FijarResistencia(CircuitConstants.OpenResistance);
            _discharge.FijarResistencia(CircuitConstants.OpenResistance);
        }
        else
        {
            _driverAlto.FijarResistencia(CircuitConstants.OpenResistance);
            _driverBajo.FijarResistencia(rOut);
            _discharge.FijarResistencia(rDisch);
        }
    }

    private void RestaurarRedPasiva()
    {
        _rDivSup.FijarResistencia(ResistenciaDivisorSuperior);
        _rDivInf.FijarResistencia(ResistenciaDivisorInferior);
        _pullReset.FijarResistencia(ResistenciaPullupReset);
        _inTrig.FijarResistencia(ResistenciaEntradaAltaZ);
        _inThres.FijarResistencia(ResistenciaEntradaAltaZ);
    }

    private void ApagarInterno()
    {
        Energizado = false;
        _salidaAlta = false;
        foreach (var arista in AristasInternas)
        {
            if (arista is AristaInterna a) a.MatarPorDueno();
        }
    }

    protected override void Quemar()
    {
        ApagarInterno();
        base.Quemar();
    }

    protected override void Reparado()
    {
        base.Reparado();
        _salidaAlta = false;
        _tiempoDesdeUltimoFlanco = 0;
        _tiempoAcumuladoSobrecarga = 0;
        FrecuenciaMedidaHz = 0;
        foreach (var arista in AristasInternas)
        {
            if (arista is AristaInterna a) a.RevivirPorDueno(Temperatura);
        }
        RestaurarRedPasiva();
        AplicarDrivers();
    }

    private sealed class AristaInterna : Component
    {
        private double _resistencia;

        public override bool AbsorbeContagio => false;

        public AristaInterna(string name, Pin a, Pin b, double resistenciaInicial) : base(name, a, b)
        {
            _resistencia = resistenciaInicial;
            MaxVoltage = double.PositiveInfinity;
            MaxCurrent = double.PositiveInfinity;
            MaxPower = double.PositiveInfinity;
            MaxTemperature = double.PositiveInfinity;
            FraccionCalorDisipado = 0;
        }

        public override double GetResistance() => _resistencia;

        public void FijarResistencia(double ohms) =>
            _resistencia = Math.Max(CircuitConstants.MinResistance, ohms);

        public void MatarPorDueno() => Quemar("se quemó el integrado");

        public void RevivirPorDueno(double temperatura) => Reparar(temperatura);
    }
}
