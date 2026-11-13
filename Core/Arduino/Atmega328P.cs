namespace ElectricSim.Core.Arduino;

/// <summary>Qué está haciendo un pin del chip, visto desde afuera (lo consume la placa para armar su modelo eléctrico).</summary>
public struct EstadoPinChip
{
    /// <summary>True si el pin está configurado como SALIDA (bit DDR en 1).</summary>
    public bool EsSalida;

    /// <summary>
    /// Fracción de tiempo en ALTO durante el último lote de ejecución
    /// (0 = siempre bajo, 1 = siempre alto, 0.5 = PWM al 50%...). Es lo que
    /// el circuito "ve" como promedio: la forma honesta de acoplar una CPU a
    /// 16MHz con un solver eléctrico que corre a 10-120 ticks por segundo.
    /// </summary>
    public double Duty;

    /// <summary>True si es entrada CON la resistencia de pull-up interna activada (pinMode INPUT_PULLUP).</summary>
    public bool PullupActivo;
}

/// <summary>
/// El microcontrolador ATmega328P completo: la CPU AVR (que vive en
/// Atmega328P.Nucleo.cs, la otra mitad de esta clase parcial), la memoria
/// (32KB de flash, 2KB de SRAM, los 32 registros), y los periféricos que un
/// sketch de Arduino usa de verdad:
///
///   - Puertos B/C/D (pinMode / digitalWrite / digitalRead)
///   - Timer0/1/2 con PWM e interrupciones (millis, delay, analogWrite, tone)
///   - ADC de 10 bits (analogRead), con el voltaje REAL del circuito
///   - USART (Serial.print / Serial.read), expuesto como monitor serie
///   - EEPROM de 1KB, interrupciones externas INT0/INT1 y de cambio de pin
///
/// Esta clase NO sabe nada de electricidad: solo dice "el pin D13 es salida
/// y está en alto el 100% del tiempo". Quien traduce eso a voltajes y
/// corrientes es el componente ArduinoUno (Core/Components/ArduinoUno.cs),
/// que vive dentro del CircuitGraph como cualquier otro componente.
/// </summary>
public sealed partial class Atmega328P
{
    public const int TamanoFlash = 32 * 1024;
    public const int TamanoSram = 2048;
    public const int CantidadPines = 20; // D0-D13 + A0-A5

    // =====================================================================
    //  Memoria
    // =====================================================================

    // La flash como bytes (para LPM) y como palabras de 16 bits (para el fetch).
    private byte[] _flash = new byte[TamanoFlash];
    private readonly ushort[] _flashPalabras = new ushort[TamanoFlash / 2];

    // El espacio de datos completo: registros (0x00-0x1F), E/S (0x20-0xFF)
    // y SRAM (0x100-0x8FF), igual que el mapa real del chip.
    private readonly byte[] _datos = new byte[0x0900];

    private readonly byte[] _eeprom = new byte[1024]; // sobrevive al Reset, como la de verdad

    // =====================================================================
    //  Estado de ejecución
    // =====================================================================

    /// <summary>Ciclos de reloj ejecutados desde el Reset (a 16MHz, 16 millones por segundo simulado).</summary>
    public long CiclosTotales { get; private set; }

    public bool ProgramaCargado { get; private set; }

    /// <summary>Texto informativo del programa cargado ("blink.hex · 924 bytes").</summary>
    public string InfoPrograma { get; private set; } = "";

    /// <summary>True si la CPU se frenó sola (instrucción BREAK o error interno).</summary>
    public bool Detenida { get; private set; }

    public string MotivoDetencion { get; private set; } = "";

    /// <summary>Frena la CPU desde afuera (lo usa la placa cuando se quema: un chip cocinado no ejecuta más nada).</summary>
    public void DetenerPorHardware(string motivo) => Detener(motivo);

    /// <summary>Revive el hardware al reparar la placa: des-frena la CPU (resume donde estaba) y borra los pines dañados. El programa y la EEPROM quedan como estaban.</summary>
    public void RepararHardware()
    {
        Detenida = false;
        MotivoDetencion = "";
        Array.Clear(PinDanado, 0, PinDanado.Length);
    }

