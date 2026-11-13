namespace ElectricSim.Core;

/// <summary>
/// Cualquier componente que "empuje" un voltaje fijo a la red en vez de dejar
/// que el solver lo calcule. Hoy lo implementa FuenteBase (de la que heredan
/// Battery y FuenteAC); el día de mañana un pin digital de Arduino en
/// HIGH/LOW también va a poder implementar esto.
/// </summary>
public interface IVoltageSource
{
    /// <summary>Se llama una vez por tick, antes de relajar la red, para fijar los voltajes de sus pines.</summary>
    void FixVoltages();
}
