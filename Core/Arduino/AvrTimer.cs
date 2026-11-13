namespace ElectricSim.Core.Arduino;

/// <summary>
/// Un timer/contador del ATmega328P (sirve para los tres: Timer0 y Timer2 de
/// 8 bits, Timer1 de 16). Es la pieza de hardware de la que dependen
/// millis(), delay(), analogWrite() (PWM) y tone() en el Arduino real, así
/// que emularlo bien es lo que hace que un sketch compilado "sienta" el
/// tiempo correcto.
///
/// CÓMO FUNCIONA (igual que el chip): el reloj de la CPU pasa por un
/// PRESCALER (÷1, ÷8, ÷64...) y cada pulso resultante incrementa el
/// CONTADOR. Según el modo de onda (los bits WGM de TCCRnA/B), el contador
/// da vueltas hasta un TOPE distinto y en el camino dispara eventos:
///
///   - DESBORDE (TOV): millis() de Arduino vive de este evento del Timer0.
///   - COINCIDENCIA con OCRnA/OCRnB (OCF): la base de CTC y de tone().
///   - PWM: en los modos PWM, el pin de salida asociado (OC0A=D6, OC0B=D5,
///     OC1A=D9, OC1B=D10, OC2A=D11, OC2B=D3) queda en manos del timer.
///     Para la capa eléctrica no simulamos el tren de pulsos ciclo a ciclo:
///     exponemos el DUTY (fracción de tiempo en alto), que es exactamente lo
///     que un LED o un motor "ven" como promedio — y lo que enseña la teoría.
///
/// El avance es por lotes: la CPU ejecuta una instrucción (1-4 ciclos) y
/// después le pasa esos ciclos a los timers. Con prescaler 64 el bucle
/// interno casi nunca corre, así que el costo por instrucción es mínimo.
/// </summary>
internal sealed class AvrTimer
{
    private readonly bool _es16Bits;
    private readonly int[] _tablaPrescaler;
    private readonly int _mascara; // 0xFF u 0xFFFF

    /// <summary>Se dispara en el desborde (para setear TOVn en TIFRn).</summary>
    public Action? AlDesbordar;
    /// <summary>Coincidencia con OCRnA / OCRnB (para OCFnA / OCFnB).</summary>
    public Action? AlCoincidirA;
    public Action? AlCoincidirB;

    // Registros del timer (los escribe/lee el chip al rutear las direcciones IO):
    public byte TCCRA;
    public byte TCCRB;
    public int Contador;   // TCNTn
    public int OcrA;
    public int OcrB;
    public int Icr;        // solo Timer1 (tope en algunos modos)

    private int _restoPrescaler;   // ciclos de CPU acumulados que todavía no completaron un pulso
    private bool _bajando;         // en los modos phase-correct el contador sube y baja

    // Prescalers reales: Timer0/1 comparten tabla; Timer2 tiene más opciones.
    private static readonly int[] TablaT0T1 = { 0, 1, 8, 64, 256, 1024, 0, 0 };  // 6 y 7 = reloj externo (no emulado)
    private static readonly int[] TablaT2 = { 0, 1, 8, 32, 64, 128, 256, 1024 };

    public AvrTimer(bool es16Bits, bool esTimer2)
    {
        _es16Bits = es16Bits;
        _mascara = es16Bits ? 0xFFFF : 0xFF;
        _tablaPrescaler = esTimer2 ? TablaT2 : TablaT0T1;
    }

    public void Reset()
    {
        TCCRA = 0; TCCRB = 0;
        Contador = 0; OcrA = 0; OcrB = 0; Icr = 0;
        _restoPrescaler = 0;
        _bajando = false;
    }

    // ------------------------------------------------------------------
    //  Modo de onda (bits WGM repartidos entre TCCRnA y TCCRnB)
    // ------------------------------------------------------------------

    private int ModoWgm => _es16Bits
        ? (TCCRA & 0b11) | ((TCCRB >> 1) & 0b1100)   // WGM13:12 en TCCRB bits 4:3
        : (TCCRA & 0b11) | ((TCCRB >> 1) & 0b100);   // WGM02 en TCCRB bit 3

    private bool EsFastPwm => _es16Bits
        ? ModoWgm is 5 or 6 or 7 or 14 or 15
        : ModoWgm is 3 or 7;

    private bool EsPhaseCorrect => _es16Bits
        ? ModoWgm is 1 or 2 or 3 or 8 or 9 or 10 or 11
        : ModoWgm is 1 or 5;

    private bool EsCtc => _es16Bits ? ModoWgm is 4 or 12 : ModoWgm == 2;

