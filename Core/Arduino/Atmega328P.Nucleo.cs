namespace ElectricSim.Core.Arduino;

/// <summary>
/// La otra mitad del ATmega328P: el núcleo AVR propiamente dicho. Acá vive
/// el ciclo FETCH → DECODE → EXECUTE, los 32 registros de trabajo, el
/// contador de programa (PC), el puntero de pila (SP), el registro de
/// estado (SREG) y la implementación de TODO el set de instrucciones que
/// avr-gcc genera para este chip: aritmética, lógica, saltos, llamadas,
/// accesos a memoria, manipulación de bits y multiplicación.
///
/// Referencia: "AVR Instruction Set Manual" de Microchip. Cada instrucción
/// respeta su codificación, sus banderas de SREG y su cantidad de ciclos,
/// porque de los ciclos exactos dependen los delays por software
/// (_delay_ms de avr-libc cuenta ciclos a mano) y el timing general.
/// </summary>
public sealed partial class Atmega328P
{
    // =====================================================================
    //  Direcciones del espacio de datos (registros de E/S del ATmega328P)
    // =====================================================================

    private const int DIR_PINB = 0x23, DIR_DDRB = 0x24, DIR_PORTB = 0x25;
    private const int DIR_PINC = 0x26, DIR_DDRC = 0x27, DIR_PORTC = 0x28;
    private const int DIR_PIND = 0x29, DIR_DDRD = 0x2A, DIR_PORTD = 0x2B;
    private const int DIR_TIFR0 = 0x35, DIR_TIFR1 = 0x36, DIR_TIFR2 = 0x37;
    private const int DIR_PCIFR = 0x3B, DIR_EIFR = 0x3C, DIR_EIMSK = 0x3D;
    private const int DIR_EECR = 0x3F, DIR_EEDR = 0x40, DIR_EEARL = 0x41, DIR_EEARH = 0x42;
    private const int DIR_TCCR0A = 0x44, DIR_TCCR0B = 0x45, DIR_TCNT0 = 0x46, DIR_OCR0A = 0x47, DIR_OCR0B = 0x48;
    private const int DIR_MCUSR = 0x54;
    private const int DIR_SPL = 0x5D, DIR_SPH = 0x5E, DIR_SREG = 0x5F;
    private const int DIR_PCICR = 0x68, DIR_EICRA = 0x69;
    private const int DIR_PCMSK0 = 0x6B, DIR_PCMSK1 = 0x6C, DIR_PCMSK2 = 0x6D;
    private const int DIR_TIMSK0 = 0x6E, DIR_TIMSK1 = 0x6F, DIR_TIMSK2 = 0x70;
    private const int DIR_ADCL = 0x78, DIR_ADCH = 0x79, DIR_ADCSRA = 0x7A, DIR_ADMUX = 0x7C;
    private const int DIR_TCCR1A = 0x80, DIR_TCCR1B = 0x81;
    private const int DIR_TCNT1L = 0x84, DIR_TCNT1H = 0x85, DIR_ICR1L = 0x86, DIR_ICR1H = 0x87;
    private const int DIR_OCR1AL = 0x88, DIR_OCR1AH = 0x89, DIR_OCR1BL = 0x8A, DIR_OCR1BH = 0x8B;
    private const int DIR_TCCR2A = 0xB0, DIR_TCCR2B = 0xB1, DIR_TCNT2 = 0xB2, DIR_OCR2A = 0xB3, DIR_OCR2B = 0xB4;
    private const int DIR_UCSR0A = 0xC0, DIR_UCSR0B = 0xC1;
    private const int DIR_UBRR0L = 0xC4, DIR_UBRR0H = 0xC5, DIR_UDR0 = 0xC6;

    // =====================================================================
    //  Estado del núcleo
    // =====================================================================

    private int _pc;      // contador de programa, en PALABRAS (el AVR direcciona la flash de a 16 bits)
    private byte _sreg;   // registro de estado: I T H S V N Z C
    private byte _temp16; // el registro TEMP del acceso de 16 bits al Timer1 (así lo hace el chip real)

    private const int MASCARA_PC = TamanoFlash / 2 - 1; // 16K palabras → 14 bits

    // Banderas del SREG (FLAG_I = 0x80 está definida en la otra mitad de la clase).
    private const byte F_C = 0x01, F_Z = 0x02, F_N = 0x04, F_V = 0x08;
    private const byte F_S = 0x10, F_H = 0x20, F_T = 0x40;

    /// <summary>Puntero de pila. Vive en 0x5D/0x5E del espacio de datos, como en el chip.</summary>
    private ushort SP
    {
        get => (ushort)(_datos[DIR_SPL] | (_datos[DIR_SPH] << 8));
        set { _datos[DIR_SPL] = (byte)value; _datos[DIR_SPH] = (byte)(value >> 8); }
    }

    /// <summary>PC actual en bytes (informativo, para el inspector).</summary>
    public int ContadorDePrograma => _pc * 2;

    private void Detener(string motivo)
    {
        Detenida = true;
        MotivoDetencion = motivo;
    }