    /// <summary>Opcodes que la CPU no reconoció (se ejecutan como NOP). Si esto no es 0, algo raro pasa.</summary>
    public int InstruccionesDesconocidas { get; private set; }

    // =====================================================================
    //  Periféricos
    // =====================================================================

    private readonly AvrTimer _timer0 = new(es16Bits: false, esTimer2: false);
    private readonly AvrTimer _timer1 = new(es16Bits: true, esTimer2: false);
    private readonly AvrTimer _timer2 = new(es16Bits: false, esTimer2: true);

    // --- USART ---
    /// <summary>Se dispara por cada byte que el programa manda por Serial (Serial.print y compañía).</summary>
    public event Action<byte>? SerialTransmitido;
    private readonly Queue<byte> _serialRx = new();
    private bool _flagTxCompleta;

    /// <summary>
    /// True = la transmisión sale al toque (modo simple, cómodo para los
    /// tests del emulador pelado). False = se respeta el BAUDRATE real: cada
    /// byte tarda sus ~10 bits, el registro queda OCUPADO mientras tanto
    /// (UDRE en 0) y el sketch se pausa como en la placa de verdad. La placa
    /// Arduino lo pone en false para que el monitor serie corra a velocidad real.
    /// </summary>
    public bool TransmisionInstantanea { get; set; } = true;

    // Transmisión en curso (solo cuando TransmisionInstantanea es false):
    private int _ciclosTxRestantes;   // ciclos que faltan para que el byte termine de salir
    private byte _byteTxEnCurso;      // el byte que se está transmitiendo ahora mismo

    // --- ADC ---
    private int _adcCiclosRestantes; // > 0 mientras hay una conversión en curso
    private bool _flagAdc;           // ADIF

    // --- Entradas: lo que la placa midió en los bornes ---
    private readonly double[] _voltajesEntrada = new double[CantidadPines];
    private readonly bool[] _nivelEntrada = new bool[CantidadPines]; // con histéresis (1.5V / 3.0V, como el chip)

    /// <summary>Temperatura del chip (°C) — la placa la copia de su propia temperatura; la lee el canal 8 del ADC (sensor interno).</summary>
    public double TemperaturaChip { get; set; } = 25;

    /// <summary>
    /// Voltaje presente en el pin AREF (V). Solo se usa cuando el programa
    /// elige la referencia EXTERNA del ADC (bits REFS = 00, analogReference(EXTERNAL)).
    /// La placa lo actualiza con el voltaje medido en su borne AREF.
    /// </summary>
    public double VoltajeAref { get; set; } = 5.0;

    /// <summary>
    /// Voltaje de AVcc (V): la referencia por defecto del ADC (REFS = 01,
    /// analogReference(DEFAULT)). La placa lo actualiza con el voltaje real de
    /// su riel de 5V, así la conversión es proporcional a la alimentación como en el chip real.
    /// </summary>
    public double VoltajeAvcc { get; set; } = 5.0;

    /// <summary>Bits REFS1:0 del registro ADMUX: 0 = AREF externo, 1 = AVcc, 3 = interna de 1.1V.</summary>
    public int ReferenciaAdc => (_datos[DIR_ADMUX] >> 6) & 0b11;

    // --- Salidas: duty por pin durante el último lote ---
    private readonly bool[] _nivelSalida = new bool[CantidadPines];    // nivel digital actual (bit PORT o PWM)
    private readonly long[] _altoDesde = new long[CantidadPines];      // ciclo en que empezó el tramo en alto actual
    private readonly double[] _acumAlto = new double[CantidadPines];   // ciclos en alto acumulados en el lote
    private long _inicioLote;

    /// <summary>El estado de cada pin (D0..D13, A0..A5) tal como quedó al cerrar el último lote. Lo lee la placa.</summary>
    public EstadoPinChip[] Pines { get; } = new EstadoPinChip[CantidadPines];

    /// <summary>Pines físicamente dañados (los marca la placa cuando detecta sobrecorriente). Un pin dañado queda flotando para siempre.</summary>
    public bool[] PinDanado { get; } = new bool[CantidadPines];

