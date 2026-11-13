using Godot;
using ElectricSim.Core.Components;

namespace ElectricSim.Game;

/// <summary>
/// El selector de archivos .hex para programar una placa Arduino. Es UN solo
/// FileDialog nativo del sistema (Godot 4.2+), compartido por todas las
/// placas del mundo: se abre apuntando a la placa que lo pidió, lee el
/// archivo elegido y se lo da a CargarPrograma. Si el archivo no es un
/// Intel HEX válido (o no se puede leer), muestra el error en criollo en un
/// AcceptDialog en vez de reventar.
///
/// Se llega acá por dos caminos: click directo sobre una placa virgen, o el
/// botón "Cargar programa" del inspector.
/// </summary>
public static class DialogoCargarHex
{
    private static FileDialog? _dialogo;
    private static AcceptDialog? _cartelError;
    private static ArduinoUno? _placaActual;

    /// <summary>Abre el selector para programar (o reprogramar) la placa dada.</summary>
    public static void Abrir(ArduinoUno placa, Node contexto)
    {
        _placaActual = placa;
        var raiz = contexto.GetTree().Root;

        if (_dialogo == null || !GodotObject.IsInstanceValid(_dialogo))
        {
            _dialogo = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,   // el diálogo del sistema operativo: navegás tus carpetas reales
                Filters = new[] { "*.hex ; Programa compilado (Intel HEX)" },
            };
            _dialogo.FileSelected += CargarArchivo;
            raiz.AddChild(_dialogo);
        }

        _dialogo.Title = $"Cargar programa en {placa.Name}";
        _dialogo.PopupCentered(new Vector2I(900, 600));
    }

    private static void CargarArchivo(string ruta)
    {
        if (_placaActual == null) return;
        try
        {
            string texto = System.IO.File.ReadAllText(ruta);
            _placaActual.CargarPrograma(texto, System.IO.Path.GetFileName(ruta));
        }
        catch (FormatException e)
        {
            MostrarError($"El archivo seleccionado no es un Intel HEX válido.\n\n{e.Message}\n\n" +
                         "Debe ser el archivo .hex generado por el IDE de Arduino\n" +
                         "(Programa → Exportar binario compilado) o por avr-gcc.");
        }
        catch (Exception e)
        {
            MostrarError($"No se pudo leer el archivo:\n{e.Message}");
        }
    }

    private static void MostrarError(string mensaje)
    {
        if (_dialogo == null) return;
        if (_cartelError == null || !GodotObject.IsInstanceValid(_cartelError))
        {
            _cartelError = new AcceptDialog { Title = "No se pudo cargar el programa" };
            _dialogo.GetTree().Root.AddChild(_cartelError);
        }
        _cartelError.DialogText = mensaje;
        _cartelError.PopupCentered();
    }
}