    // =====================================================================
    //  Pila
    // =====================================================================

    private void Push8(byte valor)
    {
        int sp = SP;
        if (sp < 0x0100)
        {
            // La pila se comió toda la SRAM y está por pisar los registros:
            // en el chip real esto corrompe todo en silencio; acá lo frenamos
            // y lo contamos, que es más útil para aprender.
            Detener("Desborde de pila (stack overflow): la RAM se llenó, probablemente por recursión infinita.");
            return;
        }
        _datos[sp] = valor;
        SP = (ushort)(sp - 1);
    }

    private byte Pop8()
    {
        int sp = SP + 1;
        SP = (ushort)sp;
        return sp < _datos.Length ? _datos[sp] : (byte)0;
    }

    private void Push16(ushort valor)
    {
        Push8((byte)valor);          // primero el byte bajo (queda en la dirección más alta)...
        Push8((byte)(valor >> 8));   // ...después el alto, como hace CALL en el AVR
    }

    private ushort Pop16()
    {
        int alto = Pop8();
        int bajo = Pop8();
        return (ushort)((alto << 8) | bajo);
    }

    // =====================================================================
    //  Acceso al espacio de datos, con ruteo a los periféricos
    // =====================================================================

    /// <summary>
    /// Lee una dirección del espacio de datos. La mayoría son la RAM tal
    /// cual; las direcciones de E/S "vivas" (PINx, timers, UART, ADC...) se
    /// componen al vuelo consultando al periférico, igual que el bus real.
    /// </summary>
    private byte LeerDato(int direccion)
    {
        switch (direccion)
        {
            case DIR_PINB: return ComponerPin('B');
            case DIR_PINC: return ComponerPin('C');
            case DIR_PIND: return ComponerPin('D');
            case DIR_SREG: return _sreg;

            case DIR_TCCR0A: return _timer0.TCCRA;
            case DIR_TCCR0B: return _timer0.TCCRB;
            case DIR_TCNT0: return (byte)_timer0.Contador;
            case DIR_OCR0A: return (byte)_timer0.OcrA;
            case DIR_OCR0B: return (byte)_timer0.OcrB;

            case DIR_TCCR2A: return _timer2.TCCRA;
            case DIR_TCCR2B: return _timer2.TCCRB;
            case DIR_TCNT2: return (byte)_timer2.Contador;
            case DIR_OCR2A: return (byte)_timer2.OcrA;
            case DIR_OCR2B: return (byte)_timer2.OcrB;

            case DIR_TCCR1A: return _timer1.TCCRA;
            case DIR_TCCR1B: return _timer1.TCCRB;
            case DIR_TCNT1L:
                // Leer el byte bajo "congela" el alto en TEMP: así el programa
                // obtiene una foto coherente de los 16 bits (mecanismo real).
                _temp16 = (byte)(_timer1.Contador >> 8);
                return (byte)_timer1.Contador;
            case DIR_TCNT1H: return _temp16;
            case DIR_ICR1L: _temp16 = (byte)(_timer1.Icr >> 8); return (byte)_timer1.Icr;
            case DIR_ICR1H: return _temp16;
            case DIR_OCR1AL: return (byte)_timer1.OcrA;
            case DIR_OCR1AH: return (byte)(_timer1.OcrA >> 8);
            case DIR_OCR1BL: return (byte)_timer1.OcrB;
            case DIR_OCR1BH: return (byte)(_timer1.OcrB >> 8);

            case DIR_UCSR0A:
                // UDRE (bit 5): 1 = el registro está libre para otro byte. En
                // modo instantáneo siempre lo está; en modo realista, solo
                // cuando no hay una transmisión en curso. TXC (bit 6) =
                // transmisión completa. RXC (bit 7) refleja la cola de entrada.
                return (byte)((_datos[DIR_UCSR0A] & 0x03)
                    | (TransmisionInstantanea || _ciclosTxRestantes <= 0 ? 0x20 : 0)
                    | (_flagTxCompleta ? 0x40 : 0)
                    | (_serialRx.Count > 0 ? 0x80 : 0));
            case DIR_UDR0:
                return _serialRx.Count > 0 ? _serialRx.Dequeue() : (byte)0;

            case DIR_ADCSRA:
                return (byte)((_datos[DIR_ADCSRA] & 0x8F)
                    | (_adcCiclosRestantes > 0 ? 0x40 : 0)   // ADSC en 1 mientras convierte
                    | (_flagAdc ? 0x10 : 0));                 // ADIF

            default:
                return (uint)direccion < (uint)_datos.Length ? _datos[direccion] : (byte)0;
        }
    }