    // Interrupciones: bandera "vale la pena escanear" para no recorrer la
    // lista de fuentes en cada instrucción.
    private bool _revisarInterrupciones;
    private int _instruccionesDeGracia; // tras RETI/SEI se ejecuta 1 instrucción antes de atender otra (como el chip real)

    public Atmega328P()
    {
        _timer0.AlDesbordar = () => MarcarFlagTimer(DIR_TIFR0, 1 << 0);
        _timer0.AlCoincidirA = () => MarcarFlagTimer(DIR_TIFR0, 1 << 1);
        _timer0.AlCoincidirB = () => MarcarFlagTimer(DIR_TIFR0, 1 << 2);
        _timer1.AlDesbordar = () => MarcarFlagTimer(DIR_TIFR1, 1 << 0);
        _timer1.AlCoincidirA = () => MarcarFlagTimer(DIR_TIFR1, 1 << 1);
        _timer1.AlCoincidirB = () => MarcarFlagTimer(DIR_TIFR1, 1 << 2);
        _timer2.AlDesbordar = () => MarcarFlagTimer(DIR_TIFR2, 1 << 0);
        _timer2.AlCoincidirA = () => MarcarFlagTimer(DIR_TIFR2, 1 << 1);
        _timer2.AlCoincidirB = () => MarcarFlagTimer(DIR_TIFR2, 1 << 2);
        Reset();
    }

    // =====================================================================
    //  Carga de programa y reset
    // =====================================================================

    /// <summary>Carga un .hex exportado del IDE de Arduino y resetea el chip, listo para ejecutar.</summary>
    public void CargarHex(string contenidoHex, string nombre = "programa")
    {
        _flash = IntelHexParser.Cargar(contenidoHex, TamanoFlash, out int bytes);
        for (int i = 0; i < _flashPalabras.Length; i++)
        {
            _flashPalabras[i] = (ushort)(_flash[i * 2] | (_flash[i * 2 + 1] << 8));
        }
        ProgramaCargado = true;
        InfoPrograma = $"{nombre} · {bytes:N0} bytes";
        Reset();
    }

    /// <summary>Vuelve el chip al estado de recién encendido (como apretar el botón RESET de la placa). La EEPROM y los pines dañados persisten.</summary>
    public void Reset()
    {
        Array.Clear(_datos);
        _pc = 0;
        _sreg = 0;
        SP = 0x08FF; // el ATmega328P arranca con el stack en el final de la SRAM
        _datos[DIR_MCUSR] = 0b0001; // PORF: "vengo de un power-on"

        _timer0.Reset();
        _timer1.Reset();
        _timer2.Reset();

        _serialRx.Clear();
        _flagTxCompleta = false;
        _ciclosTxRestantes = 0;
        _adcCiclosRestantes = 0;
        _flagAdc = false;

        CiclosTotales = 0;
        _inicioLote = 0;
        Array.Clear(_acumAlto);
        Array.Clear(_altoDesde);
        Array.Clear(_nivelSalida);
        RefrescarPines();

        Detenida = false;
        MotivoDetencion = "";
        InstruccionesDesconocidas = 0;
        _revisarInterrupciones = false;
        _instruccionesDeGracia = 0;
    }

    // =====================================================================
    //  Bucle de ejecución (lo llama la placa una vez por frame)
    // =====================================================================

    /// <summary>
    /// Ejecuta HASTA la cantidad de ciclos pedida (una instrucción AVR toma
    /// 1 a 4). Devuelve cuántos ciclos consumió de verdad. Al final del lote
    /// cierra la contabilidad de duty de cada pin, para que la placa traslade
    /// el resultado al circuito.
    /// </summary>
    public long Ejecutar(long ciclos)
    {
        if (!ProgramaCargado || Detenida || ciclos <= 0)
        {
            CerrarLote();
            return 0;
        }

        long objetivo = CiclosTotales + ciclos;
        while (CiclosTotales < objetivo && !Detenida)
        {
            // ¿Hay una interrupción esperando? (con la "gracia" de 1
            // instrucción después de RETI/SEI, como el silicio real)
            if (_instruccionesDeGracia > 0)
            {
                _instruccionesDeGracia--;
            }
            else if ((_sreg & FLAG_I) != 0 && _revisarInterrupciones)
            {
                int vector = BuscarInterrupcion();
                if (vector > 0)
                {
                    AtenderInterrupcion(vector);
                    continue;
                }
                _revisarInterrupciones = false; // no había nada: dejamos de escanear hasta la próxima novedad
            }

            int consumidos = EjecutarInstruccion(); // vive en Atmega328P.Nucleo.cs
            CiclosTotales += consumidos;
            AvanzarPerifericos(consumidos);
        }

        CerrarLote();
        return CiclosTotales - (objetivo - ciclos);
    }

