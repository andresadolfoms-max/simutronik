namespace ElectricSim.Core;

/// <summary>
/// El CÓDIGO DE COLORES de las resistencias (sistema de 4 bandas): dos
/// dígitos significativos, un multiplicador (potencia de 10) y la
/// tolerancia. Es lo primero que aprende cualquiera que arma un circuito en
/// serio, así que el modelo 3D de la resistencia pinta sus bandas con ESTO,
/// leyendo el valor real del componente: si cambiás 220Ω por 10kΩ en el
/// inspector, las bandas cambian de rojo-rojo-marrón a marrón-negro-naranja.
///
/// Vive en el Core (C# puro) para poder testearlo sin abrir el juego.
/// </summary>
public static class CodigoColores
{
    /// <summary>Nombres de los colores de dígito, del 0 al 9.</summary>
    public static readonly string[] NombresDigitos =
    {
        "negro", "marrón", "rojo", "naranja", "amarillo",
        "verde", "azul", "violeta", "gris", "blanco",
    };

    /// <summary>Exponente mínimo representable con 4 bandas (plata = ×0,01).</summary>
    public const int ExponenteMinimo = -2;

    /// <summary>Exponente máximo representable con 4 bandas (blanco = ×10⁹).</summary>
    public const int ExponenteMaximo = 9;

    /// <summary>
    /// Descompone un valor en ohms en sus tres bandas de valor:
    /// (primer dígito, segundo dígito, exponente del multiplicador).
    /// El valor se redondea a dos cifras significativas, que es lo único que
    /// el código de 4 bandas puede expresar (4,7kΩ sí; 4,75kΩ no).
    /// Valores fuera de rango se recortan a lo representable.
    /// </summary>
    public static (int Digito1, int Digito2, int Exponente) Bandas(double ohms)
    {
        if (double.IsNaN(ohms) || ohms <= 0) return (0, 0, 0);
        if (double.IsPositiveInfinity(ohms)) return (9, 9, ExponenteMaximo);

        // Dos cifras significativas: 220 -> 22 × 10^1 ; 4.7 -> 47 × 10^-1.
        int exponente = (int)Math.Floor(Math.Log10(ohms)) - 1;
        int significativo = (int)Math.Round(ohms / Math.Pow(10, exponente), MidpointRounding.AwayFromZero);

        // El redondeo puede dar 100 (ej. 995Ω -> 99,5 -> 100): se corre una década.
        if (significativo >= 100)
        {
            significativo /= 10;
            exponente++;
        }

        if (exponente < ExponenteMinimo)
        {
            // Más chico que 0,10Ω: lo mínimo que se puede pintar.
            return (1, 0, ExponenteMinimo);
        }
        if (exponente > ExponenteMaximo)
        {
            return (9, 9, ExponenteMaximo);
        }

        return (significativo / 10, significativo % 10, exponente);
    }

    /// <summary>El nombre del color de la banda multiplicadora (incluye oro = ×0,1 y plata = ×0,01).</summary>
    public static string NombreMultiplicador(int exponente) => exponente switch
    {
        -1 => "dorado",
        -2 => "plateado",
        _ => NombresDigitos[Math.Clamp(exponente, 0, 9)],
    };

    /// <summary>
    /// Descripción legible para el inspector, por ejemplo:
    /// "rojo - rojo - marrón - dorado  (22 × 10¹ Ω, ±5 %)".
    /// </summary>
    public static string Describir(double ohms)
    {
        var (d1, d2, exp) = Bandas(ohms);
        return $"{NombresDigitos[d1]} - {NombresDigitos[d2]} - {NombreMultiplicador(exp)} - dorado  " +
               $"({d1}{d2} × 10^{exp} Ω, ±5 %)";
    }
}