    /// <summary>Escribe una dirección del espacio de datos, avisándole al periférico dueño si corresponde.</summary>
    private void EscribirDato(int direccion, byte valor)
    {
        switch (direccion)
        {
            // Escribir un 1 en PINx INVIERTE el bit de PORTx (sí, el chip
            // real hace eso: es el toggle rápido que usa digitalWrite optimizado).
            case DIR_PINB: _datos[DIR_PORTB] ^= valor; RefrescarPuerto('B'); break;
            case DIR_PINC: _datos[DIR_PORTC] ^= valor; RefrescarPuerto('C'); break;
            case DIR_PIND: _datos[DIR_PORTD] ^= valor; RefrescarPuerto('D'); break;

            case DIR_PORTB: case DIR_DDRB: _datos[direccion] = valor; RefrescarPuerto('B'); break;
            case DIR_PORTC: case DIR_DDRC: _datos[direccion] = valor; RefrescarPuerto('C'); break;
            case DIR_PORTD: case DIR_DDRD: _datos[direccion] = valor; RefrescarPuerto('D'); break;

            case DIR_SREG:
                bool habilitaI = (valor & FLAG_I) != 0 && (_sreg & FLAG_I) == 0;
                _sreg = valor;
                if (habilitaI)
                {
                    _revisarInterrupciones = true;
                    _instruccionesDeGracia = Math.Max(_instruccionesDeGracia, 1);
                }
                break;

            case DIR_TCCR0A: _timer0.TCCRA = valor; break;
            case DIR_TCCR0B: _timer0.TCCRB = valor; break;
            case DIR_TCNT0: _timer0.Contador = valor; break;
            case DIR_OCR0A: _timer0.OcrA = valor; break;
            case DIR_OCR0B: _timer0.OcrB = valor; break;

            case DIR_TCCR2A: _timer2.TCCRA = valor; break;
            case DIR_TCCR2B: _timer2.TCCRB = valor; break;
            case DIR_TCNT2: _timer2.Contador = valor; break;
            case DIR_OCR2A: _timer2.OcrA = valor; break;
            case DIR_OCR2B: _timer2.OcrB = valor; break;

            case DIR_TCCR1A: _timer1.TCCRA = valor; break;
            case DIR_TCCR1B: _timer1.TCCRB = valor; break;
            // Acceso de 16 bits con TEMP: el programa escribe el byte ALTO
            // primero (queda en TEMP) y el bajo después (compromete los 16).
            case DIR_TCNT1H: case DIR_ICR1H: case DIR_OCR1AH: case DIR_OCR1BH:
                _temp16 = valor;
                break;
            case DIR_TCNT1L: _timer1.Contador = (_temp16 << 8) | valor; break;
            case DIR_ICR1L: _timer1.Icr = (_temp16 << 8) | valor; break;
            case DIR_OCR1AL: _timer1.OcrA = (_temp16 << 8) | valor; break;
            case DIR_OCR1BL: _timer1.OcrB = (_temp16 << 8) | valor; break;

            // Banderas de interrupción: se limpian ESCRIBIENDO UN 1 (clásico AVR).
            case DIR_TIFR0: case DIR_TIFR1: case DIR_TIFR2:
            case DIR_EIFR: case DIR_PCIFR:
                _datos[direccion] &= (byte)~valor;
                break;

            // Habilitadores de interrupción: puede haber quedado algo pendiente que ahora sí corresponde atender.
            case DIR_TIMSK0: case DIR_TIMSK1: case DIR_TIMSK2:
            case DIR_EIMSK: case DIR_PCICR: case DIR_EICRA:
            case DIR_PCMSK0: case DIR_PCMSK1: case DIR_PCMSK2:
            case DIR_UCSR0B:
                _datos[direccion] = valor;
                _revisarInterrupciones = true;
                break;

            case DIR_UCSR0A:
                _datos[DIR_UCSR0A] = (byte)(valor & 0x03);       // solo U2X/MPCM son escribibles
                if ((valor & 0x40) != 0) _flagTxCompleta = false; // TXC se limpia escribiendo 1
                break;
            case DIR_UDR0:
                TransmitirSerial(valor);
                break;

            case DIR_ADCSRA:
                _datos[DIR_ADCSRA] = (byte)(valor & 0x8F);                    // ADEN, ADIE, ADPS
                if ((valor & 0x10) != 0) _flagAdc = false;                    // ADIF: limpiar con 1
                if ((valor & 0x40) != 0 && (valor & 0x80) != 0)               // ADSC con ADEN: ¡a convertir!
                {
                    IniciarConversionAdc();
                }
                break;

            case DIR_EECR:
                EscribirEecr(valor);
                break;

            default:
                if ((uint)direccion < (uint)_datos.Length) _datos[direccion] = valor;
                break;
        }
    }

    // =====================================================================
    //  Banderas de SREG (las fórmulas del manual, tal cual)
    // =====================================================================

    private void PonerFlag(byte bandera, bool valor)
    {
        if (valor) _sreg |= bandera;
        else _sreg &= (byte)~bandera;
    }

    /// <summary>Banderas de una suma de 8 bits (ADD/ADC). Devuelve el resultado.</summary>
    private byte FlagsSuma(int d, int r, int acarreo)
    {
        byte res = (byte)(d + r + acarreo);
        int mezcla = (d & r) | (r & ~res) | (~res & d);
        PonerFlag(F_H, (mezcla & 0x08) != 0);
        PonerFlag(F_C, (mezcla & 0x80) != 0);
        PonerFlag(F_V, (((d & r & ~res) | (~d & ~r & res)) & 0x80) != 0);
        PonerFlag(F_N, (res & 0x80) != 0);
        PonerFlag(F_Z, res == 0);
        PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
        return res;
    }

