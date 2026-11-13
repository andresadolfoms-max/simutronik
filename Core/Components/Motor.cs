namespace ElectricSim.Core.Components;

/// <summary>
/// Motor de continua con FUERZA CONTRAELECTROMOTRIZ. Es el modelo mínimo
/// honesto de una máquina de continua, y se apoya en el offset genérico del
/// Paso 5-1 (IComponenteConOffset): la FCEM es literalmente la fem interna
/// del componente.
///
///     V_fcem = k_e · ω
///     I      = (V_bornes − V_fcem) / R_bobinado
///     Par    = k_t · I
///     dω/dt  = (Par − Par_carga − fricción·ω) / J
///
/// El motor viejo hacía `RPM = RPMMaximas · (Power / PotenciaNominal)` y de
/// ahí salían los tres síntomas de A8:
///
///  1. La corriente NO dependía de la velocidad, así que no había
///     sobrecorriente de arranque. Ahora sale sola: con el rotor quieto la
///     FCEM es 0 y el motor chupa V/R (~6.6× su corriente de régimen). Es la
///     razón por la que parpadean las luces cuando arranca un motor.
///  2. No había par de carga: no se podía frenar. Ahora `ParCarga` es la
///     perilla interesante del componente.
///  3. RPM ∝ potencia en vez de ∝ voltaje: con la mitad de volts giraba a UN
///     CUARTO. Ahora la mitad de volts da la mitad de RPM, que es lo real.
///
/// Y sale gratis el motor TRABADO quemándose: con el eje bloqueado la
/// corriente se queda en la de arranque, que supera su MaxCurrent.
///
/// EN ALTERNA queda INERTE, y eso es una decisión, no un olvido (Paso 7-3):
/// modelar el "vibra y se cocina" pide par instantáneo con la onda, o sea un
/// solver de fasores, que está fuera de alcance. Ver ALCANCE_CONGELADO.md.
/// </summary>
public class Motor : Component, IComponenteConOffset
{
    /// <summary>Resistencia del bobinado (Ω). Con el rotor quieto, ES lo único que limita la corriente.</summary>
    public double ResistenciaBobinado { get; set; } = 25;

    /// <summary>k_e (V·s/rad): cuántos volts de FCEM genera por cada rad/s. Define la velocidad máxima: ω_libre ≈ V/k_e.</summary>
    public double ConstanteFcem { get; set; } = 0.03;

    /// <summary>k_t (N·m/A): cuánto par da por amper. En unidades SI es numéricamente igual a k_e.</summary>
    public double ConstantePar { get; set; } = 0.03;

    /// <summary>Par que le colgás del eje (N·m). LA perilla del componente: subilo y el motor frena y chupa más.</summary>
    public double ParCarga { get; set; }

    /// <summary>Rozamiento propio (N·m·s/rad): lo que lo frena solo.</summary>
    public double Friccion { get; set; } = 6e-6;

    /// <summary>Momento de inercia J (kg·m²): cuánto tarda en tomar velocidad.</summary>
    public double Inercia { get; set; } = 1e-5;

    /// <summary>Velocidad angular con signo (rad/s). Negativa = gira al revés.</summary>
    public double VelocidadAngular { get; private set; }

    /// <summary>Velocidad en RPM, con signo.</summary>
    public double RPM => VelocidadAngular * 60.0 / (2.0 * Math.PI);

    public bool EstaGirando => Math.Abs(RPM) > 1;

    /// <summary>Par que está entregando ahora mismo (N·m).</summary>
    public double Par => ConstantePar * Current;

    /// <summary>Potencia MECÁNICA en el eje (W). Se va como trabajo, no como calor.</summary>
    public double PotenciaMecanica => Math.Abs(Par * VelocidadAngular);

    /// <summary>La FCEM: se OPONE a la corriente. Es exactamente el offset del modelo compañero.</summary>
    public double OffsetVoltaje => IsBurnt ? 0 : ConstanteFcem * VelocidadAngular;

