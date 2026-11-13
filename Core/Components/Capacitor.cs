namespace ElectricSim.Core.Components;

/// <summary>
/// Capacitor con MODELO COMPANION de SPICE. El cap se ve como una arista
/// entre PinA y PinB con una resistencia R_eq = dt/C (backward Euler) en
/// serie con una fem interna V_alm. La ley del cap sale sola:
///
///     V(PinA) − V(PinB) = V_alm + I·R_eq       (Ohm generalizado)
///     dV_alm/dt = I / C                        (definición del cap)
///
/// Backward Euler con R_eq = dt/C es INCONDICIONALMENTE ESTABLE: el paso
/// puede ser MUCHO más grande que τ=R·C y aún así el cap converge suavemente
/// a la exponencial física (V(t) = V_final·(1−e^(−t/τ))). Reemplaza el
/// placeholder de rampa lineal de la v2 y el modelo de "fuente virtual"
/// intermedio de la v3.
///
/// EN LA RELAJACIÓN: la fem interna V_alm aparece como OFFSET. Cuando el
/// solver actualiza V(PinA), el vecino PinB "aporta" V(PinB) + V_alm; y al
/// revés para PinB. Aristas normales (resistores, cables) tienen offset=0.
///
/// EFECTOS EMERGENTES CORRECTOS:
/// - Se carga siguiendo la exponencial real (63% en τ, 99% en 5·τ).
/// - Depende de R en serie: R chica → carga rápida; R grande → lenta.
/// - Descarga por LED: mientras V_alm > umbral, sigue empujando; se apaga
///   suavemente cuando se vacía.
/// - Polaridad INVERTIDA: carga con V_alm negativo, se detiene cuando
///   ‖V_alm‖ = V_fuente (los cerámicos son simétricos, esto lo modela solo).
/// - Detrás de llave ABIERTA: fuga mínima (nada absurdo como "100% en 8s").
/// - Después de sacar la fuente, sigue actuando como fuente débil (empuja
///   V_alm) hasta descargarse — el experimento clásico "cargar y flashear LED".
///
/// Es SOLO-DC. En corriente alterna un cap real tiene reactancia; modelar
/// eso bien pide el solver de fasores (congelado — ver Deprecated/ALCANCE_CONGELADO.md).
/// </summary>
public class Capacitor : Component, IComponenteConOffset, IVoltageSource
{
    /// <summary>Capacitancia en Faradios. Cuanto más grande, más tarda en cargarse/descargarse.</summary>
    public double Capacitance { get; set; }

    /// <summary>
    /// Voltaje CON SIGNO entre PinA y PinB en equilibrio (I=0). Es la fem
    /// interna del modelo companion. Positivo = PinA por arriba de PinB
    /// (polaridad "normal"); negativo = cap cargado al revés (los cerámicos
    /// son simétricos, un electrolítico revienta — ver CapacitorElectrolitico).
    /// </summary>
    public double VoltajeAlmacenado { get; internal set; }

    /// <summary>
    /// Nivel de carga (0..1) relativo a MaxVoltage. Con MaxVoltage = ∞ (el
    /// "Capacitor ∞" del sandbox) la división daba 0% para siempre; en ese
    /// caso se usa VoltajeReferenciaCarga como escala para que la barra del
    /// inspector siga significando algo.
    /// </summary>
    public double ChargeLevel => double.IsPositiveInfinity(MaxVoltage)
        ? Math.Min(Math.Abs(VoltajeAlmacenado) / Math.Max(VoltajeReferenciaCarga, 0.1), 1.0)
        : Math.Min(Math.Abs(VoltajeAlmacenado) / Math.Max(MaxVoltage, 0.1), 1.0);

    /// <summary>Escala del indicador de carga cuando MaxVoltage es infinito (sandbox).</summary>
    public double VoltajeReferenciaCarga { get; set; } = 25;

    /// <summary>
    /// True mientras el cap tiene carga apreciable. Se usa para propagación
    /// AC/DC: un cap cargado empuja DC al circuito conectado.
    /// </summary>
    public bool EstaEntregando => Math.Abs(VoltajeAlmacenado) > 0.1;