    /// <summary>Banderas de una resta de 8 bits (SUB/CP/SUBI...). Con acarreoPrevio y zetaEncadenada emula SBC/CPC/SBCI.</summary>
    private byte FlagsResta(int d, int r, int acarreo, bool zetaEncadenada)
    {
        byte res = (byte)(d - r - acarreo);
        int mezcla = (~d & r) | (r & res) | (res & ~d);
        PonerFlag(F_H, (mezcla & 0x08) != 0);
        PonerFlag(F_C, (mezcla & 0x80) != 0);
        PonerFlag(F_V, (((d & ~r & ~res) | (~d & r & res)) & 0x80) != 0);
        PonerFlag(F_N, (res & 0x80) != 0);
        // El detalle fino del AVR: en las restas CON acarreo (SBC/CPC), Z solo
        // queda en 1 si el resultado es 0 Y ya venía en 1 — así funcionan las
        // comparaciones de 16/32 bits encadenadas que genera el compilador.
        PonerFlag(F_Z, res == 0 && (!zetaEncadenada || (_sreg & F_Z) != 0));
        PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
        return res;
    }

    /// <summary>Banderas de las operaciones lógicas (AND/OR/EOR): V=0, N, Z, S.</summary>
    private byte FlagsLogica(byte res)
    {
        PonerFlag(F_V, false);
        PonerFlag(F_N, (res & 0x80) != 0);
        PonerFlag(F_Z, res == 0);
        PonerFlag(F_S, (res & 0x80) != 0);
        return res;
    }

    // =====================================================================
    //  EL ciclo: una instrucción completa. Devuelve los ciclos que tomó.
    // =====================================================================