    /// <summary>Solo calienta el COBRE. La potencia mecánica sale por el eje como trabajo.</summary>
    public override double PotenciaTermica =>
        IsBurnt ? 0 : Current * Current * ResistenciaBobinado;

    public Motor(string name, double resistenciaBobinado = 25) : base(name)
    {        // Polaridad REAL: conectarlo al revés cambia lo que hace.
        PinA.Polaridad = Polaridad.Positivo;
        PinB.Polaridad = Polaridad.Negativo;

        ResistenciaBobinado = resistenciaBobinado;
        Compatibilidad = CompatibilidadCorriente.SoloDC;
        MaxCurrent = 0.25;   // su corriente CONTINUA: el rotor bloqueado (~340mA) la supera y lo quema
        MaxPower = 12;
        CapacidadTermica = 2.5;
    }

    /// <summary>Su resistencia no depende del paso de tiempo, pero la interfaz lo pide.</summary>
    public void Preparar(double deltaTime) { }

    public override double GetResistance() =>
        IsBurnt ? CircuitConstants.OpenResistance : Math.Max(ResistenciaBobinado, CircuitConstants.MinResistance);

    public override void PostSolve(double deltaTime)
    {
        if (IsBurnt)
        {
            Current = 0; Power = 0; VoltajeEnBornes = 0;
            VelocidadAngular = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (EsIncompatible)
        {
            // Alterna: queda INERTE, y punto (Paso 7-3).
            VoltajeEnBornes = Math.Abs(PinA.Voltage - PinB.Voltage);
            Current = 0; Power = 0;
            VelocidadAngular = 0;
            LimpiarMinimosOperacion();
            return;
        }

        if (FusionadoEsteTick)
        {
            VoltajeEnBornes = 0;
            Current = CorrienteFusionada;
        }
        else
        {
            double deltaV = PinA.Voltage - PinB.Voltage;
            // Hay que RESTARLE la FCEM: el PostSolve genérico hace I = ΔV/R
            // y se comería el offset.
            Current = (deltaV - OffsetVoltaje) / Math.Max(GetResistance(), CircuitConstants.MinResistance);
            VoltajeEnBornes = Math.Abs(deltaV);
        }

        Power = Current * Current * ResistenciaBobinado;
        ActualizarEstado(deltaTime);
        ChequearQuemado(deltaTime);
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        if (IsBurnt) { VelocidadAngular = 0; LimpiarMinimosOperacion(); return; }

        double rBob = Math.Max(GetResistance(), CircuitConstants.MinResistance);
        bool enMovimiento = Math.Abs(VelocidadAngular) >= 0.5;
        double corrienteCapacidad = enMovimiento
            ? Math.Max(Math.Abs(Current), VoltajeEnBornes / rBob)
            : Math.Abs(Current);
        double potenciaCapacidad = VoltajeEnBornes * corrienteCapacidad;

        if (!EvaluarMinimosOperacion(VoltajeEnBornes, corrienteCapacidad, potenciaCapacidad))
        {
            VelocidadAngular = 0;
            return;
        }

        // El par de carga se opone SIEMPRE al movimiento (o al arranque).
        double sentido = VelocidadAngular != 0 ? Math.Sign(VelocidadAngular)
                       : Current != 0 ? Math.Sign(Current)
                       : 1;
        double parNeto = Par - ParCarga * sentido - Friccion * VelocidadAngular;

        VelocidadAngular += parNeto * deltaTime / Math.Max(Inercia, 1e-9);

        // Que no quede "temblando" en 0.3 RPM.
        if (Math.Abs(VelocidadAngular) < 0.05 && Math.Abs(Current) < 1e-6) VelocidadAngular = 0;
    }

    protected override void Quemar()
    {
        base.Quemar();
        VelocidadAngular = 0;
    }

    protected override void Reparado() => VelocidadAngular = 0;
}
