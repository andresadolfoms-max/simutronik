namespace ElectricSim.Core.Components;

/// <summary>
/// Bobina (inductor), modelada con el MISMO modelo compañero de backward
/// Euler que ya está probado en el Capacitor — espejado. La ecuación de un
/// inductor es V = L·di/dt; discretizada hacia atrás queda:
///
///     V = L·(I − I_anterior)/dt = R_eq·I − R_eq·I_anterior
///     con  R_eq = L/dt   (el espejo exacto de dt/C del capacitor)
///
/// o sea una resistencia R_eq en serie con una fem de −R_eq·I_anterior, que
/// es justo lo que pide IComponenteConOffset.
///
/// De ese modelo salen GRATIS las cuatro cosas que el inductor viejo no tenía
/// (A2), y por eso se reescribió entero en vez de parchearlo:
///
///  1. τ = L/R DE VERDAD. El viejo tenía un `SegundosEstablecimiento` puesto
///     a mano: tardaba 2 s pusieras 10Ω o 10kΩ al lado. Ahora la bobina
///     reacciona al circuito, que es la mitad de la lección.
///  2. GUARDA sin disipar. El viejo interpolaba su resistencia entre 4000Ω y
///     0.8Ω y quemaba 19-37 mW mientras "cargaba el campo". Acá lo único que
///     calienta es el cobre: PotenciaTermica = I²·ResistenciaBobinado.
///  3. El campo COLAPSA al abrir. El viejo se quedaba con NivelFlujo = 1.00
///     medio segundo después de abrir la llave.
///  4. Aparece el PICO INVERSO. Es el 100% de para qué se enseña un inductor
///     (la chispa, el relé, el diodo volante). Ver la nota de abajo.
///
/// SOBRE EL PICO, con honestidad: el pico que muestra este modelo vale
/// L·I/dt, o sea que CRECE con los ticks/s (a 60 t/s con 1H y 88mA da ~5V; a
/// 1000 t/s da ~88V). El pico real de abrir un circuito de 1MΩ sería de
/// decenas de kV pero dura microsegundos: un simulador de paso fijo no lo
/// puede mostrar, y fingirlo sería mentir. Lo que se ve es el pico promediado
/// sobre un tick, que es el número correcto para la resolución que hay.
/// </summary>
public class Inductor : Component, IComponenteConOffset
{
    /// <summary>Inductancia en Henrios. ES el parámetro del componente: de acá sale τ = L/R.</summary>
    public double Inductancia { get; set; }

    /// <summary>Resistencia óhmica del alambre del bobinado. Lo ÚNICO que disipa calor.</summary>
    public double ResistenciaBobinado { get; set; } = 0.8;

    /// <summary>La corriente del tick anterior: el "estado" del inductor, como VoltajeAlmacenado lo es del capacitor.</summary>
    public double CorrienteAnterior { get; private set; }

    /// <summary>Energía guardada en el campo magnético: E = ½·L·I² (Joules).</summary>
    public double EnergiaAlmacenada =>
        IsBurnt ? 0 : 0.5 * Inductancia * Current * Current;

    /// <summary>Cuán "cargado" está el campo, 0..1 normalizado a 1A. Para la barra del inspector.</summary>
    public double NivelFlujo => Math.Min(1.0, Math.Abs(Current));

    /// <summary>Guardar campo NO disipa: lo único que calienta es el cobre del bobinado (QA9).</summary>
    public override double PotenciaTermica =>
        IsBurnt ? 0 : Current * Current * ResistenciaBobinado;

    private double _resistenciaEquivalente = CircuitConstants.OpenResistance;

    public Inductor(string name, double henrios = 1.0) : base(name)
    {
        Inductancia = henrios;
        MaxVoltage = 400;   // el aislante del bobinado: el pico inverso lo puede perforar
        CapacidadTermica = 1.5;
        MaxTemperature = 150;
    }

    /// <summary>R_eq = L/dt. Espejo de dt/C. Una vez por tick.</summary>
    public void Preparar(double deltaTime)
    {
        _resistenciaEquivalente = Math.Max(
            Inductancia / Math.Max(deltaTime, 1e-6), CircuitConstants.MinResistance);
    }

    /// <summary>La R total de la rama: la del método numérico más el cobre real.</summary>
    private double ResistenciaTotal =>
        Math.Max(_resistenciaEquivalente + ResistenciaBobinado, CircuitConstants.MinResistance);

    /// <summary>La fem del modelo compañero: −R_eq·I_anterior. Es la que produce el pico inverso.</summary>
    public double OffsetVoltaje => -_resistenciaEquivalente * CorrienteAnterior;

    public override double GetResistance() =>
        IsBurnt ? CircuitConstants.OpenResistance : ResistenciaTotal;

    public override void PostSolve(double deltaTime)
    {
        if (ResultadoAC is { } alterna && !IsBurnt)
        {
            // En alterna NO vale el modelo companion de continua: vale la
            // reactancia, que ya resolvió el SolverAlterna para toda la red.
            VoltajeEnBornes = alterna.VoltajeRms;
            Current = alterna.CorrienteRms;
            Power = alterna.PotenciaActiva;
            CorrienteAnterior = 0;   // en alterna no se integra el campo
            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ChequearQuemado(deltaTime);
            return;
        }

        if (IsBurnt)
        {
            Current = 0;
            Power = 0;
            VoltajeEnBornes = 0;
            CorrienteAnterior = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (FusionadoEsteTick)
        {
            VoltajeEnBornes = 0;
            Current = CorrienteFusionada;
            Power = Current * Current * ResistenciaBobinado;
            CorrienteAnterior = Current;
            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ChequearQuemado(deltaTime);
            return;
        }

        double deltaV = PinA.Voltage - PinB.Voltage;
        // Ohm generalizado: ΔV = R_total·I + offset  →  I = (ΔV − offset)/R_total
        Current = (deltaV - OffsetVoltaje) / ResistenciaTotal;
        VoltajeEnBornes = Math.Abs(deltaV);
        Power = Current * Current * ResistenciaBobinado;   // disipada, no la del campo
        CorrienteAnterior = Current;

        EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
        ChequearQuemado(deltaTime);
    }

    protected override void Quemar()
    {
        base.Quemar();
        CorrienteAnterior = 0;
    }

    protected override void Reparado()
    {
        CorrienteAnterior = 0;
        _resistenciaEquivalente = CircuitConstants.OpenResistance;
    }
}