    /// <summary>
    /// Corriente asignada = Current del cap. Se mantiene por compatibilidad de
    /// API con inspector y código legacy; ambas apuntan al mismo número físico.
    /// </summary>
    public double CorrienteAsignada => Current;

    /// <summary>
    /// La fem interna del cap, expuesta al solver para el cálculo del offset
    /// en la relajación y de la corriente por Ohm generalizado.
    /// </summary>
    public double OffsetVoltaje => VoltajeAlmacenado;

    /// <summary>
    /// Energía guardada en el campo eléctrico: E = ½·C·V² (Joules). ES el
    /// número que el alumno quiere ver de un capacitor — no una potencia.
    /// </summary>
    public double EnergiaAlmacenada =>
        IsBurnt ? 0 : 0.5 * Capacitance * VoltajeAlmacenado * VoltajeAlmacenado;

    /// <summary>
    /// Potencia que entra o sale del campo (|V_alm·I|, Watts). Es lo que
    /// ANTES se mostraba como "Power", y es otra cosa: un cap cargándose
    /// marcaba 20 W sin calentarse un grado. Se conserva como lectura propia
    /// para quien la quiera, pero no es potencia disipada ni la mira
    /// ChequearQuemado.
    /// </summary>
    public double PotenciaAlmacenamiento =>
        IsBurnt ? 0 : Math.Abs(VoltajeAlmacenado * Current);

    /// <summary>
    /// Resistencia serie equivalente (ESR, Ohms): las pérdidas REALES del
    /// capacitor — lo único suyo que genera calor. La energía que guarda en
    /// el campo NO calienta. (Antes PotenciaTermica heredaba Power×fracción,
    /// y Power acá es |V_alm·I| = potencia de ALMACENAMIENTO: un supercap
    /// cargándose "generaba" tantos Joules de calor como los que guardaba,
    /// rompiendo la conservación de energía — QA9.)
    /// </summary>
    public double ResistenciaSerie { get; set; } = 0.05;

    // R interna dinámica: se recalcula cada tick como dt/C (backward Euler).
    private double _rInterna = 1.0;

    /// <summary>
    /// El calor de un capacitor son SOLO las pérdidas I²·ESR (unos milivatios
    /// en una carga normal), nunca la energía almacenada.
    /// </summary>
    public override double PotenciaTermica =>
        IsBurnt ? 0 : LeyesElectricas.PotenciaDesdeIR(Current, ResistenciaSerie);

    public Capacitor(string name, double capacitanceFarads) : base(name)
    {
        Capacitance = capacitanceFarads;
        // Un capacitor común (cerámico, de film) SÍ funciona en alterna: deja
        // pasar corriente con reactancia Xc = 1/(2π·f·C), que es de hecho para
        // lo que se usa. Sólo el electrolítico es polarizado y no la soporta.
        Compatibilidad = CompatibilidadCorriente.ACyDC;
        MaxVoltage = 25; // típico de un electrolítico chico; editable
    }

    /// <summary>
    /// Backward Euler: R_eq = dt/C. Se llama al inicio de cada tick, antes
    /// del solve. Con dt/C grande, el cap se comporta como fuente casi-abierta
    /// (mucha resistencia efectiva) que se carga suavemente siguiendo la
    /// exponencial real, incluso si dt >> τ.
    ///
    /// SIN piso artificial. R_eq = dt/C es la resistencia FÍSICA del método
    /// (backward Euler). Antes había un piso de 0.1Ω que le sumaba una
    /// resistencia en serie que no existe: con 1F la constante de tiempo se
    /// desviaba 5%, con 10F un 28%, y la energía no se conservaba (la fuente
    /// daba 248J, el cap guardaba 125J y el resto "desaparecía"). Lo que acota
    /// la corriente de pico es la ESR, que ya va en ResistenciaTotal.
    /// </summary>
    public void Preparar(double deltaTime)
    {
        if (IsBurnt) return;
        _rInterna = Math.Max(CircuitConstants.MinResistance, deltaTime / Math.Max(Capacitance, 1e-9));
    }

