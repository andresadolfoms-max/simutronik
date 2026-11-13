namespace ElectricSim.Core.Arduino;

/// <summary>
/// Lector del formato Intel HEX — el formato de texto en el que el IDE de
/// Arduino exporta los programas compilados ("Exportar binario compilado",
/// o el .hex que queda en la carpeta temporal de build).
///
/// Cada línea es un registro:  :LLAAAATT[DD...]CC
///   LL   = cantidad de bytes de datos
///   AAAA = dirección de destino (16 bits)
///   TT   = tipo de registro (00 datos, 01 fin, 04 dirección extendida...)
///   DD   = los datos
///   CC   = checksum (el complemento a dos de la suma de todo lo anterior)
///
/// El resultado es la imagen de la memoria FLASH: un array de bytes que la
/// CPU virtual va a ejecutar tal cual lo haría el chip real.
/// </summary>
public static class IntelHexParser
{
    /// <summary>
    /// Interpreta el contenido completo de un archivo .hex y lo vuelca en una
    /// imagen de flash del tamaño pedido (32KB para el ATmega328P).
    /// Tira FormatException con un mensaje claro si el archivo está dañado,
    /// no es Intel HEX, o el programa no entra en la flash.
    /// </summary>
    public static byte[] Cargar(string contenido, int tamanoFlash, out int bytesDePrograma)
    {
        if (string.IsNullOrWhiteSpace(contenido))
        {
            throw new FormatException("El archivo está vacío.");
        }

        var flash = new byte[tamanoFlash];
        // La flash borrada de un AVR real queda toda en 0xFF (y 0xFFFF es
        // una instrucción inofensiva para nuestra CPU si algo salta ahí).
        Array.Fill(flash, (byte)0xFF);

        int desplazamientoExtendido = 0; // registros tipo 02/04 (programas > 64KB; el Uno no los usa, pero los respetamos)
        bool huboDatos = false;
        bool huboFin = false;
        int numeroLinea = 0;
        int bytesCargados = 0;

        foreach (var lineaCruda in contenido.Split('\n'))
        {
            numeroLinea++;
            var linea = lineaCruda.Trim();
            if (linea.Length == 0) continue;
            if (linea[0] != ':')
            {
                throw new FormatException($"Línea {numeroLinea}: no empieza con ':' — esto no parece un archivo Intel HEX.");
            }
            if (linea.Length < 11 || (linea.Length - 1) % 2 != 0)
            {
                throw new FormatException($"Línea {numeroLinea}: largo inválido para un registro Intel HEX.");
            }

            // De texto hexa a bytes.
            var bytes = new byte[(linea.Length - 1) / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(linea.AsSpan(1 + i * 2, 2),
                        System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                {
                    throw new FormatException($"Línea {numeroLinea}: caracteres que no son hexadecimales.");
                }
            }

            int cantidad = bytes[0];
            int direccion = (bytes[1] << 8) | bytes[2];
            int tipo = bytes[3];
            if (bytes.Length != cantidad + 5)
            {
                throw new FormatException($"Línea {numeroLinea}: la cantidad de datos declarada ({cantidad}) no coincide con el largo de la línea.");
            }

            // Checksum: la suma de TODOS los bytes (incluido el checksum) debe dar 0 módulo 256.
            int suma = 0;
            foreach (var b in bytes) suma += b;
            if ((suma & 0xFF) != 0)
            {
                throw new FormatException($"Línea {numeroLinea}: checksum incorrecto — el archivo está dañado o cortado.");
            }

            switch (tipo)
            {
                case 0x00: // datos
                    for (int i = 0; i < cantidad; i++)
                    {
                        int destino = desplazamientoExtendido + direccion + i;
                        if (destino >= tamanoFlash)
                        {
                            throw new FormatException(
                                $"Línea {numeroLinea}: el programa escribe en la dirección 0x{destino:X} y la flash del ATmega328P termina en 0x{tamanoFlash - 1:X}. " +
                                "¿Seguro que el .hex fue compilado para Arduino Uno?");
                        }
                        flash[destino] = bytes[4 + i];
                        bytesCargados++;
                    }
                    huboDatos = true;
                    break;

                case 0x01: // fin de archivo
                    huboFin = true;
                    break;

                case 0x02: // dirección extendida de segmento (paragraph × 16)
                    desplazamientoExtendido = ((bytes[4] << 8) | bytes[5]) << 4;
                    break;

                case 0x04: // dirección lineal extendida (16 bits altos)
                    desplazamientoExtendido = ((bytes[4] << 8) | bytes[5]) << 16;
                    break;

                case 0x03: // CS:IP inicial — no aplica en AVR, se ignora
                case 0x05: // EIP inicial — ídem
                    break;

                default:
                    throw new FormatException($"Línea {numeroLinea}: tipo de registro desconocido ({tipo:X2}).");
            }

            if (huboFin) break;
        }

        if (!huboDatos)
        {
            throw new FormatException("El archivo no contiene datos de programa.");
        }

        bytesDePrograma = bytesCargados; // informativo: el inspector muestra "Programa: 1.412 bytes"
        return flash;
    }
}