    private void AvanzarPerifericos(int ciclos)
    {
        _timer0.Avanzar(ciclos);
        _timer1.Avanzar(ciclos);
        _timer2.Avanzar(ciclos);

        if (_adcCiclosRestantes > 0)
        {
            _adcCiclosRestantes -= ciclos;
            if (_adcCiclosRestantes <= 0)
            {
                CompletarConversionAdc();
            }
        }

        // Transmisión serie realista: cuando el byte termina de salir, recién
        // ahí "aparece" en el monitor, TXC se levanta y UDRE vuelve a 1.
        if (_ciclosTxRestantes > 0)
        {
            _ciclosTxRestantes -= ciclos;
            if (_ciclosTxRestantes <= 0)
            {
                _ciclosTxRestantes = 0;
                _flagTxCompleta = true;
                _revisarInterrupciones = true;
                SerialTransmitido?.Invoke(_byteTxEnCurso);
            }
        }
    }

    // =====================================================================
    //  Interrupciones
    // =====================================================================

    private const byte FLAG_I = 0x80;

    private void MarcarFlagTimer(int direccion, int bit)
    {
        _datos[direccion] |= (byte)bit;
        _revisarInterrupciones = true;
    }

    /// <summary>
    /// Busca la interrupción pendiente de MAYOR prioridad (menor número de
    /// vector, igual que el chip). Devuelve el número de vector, o 0 si no
    /// hay ninguna. El orden y las condiciones son las de la tabla del
    /// datasheet del ATmega328P.
    /// </summary>
    private int BuscarInterrupcion()
    {
        byte eimsk = _datos[DIR_EIMSK];
        if ((eimsk & 0b01) != 0 && Int0Pendiente(0)) return 1;   // INT0  (pin D2)
        if ((eimsk & 0b10) != 0 && Int0Pendiente(1)) return 2;   // INT1  (pin D3)

        byte pcicr = _datos[DIR_PCICR];
        byte pcifr = _datos[DIR_PCIFR];
        if ((pcicr & pcifr & 0b001) != 0) return 3;              // PCINT0 (puerto B)
        if ((pcicr & pcifr & 0b010) != 0) return 4;              // PCINT1 (puerto C)
        if ((pcicr & pcifr & 0b100) != 0) return 5;              // PCINT2 (puerto D)

        byte t2 = (byte)(_datos[DIR_TIFR2] & _datos[DIR_TIMSK2]);
        if ((t2 & 0b010) != 0) return 7;                          // TIMER2 COMPA
        if ((t2 & 0b100) != 0) return 8;                          // TIMER2 COMPB
        if ((t2 & 0b001) != 0) return 9;                          // TIMER2 OVF

        byte t1 = (byte)(_datos[DIR_TIFR1] & _datos[DIR_TIMSK1]);
        if ((t1 & 0b100000) != 0) return 10;                      // TIMER1 CAPT
        if ((t1 & 0b000010) != 0) return 11;                      // TIMER1 COMPA
        if ((t1 & 0b000100) != 0) return 12;                      // TIMER1 COMPB
        if ((t1 & 0b000001) != 0) return 13;                      // TIMER1 OVF

        byte t0 = (byte)(_datos[DIR_TIFR0] & _datos[DIR_TIMSK0]);
        if ((t0 & 0b010) != 0) return 14;                         // TIMER0 COMPA
        if ((t0 & 0b100) != 0) return 15;                         // TIMER0 COMPB
        if ((t0 & 0b001) != 0) return 16;                         // TIMER0 OVF (¡el corazón de millis()!)

        byte ucsr0b = _datos[DIR_UCSR0B];
        if ((ucsr0b & 0x80) != 0 && _serialRx.Count > 0) return 18; // USART RX completa
        if ((ucsr0b & 0x20) != 0 && (TransmisionInstantanea || _ciclosTxRestantes <= 0))
            return 19;                                               // USART Data Register Empty (solo cuando de verdad está vacío)
        if ((ucsr0b & 0x40) != 0 && _flagTxCompleta) return 20;     // USART TX completa

        if (_flagAdc && (_datos[DIR_ADCSRA] & 0b1000) != 0) return 21; // ADC lista

        return 0;
    }

