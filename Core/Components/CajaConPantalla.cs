namespace ElectricSim.Core.Components;

/// <summary>
/// Caja con pantalla (instrumento de paso ideal): la corriente entra por el
/// borne positivo (PinA, +) y sale por el borne negativo (PinB, −) exactamente
/// igual a como entró (sin caída de voltaje ni resistencia serie), mostrando
/// en su pantalla superior y en el inspector todas las estadísticas
/// significativas del circuito en ese punto:
///  - Voltaje
///  - Amperaje (corriente)
///  - Potencia
///  - Temperatura (si la función térmica está activada, o "-" si está desactivada)
///  - Si es corriente alterna y, de serlo, todos sus valores (frecuencia,
///    Vrms, Vpico, Irms, Ipico, desfase φ, factor de potencia cos φ,
///    potencia activa P, aparente S y reactiva Q).
/// </summary>
public class CajaConPantalla : Component
{
    /// <summary>
    /// Voltaje medido en el nodo por el que pasa la corriente (calculado por
    /// CircuitGraph respecto a la referencia del circuito).
    /// </summary>
    public double VoltajeNodoMedido { get; internal set; }

    /// <summary>Voltaje efectivo mostrado por la caja (Volts).</summary>
    public double VoltajeMedido { get; private set; }

    /// <summary>Frecuencia de la red cuando circula corriente alterna (Hz).</summary>
    public double FrecuenciaHz { get; internal set; } = 50.0;

    /// <summary>True cuando por la caja circula corriente alterna.</summary>
    public bool EsCorrienteAlterna => (TipoCorrienteRecibida & TipoCorriente.AC) != 0;

    /// <summary>Tensión eficaz en alterna (Vrms).</summary>
    public double VoltajeRms => EsCorrienteAlterna ? (ResultadoAC?.VoltajeRms ?? VoltajeMedido) : 0.0;

    /// <summary>Tensión de pico en alterna (Vp = Vrms · √2).</summary>
    public double VoltajePico => VoltajeRms * Math.Sqrt(2.0);

    /// <summary>Corriente eficaz en alterna (Irms, en Amperes).</summary>
    public double CorrienteRms => EsCorrienteAlterna ? (ResultadoAC?.CorrienteRms ?? Math.Abs(Current)) : 0.0;

    /// <summary>Corriente de pico en alterna (Ip = Irms · √2, en Amperes).</summary>
    public double CorrientePico => CorrienteRms * Math.Sqrt(2.0);

    /// <summary>Desfase entre tensión y corriente en alterna (°).</summary>
    public double FaseGrados => EsCorrienteAlterna ? (ResultadoAC?.FaseGrados ?? 0.0) : 0.0;

    /// <summary>Factor de potencia (cos φ = P / S) en alterna.</summary>
    public double FactorPotencia =>
        !EsCorrienteAlterna ? 0.0
        : ResultadoAC != null ? ResultadoAC.FactorPotencia
        : CorrienteRms > 1e-9 ? 1.0 : 0.0;

    /// <summary>Potencia activa P (Watts).</summary>
    public double PotenciaActiva =>
        EsCorrienteAlterna
            ? (ResultadoAC?.PotenciaActiva ?? (VoltajeRms * CorrienteRms))
            : Power;

    /// <summary>Potencia aparente S = Vrms · Irms (VA) en alterna.</summary>
    public double PotenciaAparente =>
        EsCorrienteAlterna
            ? (ResultadoAC?.PotenciaAparente ?? (VoltajeRms * CorrienteRms))
            : 0.0;

    /// <summary>Potencia reactiva Q (VAr) en alterna.</summary>
    public double PotenciaReactiva =>
        EsCorrienteAlterna
            ? (ResultadoAC?.PotenciaReactiva ?? 0.0)
            : 0.0;

    /// <summary>No disipa calor propio: es un instrumento ideal de cálculo.</summary>
    public override double PotenciaTermica => 0.0;

    public CajaConPantalla(string name = "Caja con pantalla") : base(name)
    {
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;

        // Instrumento ideal: no tiene límites de ruptura y acepta AC y DC.
        Compatibilidad = CompatibilidadCorriente.ACyDC;
        MaxVoltage = double.PositiveInfinity;
        MaxCurrent = double.PositiveInfinity;
        MaxPower = double.PositiveInfinity;
        MaxTemperature = double.PositiveInfinity;
    }

