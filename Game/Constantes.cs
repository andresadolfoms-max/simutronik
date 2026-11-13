using Godot;
using ElectricSim.Core;

namespace ElectricSim.Game;

/// <summary>
/// Capas de colisión del mundo, como bits. Tener esto centralizado evita el
/// clásico bug de "el rayo choca contra cualquier cosa": cada raycast pide
/// exactamente la capa que le interesa (solo bornes, solo cuerpos, etc).
/// </summary>
public static class CapasFisicas
{
    public const uint Piso = 1;      // el piso de madera (para apoyar componentes)
    public const uint Cuerpos = 2;   // el cuerpo de cada componente (seleccionar / interactuar / arrastrar)
    public const uint Bornes = 4;    // las esferitas de conexión (iniciar y terminar cables)
    public const uint Cables = 8;    // los tubos de cable ya tendidos (seleccionarlos para editarlos)
    public const uint Manijas = 16;  // las esferitas de doblez de los cables físicos (agarrarlas y moverlas)
}

/// <summary>
/// Paleta de colores eléctricos del proyecto. El color de un borne (y del
/// cable que sale de él) lo decide su Polaridad: la convención de colores
/// real de una instalación.
/// </summary>
public static class Colores
{
    public static readonly Color Positivo = new(0.85f, 0.18f, 0.13f); // rojo
    public static readonly Color Negativo = new(0.14f, 0.14f, 0.16f); // negro
    public static readonly Color Fase = new(0.55f, 0.33f, 0.15f);     // marrón (fase AC)
    public static readonly Color Neutro = new(0.35f, 0.62f, 0.95f);   // celeste (neutro AC)
    public static readonly Color Ninguna = new(0.55f, 0.55f, 0.58f);  // gris
    public static readonly Color Senal = new(0.95f, 0.68f, 0.15f);    // amarillo (GPIO / señales de microcontrolador)

    /// <summary>
    /// Contacto directo entre bornes (metal tocando metal, sin cable). Cobre
    /// brillante: se distingue de cualquier color de cable y se ve aunque los
    /// bornes estén en modo Ocultos, que es cuando más falta hace.
    /// </summary>
    public static readonly Color Contacto = new(0.95f, 0.55f, 0.20f);

    /// <summary>Pulso de corriente sobre los cables: celeste eléctrico, distinto de todos los colores de polaridad.</summary>
    public static readonly Color Flujo = new(0.45f, 0.85f, 1.0f);

    public static readonly Color Seleccion = new(1.0f, 0.85f, 0.25f); // anillo amarillo
    public static readonly Color Fantasma = new(0.35f, 0.6f, 1.0f, 0.45f);
    public static readonly Color Quemado = new(0.02f, 0.02f, 0.02f, 0.7f);

    public static Color PorPolaridad(Polaridad p) => p switch
    {
        Polaridad.Positivo => Positivo,
        Polaridad.Negativo => Negativo,
        Polaridad.Fase => Fase,
        Polaridad.Neutro => Neutro,
        Polaridad.Senal => Senal,
        _ => Ninguna,
    };
}