    /// <summary>¿INT0/INT1 está pidiendo atención, según su modo (nivel bajo / cualquier flanco / bajada / subida)?</summary>
    private bool Int0Pendiente(int cual)
    {
        int modo = (_datos[DIR_EICRA] >> (cual * 2)) & 0b11;
        if (modo == 0)
        {
            // Modo 0: por NIVEL BAJO — pendiente mientras el pin esté en bajo.
            int pin = cual == 0 ? 2 : 3; // INT0 = D2, INT1 = D3
            return !NivelDe(pin);
        }
        return (_datos[DIR_EIFR] & (1 << cual)) != 0; // modos por flanco: la bandera la puso FijarEntradas
    }

    private void AtenderInterrupcion(int vector)
    {
        // Entrar a una interrupción es: guardar el PC en el stack, apagar la
        // bandera I (no se anidan solas) y saltar al vector. El vector tiene
        // un JMP a la rutina real, que la CPU ejecuta como cualquier salto.
        Push16((ushort)_pc);
        _sreg &= unchecked((byte)~FLAG_I);
        _pc = vector * 2; // cada vector ocupa 2 palabras en el ATmega328P

        CiclosTotales += 5;
        AvanzarPerifericos(5);

        // La bandera que originó la interrupción se limpia sola al entrar
        // (así funciona el hardware; RXC/UDRE son la excepción: se limpian
        // leyendo/escribiendo UDR0, cosa que la rutina hace por su cuenta).
        switch (vector)
        {
            case 1: _datos[DIR_EIFR] &= 0b1111_1110; break;
            case 2: _datos[DIR_EIFR] &= 0b1111_1101; break;
            case 3: _datos[DIR_PCIFR] &= 0b1111_1110; break;
            case 4: _datos[DIR_PCIFR] &= 0b1111_1101; break;
            case 5: _datos[DIR_PCIFR] &= 0b1111_1011; break;
            case 7: _datos[DIR_TIFR2] &= 0b1111_1101; break;
            case 8: _datos[DIR_TIFR2] &= 0b1111_1011; break;
            case 9: _datos[DIR_TIFR2] &= 0b1111_1110; break;
            case 10: _datos[DIR_TIFR1] &= 0b1101_1111; break;
            case 11: _datos[DIR_TIFR1] &= 0b1111_1101; break;
            case 12: _datos[DIR_TIFR1] &= 0b1111_1011; break;
            case 13: _datos[DIR_TIFR1] &= 0b1111_1110; break;
            case 14: _datos[DIR_TIFR0] &= 0b1111_1101; break;
            case 15: _datos[DIR_TIFR0] &= 0b1111_1011; break;
            case 16: _datos[DIR_TIFR0] &= 0b1111_1110; break;
            case 20: _flagTxCompleta = false; break;
            case 21: _flagAdc = false; break;
        }

        // Quedan cosas pendientes casi seguro (otra fuente, o la misma):
        // volveremos a escanear después de la próxima instrucción.
        _revisarInterrupciones = true;
        _instruccionesDeGracia = 1;
    }

    // =====================================================================
    //  Entradas desde el circuito (las trae la placa, una vez por frame)
    // =====================================================================