    public override double GetResistance() => 0.0;

    /// <summary>
    /// Conexión de paso perfecta: todo lo que entra por el borne positivo (+)
    /// sale exactamente igual por el borne negativo (−), sin caída de tensión.
    /// </summary>
    public override bool EsConexionPerfecta() => !IsBurnt;

    public override void PostSolve(double deltaTime)
    {
        if (IsBurnt)
        {
            VoltajeMedido = 0;
            VoltajeEnBornes = 0;
            Current = 0;
            Power = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (FusionadoEsteTick)
        {
            if (ResultadoAC is { } ac)
            {
                VoltajeMedido = ac.VoltajeRms;
                Current = ac.CorrienteRms;
                Power = ac.PotenciaActiva;
            }
            else
            {
                VoltajeMedido = VoltajeNodoMedido;
                Current = CorrienteFusionada;
                Power = VoltajeMedido * Math.Abs(Current);
            }
            VoltajeEnBornes = VoltajeMedido;
            EvaluarMinimosOperacion(VoltajeMedido, Current, Power);
            ChequearQuemado(deltaTime);
            return;
        }

        // Caso de respaldo: conectada directamente en paralelo a una fuente sin carga.
        VoltajeMedido = Math.Abs(PinA.Voltage - PinB.Voltage);
        VoltajeEnBornes = VoltajeMedido;
        Current = 0.0;
        Power = 0.0;
        EvaluarMinimosOperacion(VoltajeMedido, Current, Power);
        ChequearQuemado(deltaTime);
    }

    /// <summary>Formatea una corriente en A o mA según su magnitud.</summary>
    public static string FormatearCorriente(double amperios)
    {
        double abs = Math.Abs(amperios);
        return abs >= 1.0
            ? $"{amperios:F2} A"
            : $"{amperios * 1000.0:F1} mA";
    }

    /// <summary>Formatea una tensión en V o mV según su magnitud.</summary>
    public static string FormatearVoltaje(double volts)
    {
        double abs = Math.Abs(volts);
        return abs > 0.0 && abs < 0.1
            ? $"{volts * 1000.0:F1} mV"
            : $"{volts:F2} V";
    }

    /// <summary>Formatea una potencia en W o mW según su magnitud.</summary>
    public static string FormatearPotencia(double watts)
    {
        double abs = Math.Abs(watts);
        return abs >= 1.0
            ? $"{watts:F2} W"
            : $"{watts * 1000.0:F1} mW";
    }

    /// <summary>
    /// Texto compacto para el display 3D sobre la cara superior de la caja.
    /// </summary>
    public string TextoPantalla3D()
    {
        string tempTexto = SimuladorTermico.Habilitada ? $"{Temperatura:F1} °C" : "-";
        string vTexto = FormatearVoltaje(VoltajeMedido);
        string iTexto = FormatearCorriente(Math.Abs(Current));

        if (EsCorrienteAlterna)
        {
            return
                $"V: {vTexto} | I: {iTexto}\n" +
                $"T: {tempTexto} | AC {FrecuenciaHz:F0} Hz\n" +
                $"Vrms:{VoltajeRms:F1}V  Vp:{VoltajePico:F1}V\n" +
                $"Irms:{FormatearCorriente(CorrienteRms)} Ip:{FormatearCorriente(CorrientePico)}\n" +
                $"φ:{FaseGrados:F1}°  cos φ:{FactorPotencia:F2}\n" +
                $"P:{FormatearPotencia(PotenciaActiva)} S:{PotenciaAparente:F1}VA\n" +
                $"Q: {PotenciaReactiva:F1} VAr";
        }

        string tipoTexto = TipoCorrienteRecibida == TipoCorriente.DC ? "No (DC)" : "No";
        return
            $"Volt: {vTexto}\n" +
            $"Amp: {iTexto}\n" +
            $"Pot: {FormatearPotencia(Power)}\n" +
            $"Temp: {tempTexto}\n" +
            $"AC: {tipoTexto}";
    }
}