    /// <summary>Hasta dónde cuenta el contador en el modo actual.</summary>
    public int Tope()
    {
        if (_es16Bits)
        {
            return ModoWgm switch
            {
                0 => 0xFFFF,                    // normal
                1 or 5 => 0xFF,                 // PWM 8 bits
                2 or 6 => 0x1FF,                // PWM 9 bits
                3 or 7 => 0x3FF,                // PWM 10 bits
                4 => Math.Max(OcrA, 1),         // CTC por OCR1A
                12 => Math.Max(Icr, 1),         // CTC por ICR1
                8 or 10 or 14 => Math.Max(Icr, 1),
                9 or 11 or 15 => Math.Max(OcrA, 1),
                _ => 0xFFFF,
            };
        }
        return ModoWgm switch
        {
            0 or 1 or 3 => 0xFF,                // normal / phase-correct / fast PWM
            2 => Math.Max(OcrA, 1),             // CTC
            5 or 7 => Math.Max(OcrA, 1),        // PWM con tope OCRA
            _ => 0xFF,
        };
    }

    // ------------------------------------------------------------------
    //  Avance
    // ------------------------------------------------------------------

    /// <summary>
    /// Avanza el timer la cantidad de ciclos de CPU que consumió la última
    /// instrucción. Dispara los eventos de desborde/coincidencia que hayan
    /// ocurrido en el camino.
    /// </summary>
    public void Avanzar(int ciclosCpu)
    {
        int prescaler = _tablaPrescaler[TCCRB & 0b111];
        if (prescaler == 0) return; // timer detenido (o con reloj externo, que no emulamos)

        _restoPrescaler += ciclosCpu;
        while (_restoPrescaler >= prescaler)
        {
            _restoPrescaler -= prescaler;
            Pulso();
        }
    }

    /// <summary>Un incremento (o decremento, en phase-correct) del contador, con sus eventos.</summary>
    private void Pulso()
    {
        int tope = Tope();

        if (EsPhaseCorrect)
        {
            // Sube hasta el tope y baja hasta 0. TOV se dispara ABAJO (así lo
            // hace el chip real, y el núcleo de Arduino cuenta con eso).
            if (_bajando)
            {
                Contador--;
                if (Contador <= 0)
                {
                    Contador = 0;
                    _bajando = false;
                    AlDesbordar?.Invoke();
                }
            }
            else
            {
                Contador++;
                if (Contador >= tope)
                {
                    Contador = tope;
                    _bajando = true;
                }
            }
            if (Contador == OcrA) AlCoincidirA?.Invoke();
            if (Contador == OcrB) AlCoincidirB?.Invoke();
            return;
        }

        // Modos que solo suben: normal, CTC y fast PWM.
        Contador++;

        if (Contador > tope)
        {
            // El contador da la vuelta al PASAR el tope (cuenta 0..tope
            // inclusive, período = tope+1, como el chip). En CTC el "tope" es
            // OCRA/ICR1 y el desborde no emite TOV; en normal y fast PWM, sí.
            Contador = 0;
            if (!EsCtc) AlDesbordar?.Invoke();
        }

        if (Contador == OcrA) AlCoincidirA?.Invoke();
        if (Contador == OcrB) AlCoincidirB?.Invoke();
    }

    // ------------------------------------------------------------------
    //  PWM: el duty que "ve" el mundo eléctrico
    // ------------------------------------------------------------------

    /// <summary>
    /// Fracción de tiempo en ALTO del pin OCnA (0 = siempre bajo, 1 = siempre
    /// alto), o null si el timer NO está manejando ese pin (bits COM apagados)
    /// y el pin sigue en manos del registro PORT normal.
    /// </summary>
    public double? DutyA() => DutyDe((TCCRA >> 6) & 0b11, OcrA);

    /// <summary>Ídem para el pin OCnB.</summary>
    public double? DutyB() => DutyDe((TCCRA >> 4) & 0b11, OcrB);

    private double? DutyDe(int com, int ocr)
    {
        if (com == 0) return null; // el timer no toca el pin

        int tope = Tope();
        double duty;

        if (EsFastPwm)
        {
            // Fast PWM: alto desde 0 hasta la coincidencia → (OCR+1)/(TOPE+1).
            duty = (ocr + 1.0) / (tope + 1.0);
        }
        else if (EsPhaseCorrect)
        {
            duty = tope > 0 ? (double)ocr / tope : 0;
        }
        else
        {
            // Modos no-PWM con COM=1: el pin CONMUTA en cada coincidencia
            // (así genera ondas cuadradas el hardware). El promedio es 50%.
            return 0.5;
        }

        // COM=3 es el modo invertido (bajo durante el pulso, alto el resto).
        if (com == 3) duty = 1.0 - duty;
        return Math.Clamp(duty, 0, 1);
    }
}
