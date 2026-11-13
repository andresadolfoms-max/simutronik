namespace ElectricSim.Core;

/// <summary>
/// Un componente que además de resistencia tiene una FEM INTERNA propia: su
/// rama no cumple V = I·R sino V = I·R + offset. Es el modelo compañero de
/// los elementos que guardan energía (capacitor, inductor) y, más adelante,
/// del motor con su fuerza contraelectromotriz y del potenciómetro.
///
/// Antes el solver preguntaba literalmente `componente is Capacitor` en tres
/// lugares (RelajarRepresentantes, CalcularCorrientesDeFuentes y
/// AsignarCorrientesDeFusionados), así que cada componente nuevo con fem
/// tenía que ir a agregar su propio `if` adentro del motor. Ahora alcanza con
/// implementar esta interfaz.
/// </summary>
public interface IComponenteConOffset
{
    /// <summary>
    /// La fem interna con SIGNO, vista de PinA a PinB. La ecuación de la rama
    /// es siempre: V(PinA) − V(PinB) = ResistenciaParaSolver()·I + OffsetVoltaje.
    /// </summary>
    double OffsetVoltaje { get; }

    /// <summary>
    /// Recalcular la resistencia equivalente del modelo compañero para este
    /// tick (dt/C en el capacitor, L/dt en el inductor). Se llama UNA vez por
    /// tick, nunca por iteración interna: la resistencia queda congelada
    /// durante todo el tick, que es la regla de oro del solver.
    /// </summary>
    void Preparar(double deltaTime);
}