    /// <summary>
    /// La placa midió el voltaje real de cada borne (con el circuito ya
    /// resuelto) y nos lo trae. Acá se convierte a niveles digitales con la
    /// histéresis del chip real (sube en 3.0V, baja en 1.5V) y se disparan
    /// las interrupciones externas (INT0/INT1) y de cambio de pin (PCINT)
    /// que correspondan.
    /// </summary>
    public void FijarEntradas(ReadOnlySpan<double> voltajes)
    {
        for (int i = 0; i < CantidadPines && i < voltajes.Length; i++)
        {
            double v = voltajes[i];
            if (double.IsNaN(v)) v = 0;
            _voltajesEntrada[i] = v;

            bool nivelAnterior = _nivelEntrada[i];
            bool nivelNuevo = nivelAnterior ? v > 1.5 : v > 3.0;
            _nivelEntrada[i] = nivelNuevo;

            if (nivelNuevo == nivelAnterior) continue;

            // --- Interrupciones externas INT0 (D2) / INT1 (D3), por flanco ---
            if (i == 2 || i == 3)
            {
                int cual = i - 2;
                int modo = (_datos[DIR_EICRA] >> (cual * 2)) & 0b11;
                bool dispara = modo switch
                {
                    1 => true,                    // cualquier cambio
                    2 => !nivelNuevo,             // flanco de bajada
                    3 => nivelNuevo,              // flanco de subida
                    _ => false,                   // nivel bajo: se evalúa en vivo, sin bandera
                };
                if (dispara)
                {
                    _datos[DIR_EIFR] |= (byte)(1 << cual);
                    _revisarInterrupciones = true;
                }
            }

            // --- Interrupciones por cambio de pin (PCINT), por grupo/puerto ---
            var (puerto, bit) = PuertoDePin(i);
            byte mascaraGrupo = puerto switch
            {
                'B' => _datos[DIR_PCMSK0],
                'C' => _datos[DIR_PCMSK1],
                _ => _datos[DIR_PCMSK2],
            };
            if ((mascaraGrupo & (1 << bit)) != 0)
            {
                int bitGrupo = puerto switch { 'B' => 0b001, 'C' => 0b010, _ => 0b100 };
                _datos[DIR_PCIFR] |= (byte)bitGrupo;
                _revisarInterrupciones = true;
            }
        }

        // Un INT en modo "nivel bajo" puede haberse vuelto pendiente sin flanco:
        _revisarInterrupciones = true;
    }

    /// <summary>Nivel digital que el chip "ve" en un pin: su propia salida si es salida, o la entrada muestreada.</summary>
    private bool NivelDe(int indicePin)
    {
        var (puerto, bit) = PuertoDePin(indicePin);
        return (LeerDdr(puerto) & (1 << bit)) != 0 ? _nivelSalida[indicePin] : _nivelEntrada[indicePin];
    }

    // =====================================================================
    //  Salidas hacia el circuito: el duty por pin del último lote
    // =====================================================================

    /// <summary>Registra un cambio de nivel de salida de un pin, cerrando el tramo anterior en la contabilidad de duty.</summary>
    private void CambiarNivelSalida(int indicePin, bool nuevoNivel)
    {
        if (_nivelSalida[indicePin] == nuevoNivel) return;
        if (_nivelSalida[indicePin])
        {
            _acumAlto[indicePin] += CiclosTotales - _altoDesde[indicePin];
        }
        else
        {
            _altoDesde[indicePin] = CiclosTotales;
        }
        _nivelSalida[indicePin] = nuevoNivel;
    }

    /// <summary>
    /// Cierra el lote de ejecución: calcula el duty real de cada pin (cuánto
    /// tiempo estuvo en alto sobre el total del lote) y deja el resultado en
    /// Pines[] para que la placa lo traduzca a su modelo eléctrico. El duty
    /// por hardware (PWM de los timers) pisa al contable cuando está activo.
    /// </summary>
    private void CerrarLote()
    {
        long largoLote = CiclosTotales - _inicioLote;

        for (int i = 0; i < CantidadPines; i++)
        {
            // Cerrar el tramo en curso.
            if (_nivelSalida[i])
            {
                _acumAlto[i] += CiclosTotales - _altoDesde[i];
                _altoDesde[i] = CiclosTotales;
            }
            // Si en este lote no corrió ni un ciclo (CPU en pausa o frenada),
            // el pin conserva su nivel congelado: nada de caer a 0.
            double dutyContable = largoLote > 0
                ? Math.Clamp(_acumAlto[i] / (double)largoLote, 0, 1)
                : (_nivelSalida[i] ? 1 : 0);
            _acumAlto[i] = 0;

            var (puerto, bit) = PuertoDePin(i);
            bool esSalida = (LeerDdr(puerto) & (1 << bit)) != 0;
            bool pullup = !esSalida && (LeerPort(puerto) & (1 << bit)) != 0;

            double? dutyPwm = DutyHardwareDe(i);

            Pines[i] = new EstadoPinChip
            {
                EsSalida = esSalida && !PinDanado[i],
                Duty = PinDanado[i] ? 0 : (dutyPwm ?? dutyContable),
                PullupActivo = pullup && !PinDanado[i],
            };
        }

        _inicioLote = CiclosTotales;
    }

