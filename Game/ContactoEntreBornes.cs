using Godot;
using ElectricSim.Core;

namespace ElectricSim.Game;

/// <summary>
/// Metal que se toca, conduce. Si dos bornes quedan lo bastante cerca, se
/// conectan solos — sin cable de por medio, como pasa en un protoboard o
/// cuando apoyás la pata de un LED sobre la de otro.
///
/// DECISIÓN IMPORTANTE: conducen los BORNES, no los cuerpos. Dos LEDs que se
/// tocan por el plástico no hacen nada; si se tocan las patas, sí. Es lo
/// realista (uno conecta por los terminales) y además es lo que hace que el
/// sistema sea usable: si condujeran los cuerpos enteros, en una escena 3D
/// tendrías conexiones fantasma cada vez que acomodás algo.
///
/// El Core NO se entera de nada de esto. Para el solver una conexión es un
/// Wire entre dos Pin, y le da igual si lo puso un cable, un contacto o el
/// usuario a mano. Por eso todo este archivo vive en la capa Game y no toca
/// una línea de física.
/// </summary>
public static class ContactoEntreBornes
{
    /// <summary>
    /// Distancia a la que dos bornes se consideran tocándose. Es un poco más
    /// que el radio visible de la esfera (0.045) para que el contacto se
    /// sienta generoso: si están claramente pegados, tienen que conectar.
    /// </summary>
    public static float DistanciaContacto { get; set; } = 0.11f;

    /// <summary>
    /// Histéresis: se separan a esta distancia, no a la de contacto. Sin esto,
    /// un borne temblando justo en el límite conectaría y desconectaría cada
    /// frame — el mismo problema que la histéresis del LED en el solver.
    /// </summary>
    public static float DistanciaSeparacion { get; set; } = 0.15f;

    /// <summary>Se puede apagar: en un tablero apretado a veces molesta.</summary>
    public static bool Habilitado { get; set; } = true;

    /// <summary>
    /// Recalcula todos los contactos. Se llama cuando cambió la geometría
    /// (soltaste un componente, lo moviste, lo rotaste, lo borraste), NO cada
    /// frame: es O(n²) sobre los bornes y no hace falta más que eso.
    /// </summary>
    public static void Recalcular(ControladorConstruccion controlador)
    {
        // 1) Soltar los contactos que ya no se tocan.
        for (int i = controlador.Cables.Count - 1; i >= 0; i--)
        {
            var cable = controlador.Cables[i];
            if (!cable.EsContacto) continue;
            if (!GodotObject.IsInstanceValid(cable)) { controlador.Cables.RemoveAt(i); continue; }

            if (!SiguenPegados(cable.BorneA, cable.BorneB))
            {
                controlador.Cables.RemoveAt(i);
                cable.QuitarSe();
            }
        }

        if (!Habilitado) return;

        // 2) Buscar contactos nuevos.
        var bornes = TodosLosBornes(controlador);
        for (int i = 0; i < bornes.Count; i++)
        {
            for (int j = i + 1; j < bornes.Count; j++)
            {
                var a = bornes[i];
                var b = bornes[j];

                // Dos bornes del MISMO componente no se conectan entre sí: eso
                // sería cortocircuitarlo por adentro, y además el solver lo
                // trataría como un lazo a sí mismo.
                if (a.Dueno != null && ReferenceEquals(a.Dueno, b.Dueno)) continue;

                if (a.GlobalPosition.DistanceTo(b.GlobalPosition) > DistanciaContacto) continue;
                if (YaConectados(controlador, a, b)) continue;

                var contacto = controlador.CrearCableCargado(a, b, fisico: false);
                contacto.MarcarComoContacto();
            }
        }
    }

    private static bool SiguenPegados(BorneVisual a, BorneVisual b)
    {
        if (!GodotObject.IsInstanceValid(a) || !GodotObject.IsInstanceValid(b)) return false;
        return a.GlobalPosition.DistanceTo(b.GlobalPosition) <= DistanciaSeparacion;
    }

    private static bool YaConectados(ControladorConstruccion controlador, BorneVisual a, BorneVisual b)
    {
        foreach (var cable in controlador.Cables)
        {
            if (!GodotObject.IsInstanceValid(cable)) continue;
            bool mismosBornes =
                (ReferenceEquals(cable.BorneA, a) && ReferenceEquals(cable.BorneB, b)) ||
                (ReferenceEquals(cable.BorneA, b) && ReferenceEquals(cable.BorneB, a));
            if (mismosBornes) return true;
        }
        return false;
    }

    private static List<BorneVisual> TodosLosBornes(ControladorConstruccion controlador)
    {
        var lista = new List<BorneVisual>();
        foreach (var visual in controlador.Visuales)
        {
            if (!GodotObject.IsInstanceValid(visual)) continue;
            if (GodotObject.IsInstanceValid(visual.BorneA)) lista.Add(visual.BorneA);
            if (GodotObject.IsInstanceValid(visual.BorneB)) lista.Add(visual.BorneB);
            foreach (var extra in visual.BornesExtra)
            {
                if (GodotObject.IsInstanceValid(extra)) lista.Add(extra);
            }
        }
        return lista;
    }
}