    private int EjecutarInstruccion()
    {
        ushort op = _flashPalabras[_pc & MASCARA_PC];
        _pc = (_pc + 1) & MASCARA_PC;

        // Campos que se repiten en casi todas las codificaciones:
        int d = (op >> 4) & 0x1F;                    // registro destino (5 bits)
        int r = (op & 0x0F) | ((op >> 5) & 0x10);    // registro fuente (5 bits, partido)

        switch (op >> 12)
        {
            // -------------------------------------------------- 0000 ----
            case 0x0:
                if (op == 0x0000) return 1; // NOP

                switch (op & 0x0F00)
                {
                    case 0x0100: // MOVW: copia un par de registros de una
                    {
                        int dd = ((op >> 4) & 0xF) * 2, rr = (op & 0xF) * 2;
                        _datos[dd] = _datos[rr];
                        _datos[dd + 1] = _datos[rr + 1];
                        return 1;
                    }
                    case 0x0200: // MULS
                    {
                        int prod = (sbyte)_datos[16 + ((op >> 4) & 0xF)] * (sbyte)_datos[16 + (op & 0xF)];
                        _datos[0] = (byte)prod;
                        _datos[1] = (byte)(prod >> 8);
                        PonerFlag(F_C, (prod & 0x8000) != 0);
                        PonerFlag(F_Z, (prod & 0xFFFF) == 0);
                        return 2;
                    }
                    case 0x0300: // MULSU / FMUL / FMULS / FMULSU
                    {
                        int rd = 16 + ((op >> 4) & 0x7), rr2 = 16 + (op & 0x7);
                        int prod = (op & 0x88) switch
                        {
                            0x00 => (sbyte)_datos[rd] * _datos[rr2],          // MULSU
                            0x08 => _datos[rd] * _datos[rr2],                 // FMUL
                            0x80 => (sbyte)_datos[rd] * (sbyte)_datos[rr2],   // FMULS
                            _ => (sbyte)_datos[rd] * _datos[rr2],             // FMULSU
                        };
                        // En las variantes fraccionales el producto se corre 1 a la izquierda; C es el bit 15 ANTES del corrimiento.
                        PonerFlag(F_C, (prod & 0x8000) != 0);
                        if ((op & 0x88) != 0x00) prod <<= 1; // todas menos MULSU son fraccionales
                        _datos[0] = (byte)prod;
                        _datos[1] = (byte)(prod >> 8);
                        PonerFlag(F_Z, (prod & 0xFFFF) == 0);
                        return 2;
                    }
                }

                switch (op & 0xFC00)
                {
                    case 0x0400: // CPC: comparación con acarreo (no guarda)
                        FlagsResta(_datos[d], _datos[r], _sreg & F_C, zetaEncadenada: true);
                        return 1;
                    case 0x0800: // SBC
                        _datos[d] = FlagsResta(_datos[d], _datos[r], _sreg & F_C, zetaEncadenada: true);
                        return 1;
                    case 0x0C00: // ADD
                        _datos[d] = FlagsSuma(_datos[d], _datos[r], 0);
                        return 1;
                }
                break;

            // -------------------------------------------------- 0001 ----
            case 0x1:
                switch (op & 0xFC00)
                {
                    case 0x1000: // CPSE: saltear la siguiente si Rd == Rr
                        return _datos[d] == _datos[r] ? 1 + Saltear() : 1;
                    case 0x1400: // CP
                        FlagsResta(_datos[d], _datos[r], 0, false);
                        return 1;
                    case 0x1800: // SUB
                        _datos[d] = FlagsResta(_datos[d], _datos[r], 0, false);
                        return 1;
                    case 0x1C00: // ADC
                        _datos[d] = FlagsSuma(_datos[d], _datos[r], _sreg & F_C);
                        return 1;
                }
                break;

            // -------------------------------------------------- 0010 ----
            case 0x2:
                switch (op & 0xFC00)
                {
                    case 0x2000: _datos[d] = FlagsLogica((byte)(_datos[d] & _datos[r])); return 1; // AND
                    case 0x2400: _datos[d] = FlagsLogica((byte)(_datos[d] ^ _datos[r])); return 1; // EOR
                    case 0x2800: _datos[d] = FlagsLogica((byte)(_datos[d] | _datos[r])); return 1; // OR
                    case 0x2C00: _datos[d] = _datos[r]; return 1;                                   // MOV
                }
                break;

            // ------------------------------------- inmediatos (Rd = r16-r31)
            case 0x3: // CPI
                FlagsResta(_datos[16 + ((op >> 4) & 0xF)], Inmediato(op), 0, false);
                return 1;
            case 0x4: // SBCI
            {
                int rd = 16 + ((op >> 4) & 0xF);
                _datos[rd] = FlagsResta(_datos[rd], Inmediato(op), _sreg & F_C, zetaEncadenada: true);
                return 1;
            }
            case 0x5: // SUBI
            {
                int rd = 16 + ((op >> 4) & 0xF);
                _datos[rd] = FlagsResta(_datos[rd], Inmediato(op), 0, false);
                return 1;
            }
            case 0x6: // ORI
            {
                int rd = 16 + ((op >> 4) & 0xF);
                _datos[rd] = FlagsLogica((byte)(_datos[rd] | Inmediato(op)));
                return 1;
            }
            case 0x7: // ANDI
            {
                int rd = 16 + ((op >> 4) & 0xF);
                _datos[rd] = FlagsLogica((byte)(_datos[rd] & Inmediato(op)));
                return 1;
            }

            // ------------------------- LDD/STD con desplazamiento (Y+q, Z+q)
            case 0x8:
            case 0xA:
            {
                int q = (op & 0x7) | ((op >> 7) & 0x18) | ((op >> 8) & 0x20);
                int baseReg = (op & 0x8) != 0 ? 28 : 30; // Y o Z
                int direccion = LeerPar(baseReg) + q;
                if ((op & 0x200) != 0) EscribirDato(direccion, _datos[d]); // STD
                else _datos[d] = LeerDato(direccion);                      // LDD
                return 2;
            }

            // -------------------------------------------------- 1001 ----
            case 0x9:
                return EjecutarGrupo9(op, d);

            // -------------------------------------------------- IN / OUT
            case 0xB:
            {
                int a = 0x20 + ((op & 0xF) | ((op >> 5) & 0x30));
                if ((op & 0x800) != 0) EscribirDato(a, _datos[d]); // OUT
                else _datos[d] = LeerDato(a);                      // IN
                return 1;
            }

            // -------------------------------------------------- RJMP ----
            case 0xC:
            {
                int k = op & 0xFFF;
                if ((k & 0x800) != 0) k -= 0x1000;
                _pc = (_pc + k) & MASCARA_PC;
                return 2;
            }

            // -------------------------------------------------- RCALL ---
            case 0xD:
            {
                int k = op & 0xFFF;
                if ((k & 0x800) != 0) k -= 0x1000;
                Push16((ushort)_pc);
                _pc = (_pc + k) & MASCARA_PC;
                return 3;
            }

            // -------------------------------------------------- LDI -----
            case 0xE:
                _datos[16 + ((op >> 4) & 0xF)] = Inmediato(op);
                return 1;

            // -------------------------------------------------- 1111 ----
            case 0xF:
                // Saltos condicionales por bandera de SREG.
                if ((op & 0xFC00) == 0xF000 || (op & 0xFC00) == 0xF400)
                {
                    int bit = op & 0x7;
                    bool saltaSiUno = (op & 0x0400) == 0; // BRBS salta si la bandera está en 1; BRBC si está en 0
                    bool bandera = (_sreg & (1 << bit)) != 0;
                    if (bandera == saltaSiUno)
                    {
                        int k = (op >> 3) & 0x7F;
                        if ((k & 0x40) != 0) k -= 0x80;
                        _pc = (_pc + k) & MASCARA_PC;
                        return 2;
                    }
                    return 1;
                }
                switch (op & 0xFE08)
                {
                    case 0xF800: // BLD: copia T al bit b de Rd
                    {
                        int bit = op & 0x7;
                        if ((_sreg & F_T) != 0) _datos[d] |= (byte)(1 << bit);
                        else _datos[d] &= (byte)~(1 << bit);
                        return 1;
                    }
                    case 0xFA00: // BST: guarda el bit b de Rd en T
                        PonerFlag(F_T, (_datos[d] & (1 << (op & 0x7))) != 0);
                        return 1;
                    case 0xFC00: // SBRC: saltear si el bit está en 0
                        return (_datos[d] & (1 << (op & 0x7))) == 0 ? 1 + Saltear() : 1;
                    case 0xFE00: // SBRS: saltear si el bit está en 1
                        return (_datos[d] & (1 << (op & 0x7))) != 0 ? 1 + Saltear() : 1;
                }
                break;
        }

        // Opcode que no reconocemos: lo contamos y seguimos como NOP. Si hay
        // muchos, el .hex no es para este chip (o se está ejecutando basura).
        InstruccionesDesconocidas++;
        if (InstruccionesDesconocidas > 200)
        {
            Detener("Demasiadas instrucciones desconocidas: ¿el .hex es para un ATmega328P?");
        }
        return 1;
    }