    /// <summary>Si un timer está manejando este pin por hardware (analogWrite), su duty manda. Mapa real: D6/D5→Timer0, D9/D10→Timer1, D11/D3→Timer2.</summary>
    private double? DutyHardwareDe(int indicePin) => indicePin switch
    {
        6 => _timer0.DutyA(),
        5 => _timer0.DutyB(),
        9 => _timer1.DutyA(),
        10 => _timer1.DutyB(),
        11 => _timer2.DutyA(),
        3 => _timer2.DutyB(),
        _ => null,
    };

    /// <summary>Recalcula los niveles de salida de un puerto después de escribir PORT o DDR (con su contabilidad de duty).</summary>
    private void RefrescarPuerto(char puerto)
    {
        byte port = LeerPort(puerto);
        for (int i = 0; i < CantidadPines; i++)
        {
            var (p, bit) = PuertoDePin(i);
            if (p != puerto) continue;
            CambiarNivelSalida(i, (port & (1 << bit)) != 0);
        }
    }

    private void RefrescarPines()
    {
        RefrescarPuerto('B');
        RefrescarPuerto('C');
        RefrescarPuerto('D');
        CerrarLote();
    }

    // =====================================================================
    //  Mapa de pines Arduino → puerto/bit del chip (el cableado real del Uno)
    // =====================================================================

    /// <summary>D0-D7 = PD0-PD7 · D8-D13 = PB0-PB5 · A0-A5 = PC0-PC5.</summary>
    public static (char puerto, int bit) PuertoDePin(int indicePin) => indicePin switch
    {
        < 8 => ('D', indicePin),
        < 14 => ('B', indicePin - 8),
        _ => ('C', indicePin - 14),
    };

    private byte LeerDdr(char puerto) => puerto switch
    {
        'B' => _datos[DIR_DDRB],
        'C' => _datos[DIR_DDRC],
        _ => _datos[DIR_DDRD],
    };

    private byte LeerPort(char puerto) => puerto switch
    {
        'B' => _datos[DIR_PORTB],
        'C' => _datos[DIR_PORTC],
        _ => _datos[DIR_PORTD],
    };

    /// <summary>Compone el registro PINx que el programa lee con digitalRead: salidas devuelven lo que están sacando, entradas lo muestreado del circuito.</summary>
    private byte ComponerPin(char puerto)
    {
        byte resultado = 0;
        for (int i = 0; i < CantidadPines; i++)
        {
            var (p, bit) = PuertoDePin(i);
            if (p != puerto) continue;
            if (NivelDe(i)) resultado |= (byte)(1 << bit);
        }
        return resultado;
    }

    // =====================================================================
    //  USART (el Serial de Arduino)
    // =====================================================================

    /// <summary>Inyecta texto como si el usuario lo tipeara en el Monitor Serie (dispara la interrupción RX si el sketch la usa).</summary>
    public void SerialRecibir(string texto)
    {
        foreach (char c in texto)
        {
            _serialRx.Enqueue((byte)c);
        }
        _revisarInterrupciones = true;
    }

    private void TransmitirSerial(byte b)
    {
        if (TransmisionInstantanea)
        {
            // Modo simple: el registro queda libre al toque y el byte "sale" ya.
            _flagTxCompleta = true;
            _revisarInterrupciones = true;
            SerialTransmitido?.Invoke(b);
            return;
        }

        // Modo realista: si el registro TODAVÍA está ocupado con el byte
        // anterior, el chip real DESCARTA esta escritura (por eso el sketch
        // consulta UDRE antes de escribir). Si está libre, arranca a mandar
        // este byte: tarda sus 10 bits a la velocidad del UART, y hasta que no
        // termine, UDRE queda en 0 y no entra otro.
        if (_ciclosTxRestantes > 0) return;
        _byteTxEnCurso = b;
        _flagTxCompleta = false;               // TXC baja: hay una transmisión en marcha
        _ciclosTxRestantes = CiclosPorByteSerie();
    }