    /// <summary>
    /// La resistencia REAL de la rama del capacitor: la R del método numérico
    /// (dt/C) MÁS la ESR física. Que la ESR estuviera afuera del solver era el
    /// bug A6a: la corriente de pico quedaba acotada sólo por dt/C, o sea por
    /// el slider de ticks/s, y una ESR de 100 Ω no limitaba nada. Con la ESR
    /// adentro, el pico es V/(ESR + R_int_fuente) y deja de depender de dt.
    /// </summary>
    private double ResistenciaTotal =>
        Math.Max(_rInterna + ResistenciaSerie, CircuitConstants.MinResistance);

    public override double GetResistance()
    {
        if (IsBurnt) return CircuitConstants.OpenResistance;
        return ResistenciaTotal;
    }

    /// <summary>
    /// El cap ya NO fija sus bornes — su fem interna la aplica el solver de
    /// relajación como offset. Se deja el método para respetar IVoltageSource
    /// (que lo llama CircuitGraph), pero es un no-op.
    /// </summary>
    public void FixVoltages() { }

    public override void PostSolve(double deltaTime)
    {
        if (ResultadoAC is { } alterna && !IsBurnt)
        {
            // En alterna NO vale el modelo companion de continua: vale la
            // reactancia, que ya resolvió el SolverAlterna para toda la red.
            VoltajeEnBornes = alterna.VoltajeRms;
            Current = alterna.CorrienteRms;
            Power = alterna.PotenciaActiva;
            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ChequearQuemado(deltaTime);
            return;
        }

        // NO llamamos a base.PostSolve porque calcularía Current con Ohm
        // normal (ΔV/R), y como el cap tiene fem interna eso es incorrecto.
        // Replicamos acá los casos borde relevantes y calculamos Current con
        // Ohm generalizado.
        if (IsBurnt)
        {
            Current = 0;
            Power = 0;
            VoltajeEnBornes = 0;
            LimpiarMinimosOperacion();
            return;
        }
        if (EsIncompatible)
        {
            // AC en un cap SoloDC: inerte. No se carga.
            VoltajeEnBornes = Math.Abs(PinA.Voltage - PinB.Voltage);
            Current = 0;
            Power = 0;
            LimpiarMinimosOperacion();
            return;
        }
        if (FusionadoEsteTick)
        {
            // Con R_eq del cap normalmente alto (dt/C), no debería fusionarse;
            // pero por las dudas, respetamos la lógica de base.
            VoltajeEnBornes = 0;
            Power = 0;
            Current = CorrienteFusionada;
            EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
            ActualizarEstado(deltaTime);
            ChequearQuemado(deltaTime);
            return;
        }

        // Ohm generalizado: I = (ΔV − V_alm) / R_eq. Signo consistente:
        // positiva = fluye de PinA a PinB (carga el cap en polaridad +).
        double deltaV = PinA.Voltage - PinB.Voltage;
        // MISMA R que vio el solver, o el número que reporta el componente no
        // tiene nada que ver con el que resolvió el circuito.
        double r = ResistenciaTotal;
        Current = (deltaV - VoltajeAlmacenado) / r;
        VoltajeEnBornes = Math.Abs(deltaV);
        // Power = potencia DISIPADA (I²·ESR), igual que en todos los demás
        // componentes y coherente con PotenciaTermica. La de almacenamiento
        // vive aparte, en PotenciaAlmacenamiento (A6b).
        Power = LeyesElectricas.PotenciaDesdeIR(Current, ResistenciaSerie);

        EvaluarMinimosOperacion(VoltajeEnBornes, Current, Power);
        ActualizarEstado(deltaTime);
        ChequearQuemado(deltaTime);
    }

    protected override void ActualizarEstado(double deltaTime)
    {
        // Ley del cap: dV_alm/dt = I/C. Con signo consistente: I > 0 (fluye
        // A→B) sube V_alm; I < 0 (fluye B→A) lo baja. Funciona en ambas
        // polaridades sin código especial.
        VoltajeAlmacenado += Current * deltaTime / Math.Max(Capacitance, 1e-9);
    }

    protected override void Quemar()
    {
        base.Quemar();
        VoltajeAlmacenado = 0;
        Current = 0;
    }

    /// <summary>Reparado = capacitor nuevo: descargado del todo.</summary>
    protected override void Reparado()
    {
        VoltajeAlmacenado = 0;
    }
}