    /// <summary>El grupo 1001 xxxx: cargas/almacenamientos, saltos absolutos, operaciones de un registro, pila, bits de E/S y MUL.</summary>
    private int EjecutarGrupo9(ushort op, int d)
    {
        // --- LDS / STS / LD / ST / LPM / POP / PUSH: 1001 00xx ---
        if ((op & 0xFC00) == 0x9000)
        {
            bool almacena = (op & 0x0200) != 0;
            switch (op & 0x000F)
            {
                case 0x0: // LDS / STS (la dirección viene en la palabra siguiente)
                {
                    int direccion = _flashPalabras[_pc & MASCARA_PC];
                    _pc = (_pc + 1) & MASCARA_PC;
                    if (almacena) EscribirDato(direccion, _datos[d]);
                    else _datos[d] = LeerDato(direccion);
                    return 2;
                }
                case 0x1: return AccesoIndirecto(op, d, 30, post: +1);  // Z+
                case 0x2: return AccesoIndirecto(op, d, 30, post: -1);  // -Z
                case 0x4: // LPM Rd, Z — leer la FLASH (tablas de datos, strings en PROGMEM)
                    if (almacena) break;
                    _datos[d] = _flash[LeerPar(30) & (TamanoFlash - 1)];
                    return 3;
                case 0x5: // LPM Rd, Z+
                {
                    if (almacena) break;
                    ushort z = LeerPar(30);
                    _datos[d] = _flash[z & (TamanoFlash - 1)];
                    EscribirPar(30, (ushort)(z + 1));
                    return 3;
                }
                case 0x6: // ELPM Rd, Z (en el 328P no hay banco alto: es un LPM)
                    if (almacena) break;
                    _datos[d] = _flash[LeerPar(30) & (TamanoFlash - 1)];
                    return 3;
                case 0x7:
                {
                    if (almacena) break;
                    ushort z = LeerPar(30);
                    _datos[d] = _flash[z & (TamanoFlash - 1)];
                    EscribirPar(30, (ushort)(z + 1));
                    return 3;
                }
                case 0x9: return AccesoIndirecto(op, d, 28, post: +1);  // Y+
                case 0xA: return AccesoIndirecto(op, d, 28, post: -1);  // -Y
                case 0xC: return AccesoIndirecto(op, d, 26, post: 0);   // X
                case 0xD: return AccesoIndirecto(op, d, 26, post: +1);  // X+
                case 0xE: return AccesoIndirecto(op, d, 26, post: -1);  // -X
                case 0xF: // POP / PUSH
                    if (almacena) Push8(_datos[d]);
                    else _datos[d] = Pop8();
                    return 2;
            }
        }

        // --- Operaciones de un solo registro y misceláneas: 1001 010x ---
        if ((op & 0xFE00) == 0x9400)
        {
            switch (op & 0x000F)
            {
                case 0x0: // COM: complemento a uno
                    _datos[d] = FlagsLogica((byte)~_datos[d]);
                    PonerFlag(F_C, true);
                    return 1;
                case 0x1: // NEG: complemento a dos
                {
                    byte previo = _datos[d];
                    byte res = (byte)(0 - previo);
                    _datos[d] = res;
                    PonerFlag(F_H, ((res | previo) & 0x08) != 0);
                    PonerFlag(F_C, res != 0);
                    PonerFlag(F_V, res == 0x80);
                    PonerFlag(F_N, (res & 0x80) != 0);
                    PonerFlag(F_Z, res == 0);
                    PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
                    return 1;
                }
                case 0x2: // SWAP: intercambia los nibbles
                    _datos[d] = (byte)((_datos[d] << 4) | (_datos[d] >> 4));
                    return 1;
                case 0x3: // INC (no toca C)
                {
                    byte res = (byte)(_datos[d] + 1);
                    _datos[d] = res;
                    PonerFlag(F_V, res == 0x80);
                    PonerFlag(F_N, (res & 0x80) != 0);
                    PonerFlag(F_Z, res == 0);
                    PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
                    return 1;
                }
                case 0x5: // ASR: corrimiento aritmético (conserva el signo)
                {
                    byte previo = _datos[d];
                    byte res = (byte)((previo >> 1) | (previo & 0x80));
                    _datos[d] = res;
                    FlagsCorrimiento(res, (previo & 1) != 0);
                    return 1;
                }
                case 0x6: // LSR
                {
                    byte previo = _datos[d];
                    byte res = (byte)(previo >> 1);
                    _datos[d] = res;
                    FlagsCorrimiento(res, (previo & 1) != 0);
                    return 1;
                }
                case 0x7: // ROR: rota a la derecha a través del acarreo
                {
                    byte previo = _datos[d];
                    byte res = (byte)((previo >> 1) | ((_sreg & F_C) != 0 ? 0x80 : 0));
                    _datos[d] = res;
                    FlagsCorrimiento(res, (previo & 1) != 0);
                    return 1;
                }
                case 0x8: // BSET / BCLR / RET / RETI / SLEEP / BREAK / WDR / LPM / SPM
                    return EjecutarMiscelanea(op);
                case 0x9: // IJMP / ICALL: saltar a la dirección que dice Z
                    if ((op & 0x0100) != 0) Push16((ushort)_pc); // ICALL
                    _pc = LeerPar(30) & MASCARA_PC;
                    return (op & 0x0100) != 0 ? 3 : 2;
                case 0xA: // DEC (no toca C)
                {
                    byte res = (byte)(_datos[d] - 1);
                    _datos[d] = res;
                    PonerFlag(F_V, res == 0x7F);
                    PonerFlag(F_N, (res & 0x80) != 0);
                    PonerFlag(F_Z, res == 0);
                    PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
                    return 1;
                }
                case 0xC: case 0xD: // JMP absoluto (2 palabras)
                {
                    int k = (((op & 0x1F0) >> 3) | (op & 1)) << 16;
                    k |= _flashPalabras[_pc & MASCARA_PC];
                    _pc = k & MASCARA_PC;
                    return 3;
                }
                case 0xE: case 0xF: // CALL absoluto (2 palabras)
                {
                    int k = (((op & 0x1F0) >> 3) | (op & 1)) << 16;
                    k |= _flashPalabras[_pc & MASCARA_PC];
                    Push16((ushort)((_pc + 1) & MASCARA_PC)); // la vuelta es DESPUÉS de la segunda palabra
                    _pc = k & MASCARA_PC;
                    return 4;
                }
            }
        }

        // --- ADIW / SBIW: sumar/restar constante a un par (X, Y, Z o r25:24) ---
        if ((op & 0xFE00) == 0x9600)
        {
            int par = 24 + 2 * ((op >> 4) & 0x3);
            int k = (op & 0xF) | ((op >> 2) & 0x30);
            ushort previo = LeerPar(par);
            ushort res;
            if ((op & 0x0100) == 0) // ADIW
            {
                res = (ushort)(previo + k);
                PonerFlag(F_V, ((~previo & res) & 0x8000) != 0);
                PonerFlag(F_C, ((previo & ~res) & 0x8000) != 0);
            }
            else // SBIW
            {
                res = (ushort)(previo - k);
                PonerFlag(F_V, ((previo & ~res) & 0x8000) != 0);
                PonerFlag(F_C, ((res & ~previo) & 0x8000) != 0);
            }
            EscribirPar(par, res);
            PonerFlag(F_N, (res & 0x8000) != 0);
            PonerFlag(F_Z, res == 0);
            PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
            return 2;
        }

        // --- CBI / SBIC / SBI / SBIS: bits de los primeros 32 registros de E/S ---
        if ((op & 0xFC00) == 0x9800)
        {
            int direccion = 0x20 + ((op >> 3) & 0x1F);
            int bit = 1 << (op & 0x7);
            switch (op & 0x0300)
            {
                case 0x0000: // CBI
                    EscribirDato(direccion, (byte)(LeerDato(direccion) & ~bit));
                    return 2;
                case 0x0200: // SBI
                    EscribirDato(direccion, (byte)(LeerDato(direccion) | bit));
                    return 2;
                case 0x0100: // SBIC: saltear si el bit de E/S está en 0
                    return (LeerDato(direccion) & bit) == 0 ? 1 + Saltear() : 1;
                default:     // SBIS
                    return (LeerDato(direccion) & bit) != 0 ? 1 + Saltear() : 1;
            }
        }

        // --- MUL: multiplicación sin signo, resultado en R1:R0 ---
        if ((op & 0xFC00) == 0x9C00)
        {
            int r = (op & 0x0F) | ((op >> 5) & 0x10);
            int prod = _datos[d] * _datos[r];
            _datos[0] = (byte)prod;
            _datos[1] = (byte)(prod >> 8);
            PonerFlag(F_C, (prod & 0x8000) != 0);
            PonerFlag(F_Z, (prod & 0xFFFF) == 0);
            return 2;
        }

        InstruccionesDesconocidas++;
        return 1;
    }