    /// <summary>
    /// Cuántos ciclos de CPU tarda en salir un byte (10 bits: arranque + 8
    /// datos + parada) a la velocidad configurada en UBRR0. baud = F_CPU /
    /// (16·(UBRR+1)) en modo normal; con U2X0, /8. A 9600 baudios y 16MHz da
    /// ~16.640 ciclos por byte, igual que en el chip real.
    /// </summary>
    private int CiclosPorByteSerie()
    {
        int ubrr = _datos[DIR_UBRR0L] | (_datos[DIR_UBRR0H] << 8);
        int divisor = (_datos[DIR_UCSR0A] & 0x02) != 0 ? 8 : 16; // U2X0 duplica la velocidad
        return Math.Max(1, divisor * (ubrr + 1) * 10);
    }

    // =====================================================================
    //  ADC (analogRead)
    // =====================================================================

    private void IniciarConversionAdc()
    {
        // 13 ciclos de ADC por conversión; el reloj del ADC es el de la CPU
        // dividido por el prescaler de los bits ADPS (Arduino usa 128 →
        // una conversión tarda ~104µs a 16MHz, como en la placa real).
        int prescaler = 1 << Math.Max(1, _datos[DIR_ADCSRA] & 0b111);
        _adcCiclosRestantes = 13 * prescaler;
    }

    private void CompletarConversionAdc()
    {
        _adcCiclosRestantes = 0;

        int canal = _datos[DIR_ADMUX] & 0x0F;
        double vref = ReferenciaAdc switch
        {
            0 => Math.Max(VoltajeAref, 0.05),   // EXTERNAL: el voltaje que haya en el pin AREF
            3 => 1.1,                           // INTERNAL: referencia interna de 1.1V
            _ => Math.Max(VoltajeAvcc, 0.05),   // DEFAULT: AVcc (el riel de 5V de la placa)
        };

        double voltaje = canal switch
        {
            <= 5 => _voltajesEntrada[14 + canal],                        // A0..A5: el voltaje REAL del borne
            8 => 0.314 + (TemperaturaChip - 25) * 0.001,                 // sensor de temperatura interno (~1mV/°C)
            14 => 1.1,                                                   // bandgap de 1.1V
            _ => 0,                                                      // canales que el DIP no tiene / GND
        };

        int valor = Math.Clamp((int)(voltaje / vref * 1024.0), 0, 1023);

        if ((_datos[DIR_ADMUX] & 0x20) != 0) // ADLAR: resultado alineado a la izquierda
        {
            _datos[DIR_ADCL] = (byte)((valor << 6) & 0xC0);
            _datos[DIR_ADCH] = (byte)(valor >> 2);
        }
        else
        {
            _datos[DIR_ADCL] = (byte)(valor & 0xFF);
            _datos[DIR_ADCH] = (byte)(valor >> 8);
        }

        _flagAdc = true;
        _revisarInterrupciones = true;
    }

    // =====================================================================
    //  EEPROM (1KB que sobrevive al reset, como la del chip)
    // =====================================================================

    private void EscribirEecr(byte valor)
    {
        int direccion = (_datos[DIR_EEARL] | (_datos[DIR_EEARH] << 8)) & 0x3FF;
        if ((valor & 0b0001) != 0) // EERE: leer
        {
            _datos[DIR_EEDR] = _eeprom[direccion];
        }
        if ((valor & 0b0010) != 0) // EEPE: escribir (aceptamos la secuencia EEMPE→EEPE sin cronometrarla)
        {
            _eeprom[direccion] = _datos[DIR_EEDR];
        }
        // EEPE se limpia "al instante": la escritura acá no tarda 3.3ms.
        _datos[DIR_EECR] = (byte)(valor & 0b0011_1100);
    }
}