    /// <summary>BSET/BCLR (SEI, CLI, SEC... son alias de estas) y las instrucciones "sueltas" RET/RETI/SLEEP/BREAK/WDR/LPM/SPM.</summary>
    private int EjecutarMiscelanea(ushort op)
    {
        switch (op)
        {
            case 0x9508: // RET
                _pc = Pop16() & MASCARA_PC;
                return 4;
            case 0x9518: // RETI: volver de una interrupción, rehabilitándolas
                _pc = Pop16() & MASCARA_PC;
                _sreg |= FLAG_I;
                _revisarInterrupciones = true;
                _instruccionesDeGracia = 1; // el chip garantiza 1 instrucción del programa antes de otra interrupción
                return 4;
            case 0x9588: return 1; // SLEEP (acá no ahorra energía, pero tampoco molesta)
            case 0x95A8: return 1; // WDR (no emulamos el watchdog: nunca muerde)
            case 0x9598: // BREAK: pensada para debuggers; frenamos la CPU
                Detener("El programa ejecutó BREAK.");
                return 1;
            case 0x95C8: // LPM implícito: R0 = flash[Z]
                _datos[0] = _flash[LeerPar(30) & (TamanoFlash - 1)];
                return 3;
            case 0x95E8: return 1; // SPM: autoprogramación de la flash (cosa de bootloaders; no aplica)
        }

        if ((op & 0xFF8F) == 0x9408) // BSET s (SEC/SEZ/SEN/SEV/SES/SEH/SET/SEI)
        {
            byte bandera = (byte)(1 << ((op >> 4) & 0x7));
            bool habilitaI = bandera == FLAG_I && (_sreg & FLAG_I) == 0;
            _sreg |= bandera;
            if (habilitaI)
            {
                _revisarInterrupciones = true;
                _instruccionesDeGracia = Math.Max(_instruccionesDeGracia, 1); // SEI también da 1 instrucción de gracia
            }
            return 1;
        }
        if ((op & 0xFF8F) == 0x9488) // BCLR s (CLC/CLZ/.../CLI)
        {
            _sreg &= (byte)~(1 << ((op >> 4) & 0x7));
            return 1;
        }

        InstruccionesDesconocidas++;
        return 1;
    }

    // =====================================================================
    //  Ayudantes de decodificación
    // =====================================================================

    /// <summary>La constante de 8 bits de CPI/SUBI/LDI...: 4 bits abajo, 4 arriba.</summary>
    private static byte Inmediato(ushort op) => (byte)((op & 0x0F) | ((op >> 4) & 0xF0));

    /// <summary>LD/ST por X, Y o Z con pre-decremento (post = -1), post-incremento (+1) o sin tocar (0).</summary>
    private int AccesoIndirecto(ushort op, int d, int regPar, int post)
    {
        ushort puntero = LeerPar(regPar);
        if (post < 0)
        {
            puntero--;
            EscribirPar(regPar, puntero);
        }
        if ((op & 0x0200) != 0) EscribirDato(puntero, _datos[d]); // ST
        else _datos[d] = LeerDato(puntero);                       // LD
        if (post > 0)
        {
            EscribirPar(regPar, (ushort)(puntero + 1));
        }
        return 2;
    }

    /// <summary>Banderas comunes a ASR/LSR/ROR: C del bit expulsado, V = N⊕C.</summary>
    private void FlagsCorrimiento(byte res, bool acarreo)
    {
        PonerFlag(F_C, acarreo);
        PonerFlag(F_N, (res & 0x80) != 0);
        PonerFlag(F_V, ((res & 0x80) != 0) ^ acarreo);
        PonerFlag(F_Z, res == 0);
        PonerFlag(F_S, ((_sreg & F_N) != 0) ^ ((_sreg & F_V) != 0));
    }

    /// <summary>
    /// Saltea la instrucción siguiente (para CPSE/SBRC/SBRS/SBIC/SBIS).
    /// Si la siguiente ocupa DOS palabras (LDS/STS/JMP/CALL) hay que saltear
    /// las dos. Devuelve los ciclos extra que costó el salteo.
    /// </summary>
    private int Saltear()
    {
        ushort siguiente = _flashPalabras[_pc & MASCARA_PC];
        bool dosPalabras = (siguiente & 0xFE0C) == 0x940C     // JMP / CALL
                        || (siguiente & 0xFC0F) == 0x9000;    // LDS / STS
        _pc = (_pc + (dosPalabras ? 2 : 1)) & MASCARA_PC;
        return dosPalabras ? 2 : 1;
    }

    /// <summary>Lee un par de registros como palabra de 16 bits (X = r27:r26, Y = r29:r28, Z = r31:r30).</summary>
    private ushort LeerPar(int registro) => (ushort)(_datos[registro] | (_datos[registro + 1] << 8));

    private void EscribirPar(int registro, ushort valor)
    {
        _datos[registro] = (byte)valor;
        _datos[registro + 1] = (byte)(valor >> 8);
    }
}
