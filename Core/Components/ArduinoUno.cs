using System.Diagnostics;
using System.Text;
using ElectricSim.Core.Arduino;

namespace ElectricSim.Core.Components;

/// <summary>
/// La placa Arduino UNO completa, como componente del circuito.
///
/// QUÉ ES para el solver: una fuente de 5V (hereda de FuenteBase: PinA = borne
/// 5V, PinB = borne GND) que además aporta un montón de aristas internas
/// (IComponenteCompuesto) y bornes conectables extra (IComponenteMultiBorne):
///
///   - 3V3: un regulador interno de 3.3V (otra FuenteBase anidada).
///   - GND2 y GND3: los otros dos GND de la placa real (fila POWER y fila
///     digital, junto a AREF), puenteados al principal.
///   - IOREF: referencia de voltaje de E/S; en el UNO es el mismo riel de 5V.
///   - SDA / SCL: en el UNO R3 están unidos físicamente a A4 / A5 (el
///     periférico I2C/TWI del chip no está emulado: solo la conexión).
///   - AREF: entrada de referencia externa del ADC (32kΩ a GND, como el chip).
///   - RESET: con pull-up de 10kΩ a 5V; llevarlo a GND mantiene la CPU en
///     reset y al soltarlo el programa arranca de nuevo.
///   - VIN: entrada de alimentación externa (7–12V recomendados). La placa
///     sigue alimentada por USB en la simulación; VIN consume la corriente
///     de reposo de la placa y la quema si supera 20V.
///   - D0–D13 y A0–A5: los 20 pines de señal. Cada uno se modela con DOS
///     resistencias internas que van del borne del pin al rail de 5V (el
///     "driver alto") y a GND (el "driver bajo"):
///
///         · SALIDA en HIGH  → alto = 28Ω, bajo = abierto  (empuja ~5V)
///         · SALIDA en LOW   → alto = abierto, bajo = 28Ω  (chupa a 0V)
///         · SALIDA con PWM  → las dos conducen en proporción al duty:
///           el equivalente de Thevenin exacto del tren de pulsos promediado
///           (V = 5·duty, R = 28Ω) — lo que un LED o un motor "ven".
///         · ENTRADA         → las dos abiertas (pin flotante)
///         · INPUT_PULLUP    → 35kΩ al rail de 5V, como el chip real.
///
/// Los 28Ω son la resistencia de salida real de un pin de ATmega328P: de ahí
/// sale gratis que un pin en corto a GND entregue ~178mA, muy por encima de
/// los 40mA de máxima absoluta del datasheet → el pin se DAÑA (para siempre,
/// como en la vida real) sin que muera la placa entera. Un corto del 3V3
/// quema SOLO al regulador de 3.3V (la placa sigue, sin ese riel — como en
/// un Uno real); el corto del riel de 5V, ese sí, mata la placa completa.
///
/// QUÉ CONTIENE además: un ATmega328P emulado de verdad (ver Core/Arduino/)
/// que ejecuta el .hex compilado por el IDE de Arduino, ciclo a ciclo, a
/// 16MHz de tiempo simulado. El puente entre ambos mundos es simple y
/// honesto: antes de cada frame la placa le muestra al chip los VOLTAJES
/// REALES de sus bornes (para digitalRead/analogRead/interrupciones), el
/// chip ejecuta su tajada de ciclos, y al terminar la placa traduce el
/// estado de cada pin (salida/entrada/duty) a las resistencias de arriba.
/// </summary>
public class ArduinoUno : FuenteBase, IComponenteCompuesto, IComponenteMultiBorne
{
    public override TipoCorriente Tipo => TipoCorriente.DC;

    // ------------------------------------------------------------------
    //  El cerebro
    // ------------------------------------------------------------------

    private readonly Atmega328P _chip = new();

    /// <summary>Los nombres de los 20 pines de señal, en orden de índice.</summary>
    public static readonly string[] NombresPines =
    {
        "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7",
        "D8", "D9", "D10", "D11", "D12", "D13",
        "A0", "A1", "A2", "A3", "A4", "A5",
    };

    // ------------------------------------------------------------------
    //  Bornes y aristas internas
    // ------------------------------------------------------------------

    private readonly Pin[] _bornesGpio = new Pin[Atmega328P.CantidadPines];
    private readonly Pin _borneGnd2;
    private readonly Pin _borne3v3;

    // Bornes del encabezado R3 agregados después: van SIEMPRE al final de
    // BornesExtra (el guardado referencia los extra por índice "X#").
    private readonly Pin _borneGnd3;
    private readonly Pin _borneAref;
    private readonly Pin _borneSda;
    private readonly Pin _borneScl;
    private readonly Pin _borneIoref;
    private readonly Pin _borneReset;
    private readonly Pin _borneVin;

    private readonly AristaDriver[] _driverAlto = new AristaDriver[Atmega328P.CantidadPines];
    private readonly AristaDriver[] _driverBajo = new AristaDriver[Atmega328P.CantidadPines];
    private readonly Rail3V3 _rail3v3;
    private readonly Wire _puenteGnd;
    private readonly Wire _puenteGnd3;
    private readonly Wire _puenteIoref;
    private readonly Wire _puenteSda;
    private readonly Wire _puenteScl;
    private readonly AristaDriver _pullupReset;
    private readonly AristaDriver _entradaAref;
    private readonly AristaDriver _consumoVin;

    /// <summary>Resistencia de salida de un pin del ATmega328P (Ohms). De acá sale la caída de voltaje bajo carga y la corriente de cortocircuito (~178mA).</summary>
    public const double ResistenciaDriver = 28;

    /// <summary>Resistencia del pull-up interno (Ohms), dentro del rango 20k–50k del datasheet.</summary>
    public const double ResistenciaPullup = 35_000;

    /// <summary>Pull-up del pin RESET en la placa UNO (Ohms).</summary>
    public const double ResistenciaPullupReset = 10_000;

    /// <summary>Resistencia de entrada del pin AREF (Ohms), valor típico del datasheet.</summary>
    public const double ResistenciaEntradaAref = 32_000;

    /// <summary>Por debajo de este voltaje el pin RESET mantiene la CPU reiniciada (V).</summary>
    public const double UmbralReset = 1.5;

    /// <summary>Voltaje en VIN a partir del cual el regulador de la placa empieza a trabajar (V).</summary>
    public const double VinMinimoRegulador = 6.5;

    /// <summary>Corriente de reposo que la placa toma de VIN cuando se la alimenta por ahí (A).</summary>
    public const double CorrienteReposoVin = 0.045;

    /// <summary>Voltaje máximo en VIN antes de quemar el regulador de la placa (V). ∞ = sin límite (modo sandbox).</summary>
    public double VoltajeMaximoVin { get; set; } = 20;

    // ------------------------------------------------------------------
    //  Estado eléctrico por pin
    // ------------------------------------------------------------------

    /// <summary>
    /// Corriente máxima que un pin aguanta antes de dañarse (Amps). El
    /// datasheet dice 40mA absolutos; con ∞ el pin se vuelve indestructible
    /// (modo sandbox).
    /// </summary>
    public double LimiteCorrientePin { get; set; } = 0.04;

    /// <summary>Corriente que ESTÁ SALIENDO de cada pin hacia el circuito (negativa = entrando), medida en el último tick.</summary>
    public double[] CorrientePorPin { get; } = new double[Atmega328P.CantidadPines];

    /// <summary>Pines dañados por sobrecorriente. Un pin dañado queda abierto para siempre (hay que reponer la placa, como en la realidad).</summary>
    public IReadOnlyList<bool> PinesDanados => _chip.PinDanado;

    private readonly double[] _segundosSobrecorriente = new double[Atmega328P.CantidadPines];

    /// <summary>Cuántos segundos seguidos de sobrecorriente aguanta un pin antes de dañarse (independiente del tick-rate).</summary>
    private const double SegundosParaDanarPin = 0.3;
    private readonly double[] _voltajesMuestreados = new double[Atmega328P.CantidadPines];

    // --- Pines de control (RESET, AREF, VIN) ---
    private int _ticksResetBajo;
    private bool _resetSostenido;
    private double _segundosSobretensionVin;
    private bool _vinAlimentando;

    /// <summary>True mientras el pin RESET está por debajo de UmbralReset (la CPU queda reiniciada).</summary>
    public bool ResetSostenido => _resetSostenido;

    /// <summary>Voltaje medido en el pin VIN en el último tick (V).</summary>
    public double VoltajeVin { get; private set; }

    /// <summary>Voltaje medido en el pin AREF en el último tick (V).</summary>
    public double VoltajeAref { get; private set; }

    /// <summary>Voltaje medido en el pin RESET en el último tick (V).</summary>
    public double VoltajeReset { get; private set; }

    // ------------------------------------------------------------------
    //  Tiempo real
    // ------------------------------------------------------------------

    /// <summary>Frecuencia del cristal (MHz). El Uno real corre a 16; bajarla hace todo más lento (útil para VER un PWM, por ejemplo).</summary>
    public double FrecuenciaMHz
    {
        get => _frecuenciaMHz;
        set => _frecuenciaMHz = Math.Clamp(value, 0.1, 32);
    }
    private double _frecuenciaMHz = 16;

    /// <summary>Pausa la CPU sin resetearla (los pines quedan congelados como están).</summary>
    public bool CpuEnPausa { get; set; }

    /// <summary>
    /// Milisegundos de CPU REAL que la placa se permite gastar por frame para
    /// emular. Si el sketch es muy pesado para llegar a 16MHz en ese
    /// presupuesto, la placa corre en "cámara lenta" (ver VelocidadReal) en
    /// vez de congelar el juego.
    /// </summary>
    public double PresupuestoMsPorFrame { get; set; } = 6;

    /// <summary>1.0 = el chip está corriendo a velocidad real; 0.5 = a la mitad (el emulador no llega y el tiempo del sketch pasa más despacio).</summary>
    public double VelocidadReal { get; private set; } = 1;

    private double _restoCiclos;
    private readonly Stopwatch _cronometro = new();

    // ------------------------------------------------------------------
    //  Serie (el Monitor Serie del IDE)
    // ------------------------------------------------------------------

    private readonly StringBuilder _salidaSerie = new();
    private const int MaxCaracteresSerie = 4000;

    /// <summary>Todo lo que el sketch imprimió por Serial (los últimos ~4000 caracteres).</summary>
    public string SalidaSerie => _salidaSerie.ToString();

    /// <summary>Cuántos caracteres llegaron por Serial en total (para que el inspector sepa si hay novedades).</summary>
    public long CaracteresSerieRecibidos { get; private set; }

    // =====================================================================
    //  Construcción
    // =====================================================================

    public ArduinoUno(string name) : base(name, 5.0)
    {
        // La placa entera como fuente de 5V: el USB/regulador real aguanta
        // alrededor de 1A antes de morir (el polyfuse salta antes, pero acá
        // el fusible sos vos).
        MaxCurrent = 1.0;
        ResistenciaInternaNominal = 0.35;

        // Térmica de una placa: masa razonable, disipa bien.
        CapacidadTermica = 18;
        CoeficienteEnfriamiento = 0.05;
        MaxTemperature = 85;

        PinA.Polaridad = Polaridad.Positivo;  // borne 5V
        PinB.Polaridad = Polaridad.Negativo;  // borne GND

        // --- Bornes extra ---
        _borne3v3 = new Pin(this, $"{name}.3V3") { Polaridad = Polaridad.Positivo };
        _borneGnd2 = new Pin(this, $"{name}.GND2") { Polaridad = Polaridad.Negativo };
        for (int i = 0; i < _bornesGpio.Length; i++)
        {
            _bornesGpio[i] = new Pin(this, $"{name}.{NombresPines[i]}") { Polaridad = Polaridad.Senal };
        }
        _borneGnd3 = new Pin(this, $"{name}.GND3") { Polaridad = Polaridad.Negativo };
        _borneAref = new Pin(this, $"{name}.AREF") { Polaridad = Polaridad.Senal };
        _borneSda = new Pin(this, $"{name}.SDA") { Polaridad = Polaridad.Senal };
        _borneScl = new Pin(this, $"{name}.SCL") { Polaridad = Polaridad.Senal };
        _borneIoref = new Pin(this, $"{name}.IOREF") { Polaridad = Polaridad.Positivo };
        _borneReset = new Pin(this, $"{name}.RESET") { Polaridad = Polaridad.Senal };
        _borneVin = new Pin(this, $"{name}.VIN") { Polaridad = Polaridad.Positivo };

        // --- Aristas internas ---
        _rail3v3 = new Rail3V3($"{name}.Regulador3V3", _borne3v3, PinB);
        _puenteGnd = new Wire(PinB, _borneGnd2, $"{name}.PuenteGND");
        for (int i = 0; i < _bornesGpio.Length; i++)
        {
            _driverAlto[i] = new AristaDriver($"{name}.{NombresPines[i]}↑", PinA, _bornesGpio[i]);
            _driverBajo[i] = new AristaDriver($"{name}.{NombresPines[i]}↓", _bornesGpio[i], PinB);
        }

        // Puentes físicos de la placa R3: GND3 = GND, IOREF = 5V, SDA = A4, SCL = A5.
        _puenteGnd3 = new Wire(PinB, _borneGnd3, $"{name}.PuenteGND3");
        _puenteIoref = new Wire(PinA, _borneIoref, $"{name}.PuenteIOREF");
        _puenteSda = new Wire(_bornesGpio[18], _borneSda, $"{name}.PuenteSDA");
        _puenteScl = new Wire(_bornesGpio[19], _borneScl, $"{name}.PuenteSCL");

        // Pines de control con su carga interna real.
        _pullupReset = new AristaDriver($"{name}.PullupRESET", PinA, _borneReset);
        _pullupReset.FijarResistencia(ResistenciaPullupReset);
        _entradaAref = new AristaDriver($"{name}.EntradaAREF", _borneAref, PinB);
        _entradaAref.FijarResistencia(ResistenciaEntradaAref);
        _consumoVin = new AristaDriver($"{name}.ConsumoVIN", _borneVin, PinB);

        _chip.SerialTransmitido += RecibirByteSerie;

        // La placa real transmite a la velocidad del baudrate: el monitor
        // serie muestra el texto saliendo a ritmo (bajá la frecuencia del
        // reloj para VERLO despacio). El emulador pelado queda instantáneo.
        _chip.TransmisionInstantanea = false;
    }

    public IEnumerable<Component> AristasInternas
    {
        get
        {
            yield return _puenteGnd;
            yield return _rail3v3;
            for (int i = 0; i < _driverAlto.Length; i++)
            {
                yield return _driverAlto[i];
                yield return _driverBajo[i];
            }
            yield return _puenteGnd3;
            yield return _puenteIoref;
            yield return _puenteSda;
            yield return _puenteScl;
            yield return _pullupReset;
            yield return _entradaAref;
            yield return _consumoVin;
        }
    }

    /// <summary>
    /// Los bornes conectables además de 5V (PinA) y GND (PinB). ORDEN FIJO:
    /// los proyectos guardados referencian estos bornes por su posición
    /// ("X0", "X1"...), así que los nuevos se agregan SIEMPRE al final.
    /// </summary>
    public IEnumerable<(string Etiqueta, Pin Pin)> BornesExtra
    {
        get
        {
            yield return ("3V3", _borne3v3);
            yield return ("GND2", _borneGnd2);
            for (int i = 0; i < _bornesGpio.Length; i++)
            {
                yield return (NombresPines[i], _bornesGpio[i]);
            }
            yield return ("GND3", _borneGnd3);
            yield return ("AREF", _borneAref);
            yield return ("SDA", _borneSda);
            yield return ("SCL", _borneScl);
            yield return ("IOREF", _borneIoref);
            yield return ("RESET", _borneReset);
            yield return ("VIN", _borneVin);
        }
    }

    // =====================================================================
    //  Programa
    // =====================================================================

    /// <summary>¿Hay un .hex cargado?</summary>
    public bool ProgramaCargado => _chip.ProgramaCargado;

    /// <summary>Nombre y tamaño del programa cargado ("blink.hex · 924 bytes"), o vacío.</summary>
    public string InfoPrograma => _chip.InfoPrograma;

    /// <summary>
    /// El TEXTO del último .hex cargado, tal cual entró. La placa lo recuerda
    /// para que el sistema de guardado pueda restaurar el programa al abrir
    /// un proyecto (el chip solo conserva la flash decodificada).
    /// </summary>
    public string TextoHexCargado { get; private set; } = "";

    /// <summary>Nombre del archivo del programa cargado ("blink.hex"), para restaurarlo igual.</summary>
    public string NombrePrograma { get; private set; } = "";

    /// <summary>
    /// Carga un programa compilado (el contenido de un archivo .hex exportado
    /// por el IDE de Arduino con "Sketch → Exportar binario compilado") y
    /// arranca la CPU desde cero. Tira FormatException con mensaje claro si
    /// el archivo no es un Intel HEX válido.
    /// </summary>
    public void CargarPrograma(string contenidoHex, string nombre = "programa")
    {
        if (IsBurnt) return; // una placa quemada no revive con software
        _chip.CargarHex(contenidoHex, nombre);
        // Recién ACÁ (si el hex era válido y no tiró excepción) lo recordamos.
        TextoHexCargado = contenidoHex;
        NombrePrograma = nombre;
        _salidaSerie.Clear();
        CaracteresSerieRecibidos = 0;
        VelocidadReal = 1;
        _restoCiclos = 0;
        AplicarSalidasAlCircuito();
    }

    /// <summary>Como apretar el botón RESET de la placa: el programa arranca de nuevo (la EEPROM y los pines dañados quedan como estaban).</summary>
    public void Reiniciar()
    {
        if (IsBurnt) return;
        _chip.Reset();
        _salidaSerie.Clear();
        CaracteresSerieRecibidos = 0;
        _restoCiclos = 0;
        AplicarSalidasAlCircuito();
    }

    /// <summary>Estado de la CPU en lenguaje claro, para el inspector.</summary>
    public string EstadoCpu
    {
        get
        {
            if (IsBurnt) return "placa quemada";
            if (!_chip.ProgramaCargado) return "sin programa";
            if (_resetSostenido) return "en reset (pin RESET conectado a GND)";
            if (_chip.Detenida) return $"detenida: {_chip.MotivoDetencion}";
            if (CpuEnPausa) return "en pausa";
            return VelocidadReal > 0.97 ? $"ejecutando a {_frecuenciaMHz:0.#} MHz (velocidad real)"
                 : $"ejecutando al {VelocidadReal * 100:F0}% de la velocidad real ({_frecuenciaMHz:0.#} MHz)";
        }
    }

    /// <summary>True si la CPU se detuvo por un error de hardware o instrucción BREAK.</summary>
    public bool CpuDetenida => _chip.Detenida;

    /// <summary>Motivo por el que se detuvo la CPU (vacío si sigue activa).</summary>
    public string MotivoDetencionCpu => _chip.MotivoDetencion;

    /// <summary>Referencia del ADC elegida por el programa, para el inspector.</summary>
    public string ReferenciaAdcTexto
    {
        get
        {
            double avcc = VoltajeSeguro(PinA) - VoltajeSeguro(PinB);
            return _chip.ReferenciaAdc switch
            {
                0 => $"externa en AREF ({VoltajeAref:F2} V)",
                3 => "interna de 1.1 V",
                _ => $"AVcc ({(avcc > 0.5 ? avcc : Voltage):F2} V)",
            };
        }
    }

    /// <summary>Cómo se alimenta la placa ahora mismo, para el inspector.</summary>
    public string EstadoAlimentacion
    {
        get
        {
            if (IsBurnt) return "sin alimentación (placa quemada)";
            if (VoltajeVin < -0.5) return $"USB 5 V · VIN con polaridad invertida ({VoltajeVin:F1} V, bloqueada por el diodo de protección)";
            if (_vinAlimentando) return $"USB 5 V + VIN {VoltajeVin:F1} V (el regulador consume {CorrienteReposoVin * 1000:F0} mA)";
            if (VoltajeVin > 0.5) return $"USB 5 V · VIN {VoltajeVin:F1} V (insuficiente: el regulador necesita al menos {VinMinimoRegulador:F1} V)";
            return "USB 5 V";
        }
    }

    /// <summary>Estado del regulador de 3.3 V, para el inspector.</summary>
    public string EstadoRegulador3V3 =>
        IsBurnt ? "sin alimentación"
        : _rail3v3.IsBurnt ? $"QUEMADO ({_rail3v3.MotivoQuemado}): el pin 3V3 no entrega voltaje"
        : $"funcionando · {Math.Abs(_rail3v3.CorrienteEntregada) * 1000:F1} mA de {_rail3v3.MaxCurrent * 1000:F0} mA máx.";

    /// <summary>Manda texto al sketch como si lo tipearas en el Monitor Serie (con salto de línea al final, como el monitor real).</summary>
    public void EnviarSerie(string texto) => _chip.SerialRecibir(texto + "\n");

    private void RecibirByteSerie(byte b)
    {
        CaracteresSerieRecibidos++;
        char c = (char)b;
        if (c == '\r') return; // los println mandan \r\n; con \n alcanza
        _salidaSerie.Append(c);
        if (_salidaSerie.Length > MaxCaracteresSerie)
        {
            _salidaSerie.Remove(0, _salidaSerie.Length - MaxCaracteresSerie);
        }
    }

    // =====================================================================
    //  El puente tiempo real → chip (lo llama Simulacion una vez por frame)
    // =====================================================================

    /// <summary>
    /// Avanza el microcontrolador el tiempo de juego transcurrido: le muestra
    /// las entradas medidas en el último tick eléctrico, ejecuta los ciclos
    /// que corresponden a la frecuencia configurada (con tope de presupuesto
    /// de CPU real) y traduce el estado resultante de los pines al modelo
    /// eléctrico. Si el presupuesto no alcanza, el chip corre en cámara
    /// lenta y VelocidadReal lo refleja.
    /// </summary>
    public void AvanzarTiempoReal(double deltaSegundos)
    {
        if (IsBurnt || !_chip.ProgramaCargado)
        {
            _chip.Ejecutar(0); // cierra el lote para que los pines queden congelados como están
            AplicarSalidasAlCircuito();
            return;
        }

        // Pin RESET en bajo: la CPU queda reiniciada (todos los pines como
        // entradas) hasta que se suelte; al soltarlo arranca desde cero.
        // Un reset por hardware también recupera una CPU detenida.
        if (_resetSostenido)
        {
            _chip.Reset();
            _restoCiclos = 0;
            AplicarSalidasAlCircuito();
            return;
        }

        if (_chip.Detenida || CpuEnPausa)
        {
            _chip.Ejecutar(0);
            AplicarSalidasAlCircuito();
            return;
        }

        _chip.FijarEntradas(_voltajesMuestreados);

        // ¿Cuántos ciclos "tocan" en este frame? (guardando la fracción para el próximo)
        double deseadosExactos = Math.Min(deltaSegundos, 0.1) * _frecuenciaMHz * 1_000_000 + _restoCiclos;
        long deseados = (long)deseadosExactos;
        _restoCiclos = deseadosExactos - deseados;
        if (deseados <= 0)
        {
            AplicarSalidasAlCircuito();
            return;
        }

        // Ejecutar en tandas, vigilando el presupuesto de tiempo real.
        _cronometro.Restart();
        long ejecutados = 0;
        while (ejecutados < deseados)
        {
            long tanda = Math.Min(50_000, deseados - ejecutados);
            ejecutados += _chip.Ejecutar(tanda);
            if (_chip.Detenida) break;
            if (_cronometro.Elapsed.TotalMilliseconds > PresupuestoMsPorFrame) break;
        }

        // Los ciclos que no entraron se DESCARTAN (no se acumulan como deuda):
        // el resultado es cámara lenta estable, no un espiral de atraso.
        double velocidadInstantanea = Math.Clamp(ejecutados / (double)deseados, 0, 1);
        VelocidadReal = VelocidadReal * 0.85 + velocidadInstantanea * 0.15;

        AplicarSalidasAlCircuito();
    }

    /// <summary>Traduce el estado de cada pin del chip (entrada/salida/duty/pull-up) a las resistencias de sus dos drivers.</summary>
    private void AplicarSalidasAlCircuito()
    {
        for (int i = 0; i < _bornesGpio.Length; i++)
        {
            if (_chip.PinDanado[i])
            {
                _driverAlto[i].FijarResistencia(CircuitConstants.OpenResistance);
                _driverBajo[i].FijarResistencia(CircuitConstants.OpenResistance);
                continue;
            }

            EstadoPinChip estado = _chip.Pines[i];

            if (!estado.EsSalida)
            {
                _driverAlto[i].FijarResistencia(estado.PullupActivo
                    ? ResistenciaPullup
                    : CircuitConstants.OpenResistance);
                _driverBajo[i].FijarResistencia(CircuitConstants.OpenResistance);
                continue;
            }

            // Salida: el equivalente de Thevenin del tren de pulsos. Con duty
            // intermedio, las DOS resistencias conducen (28/d y 28/(1−d)):
            // el divisor resultante da exactamente V = 5·duty con R = 28Ω.
            double duty = estado.Duty;
            if (duty < 0.02) duty = 0;        // recortar los extremos: un pin
            if (duty > 0.98) duty = 1;        // "casi siempre alto" ES alto

            _driverAlto[i].FijarResistencia(duty <= 0
                ? CircuitConstants.OpenResistance
                : ResistenciaDriver / duty);
            _driverBajo[i].FijarResistencia(duty >= 1
                ? CircuitConstants.OpenResistance
                : ResistenciaDriver / (1 - duty));
        }
    }

    // =====================================================================
    //  Después de cada tick eléctrico: medir, dañar pines, alimentar el ADC
    // =====================================================================

    public override void PostSolve(double deltaTime)
    {
        base.PostSolve(deltaTime); // corriente entregada, cortocircuito, térmica y límites de la fuente de 5V

        _chip.TemperaturaChip = Temperatura;
        MedirPinesDeControl(deltaTime);

        double vGnd = VoltajeSeguro(PinB);
        for (int i = 0; i < _bornesGpio.Length; i++)
        {
            // El voltaje REAL del borne respecto al GND de la placa:
            // esto es lo que digitalRead y analogRead van a ver.
            _voltajesMuestreados[i] = VoltajeSeguro(_bornesGpio[i]) - vGnd;

            // Corriente neta saliendo del pin: lo que entra desde el rail de
            // 5V menos lo que se escapa hacia GND por el driver bajo.
            double corriente = _driverAlto[i].Current - _driverBajo[i].Current;
            CorrientePorPin[i] = IsBurnt ? 0 : corriente;

            if (IsBurnt || _chip.PinDanado[i]) continue;

            // ¿Sobrecorriente sostenida? Un tercio de segundo por encima del
            // límite y el pin muere — la placa sigue viva (así se rompen de
            // verdad). En segundos, no en ticks: un roce breve perdona.
            if (!double.IsInfinity(LimiteCorrientePin) && Math.Abs(corriente) > LimiteCorrientePin)
            {
                _segundosSobrecorriente[i] += deltaTime;
                if (_segundosSobrecorriente[i] >= SegundosParaDanarPin)
                {
                    DanarPin(i);
                }
            }
            else
            {
                _segundosSobrecorriente[i] = 0;
            }
        }
    }

    private void DanarPin(int indice)
    {
        _chip.PinDanado[indice] = true;
        _driverAlto[indice].FijarResistencia(CircuitConstants.OpenResistance);
        _driverBajo[indice].FijarResistencia(CircuitConstants.OpenResistance);
    }

    private static double VoltajeSeguro(Pin pin) => double.IsNaN(pin.Voltage) ? 0 : pin.Voltage;

    /// <summary>
    /// Lee RESET, AREF, VIN y el riel de 5V después de cada tick eléctrico:
    /// alimenta las referencias del ADC, decide si la CPU queda en reset,
    /// ajusta el consumo de VIN y quema la placa si VIN se pasa de su máximo.
    /// </summary>
    private void MedirPinesDeControl(double deltaTime)
    {
        double vGnd = VoltajeSeguro(PinB);
        VoltajeReset = VoltajeSeguro(_borneReset) - vGnd;
        VoltajeAref = VoltajeSeguro(_borneAref) - vGnd;
        VoltajeVin = VoltajeSeguro(_borneVin) - vGnd;

        if (IsBurnt)
        {
            _resetSostenido = false;
            _ticksResetBajo = 0;
            _vinAlimentando = false;
            _consumoVin.FijarResistencia(CircuitConstants.OpenResistance);
            return;
        }

        // Referencias del ADC: AREF medido y AVcc = riel real de 5V.
        _chip.VoltajeAref = Math.Max(0, VoltajeAref);
        double avcc = VoltajeSeguro(PinA) - VoltajeSeguro(PinB);
        _chip.VoltajeAvcc = avcc > 0.5 ? avcc : 5.0;

        // RESET: dos ticks seguidos por debajo del umbral, para que un
        // reacomodo momentáneo del circuito no reinicie el programa.
        if (VoltajeReset < UmbralReset)
        {
            _ticksResetBajo++;
            if (_ticksResetBajo >= 2) _resetSostenido = true;
        }
        else
        {
            _ticksResetBajo = 0;
            _resetSostenido = false;
        }

        // VIN: con histéresis, el regulador consume su corriente de reposo
        // mientras haya voltaje suficiente para trabajar.
        if (_vinAlimentando ? VoltajeVin < VinMinimoRegulador - 0.5 : VoltajeVin >= VinMinimoRegulador)
        {
            _vinAlimentando = !_vinAlimentando;
        }
        _consumoVin.FijarResistencia(_vinAlimentando
            ? Math.Max(VoltajeVin, VinMinimoRegulador) / CorrienteReposoVin
            : CircuitConstants.OpenResistance);

        // VIN por encima del máximo del regulador: la placa se quema.
        if (!double.IsInfinity(VoltajeMaximoVin) && VoltajeVin > VoltajeMaximoVin)
        {
            _segundosSobretensionVin += deltaTime;
            if (_segundosSobretensionVin >= SegundosParaDanarPin)
            {
                Quemar($"sobretensión en VIN (máx. {VoltajeMaximoVin:0.#} V)");
            }
        }
        else
        {
            _segundosSobretensionVin = 0;
        }
    }

    // =====================================================================
    //  Daños parciales (pines y regulador de 3.3V) y su persistencia
    // =====================================================================

    /// <summary>True si el regulador de 3.3V está quemado (la placa puede seguir viva).</summary>
    public bool Regulador3V3Quemado => _rail3v3.IsBurnt;

    /// <summary>True si hay pines dañados o el regulador de 3.3V quemado, con la placa viva.</summary>
    public bool TieneDanosParciales => !IsBurnt && (PinesDanadosTotal > 0 || _rail3v3.IsBurnt);

    /// <summary>
    /// Repara los pines dañados y el regulador de 3.3V sin tocar la CPU ni el
    /// programa (equivale a cambiar el chip y el regulador por unos nuevos).
    /// </summary>
    public void RepararDanosParciales()
    {
        if (IsBurnt) return;
        if (_rail3v3.IsBurnt) _rail3v3.RevivirPorDueno(Temperatura);
        Array.Clear(_chip.PinDanado, 0, _chip.PinDanado.Length);
        Array.Clear(_segundosSobrecorriente, 0, _segundosSobrecorriente.Length);
        AplicarSalidasAlCircuito();
    }

    /// <summary>Pines dañados como máscara de bits (bit i = pin i de NombresPines). La usa el guardado.</summary>
    public int MascaraPinesDanados
    {
        get
        {
            int mascara = 0;
            for (int i = 0; i < _chip.PinDanado.Length; i++)
            {
                if (_chip.PinDanado[i]) mascara |= 1 << i;
            }
            return mascara;
        }
    }

    /// <summary>Restaura los pines dañados guardados en un proyecto (ver MascaraPinesDanados).</summary>
    public void RestaurarPinesDanados(int mascara)
    {
        for (int i = 0; i < _chip.PinDanado.Length; i++)
        {
            if ((mascara & (1 << i)) != 0) DanarPin(i);
        }
    }

    /// <summary>Marca el regulador de 3.3V como quemado al abrir un proyecto que lo tenía así.</summary>
    public void RestaurarRegulador3V3Quemado(string motivo)
    {
        if (!IsBurnt) _rail3v3.QuemarPorCarga(string.IsNullOrEmpty(motivo) ? "cortocircuito" : motivo);
    }

    /// <summary>Motivo por el que se quemó el regulador de 3.3V (vacío si funciona).</summary>
    public string MotivoRegulador3V3 => _rail3v3.MotivoQuemado;

    /// <summary>Voltaje actual de un pin de señal (para la capa visual, por ejemplo el LED "L").</summary>
    public double VoltajePin(int indice) => _voltajesMuestreados[indice];

    /// <summary>Estado crudo de un pin del chip (salida/duty/pull-up), para la capa visual.</summary>
    public EstadoPinChip EstadoPin(int indice) => _chip.Pines[indice];

    /// <summary>Texto de estado de un pin para el inspector ("D13 · salida · 100% · 12.4mA saliendo").</summary>
    public string DescribirPin(int indice)
    {
        if (_chip.PinDanado[indice]) return $"{NombresPines[indice]} · DAÑADO por sobrecorriente";

        EstadoPinChip estado = _chip.Pines[indice];
        double corriente = CorrientePorPin[indice];
        string flujo = Math.Abs(corriente) < 0.0005
            ? "sin corriente"
            : $"{Math.Abs(corriente) * 1000:F1}mA {(corriente > 0 ? "saliendo" : "entrando")}";

        if (!estado.EsSalida)
        {
            string modo = estado.PullupActivo ? "entrada con pull-up" : "entrada";
            return $"{NombresPines[indice]} · {modo} · lee {_voltajesMuestreados[indice]:F2}V · {flujo}";
        }
        string nivel = estado.Duty >= 0.99 ? "ALTO (5V)"
                     : estado.Duty <= 0.01 ? "BAJO (0V)"
                     : $"PWM {estado.Duty * 100:F0}% (ciclo de trabajo promedio)";
        return $"{NombresPines[indice]} · salida · {nivel} · {flujo}";
    }

    /// <summary>Cantidad de pines dañados (lectura rápida para el inspector).</summary>
    public int PinesDanadosTotal
    {
        get
        {
            int total = 0;
            foreach (bool danado in _chip.PinDanado)
            {
                if (danado) total++;
            }
            return total;
        }
    }

    // =====================================================================
    //  Muerte de la placa
    // =====================================================================

    /// <summary>
    /// Cuando la placa muere (corto del rail de 5V o del 3V3, sobrecorriente
    /// total, temperatura), muere TODO: el regulador interno, los drivers de
    /// los pines y la CPU. El orden importa: primero las tripas (que dejan
    /// el GND compartido como estaba) y al final la base, que suelta los
    /// bornes fijos de la fuente principal.
    /// </summary>
    protected override void Quemar()
    {
        _rail3v3.MatarPorDueno();
        for (int i = 0; i < _driverAlto.Length; i++)
        {
            _driverAlto[i].MatarPorDueno();
            _driverBajo[i].MatarPorDueno();
        }
        _pullupReset.MatarPorDueno();
        _entradaAref.MatarPorDueno();
        _consumoVin.MatarPorDueno();
        _resetSostenido = false;
        _ticksResetBajo = 0;
        _vinAlimentando = false;
        _chip.DetenerPorHardware("la placa se quemó");
        base.Quemar();
    }

    /// <summary>
    /// Reparar la placa la deja como nueva: la fuente principal vuelve a fijar
    /// 5V/GND (base.Reparado), reviven el regulador de 3.3V y los drivers de
    /// cada pin, la CPU se des-frena (resume donde estaba, con el programa
    /// intacto) y se borran los pines dañados. El orden espeja al de Quemar.
    /// </summary>
    protected override void Reparado()
    {
        base.Reparado();
        _rail3v3.RevivirPorDueno(Temperatura);
        for (int i = 0; i < _driverAlto.Length; i++)
        {
            _driverAlto[i].RevivirPorDueno(Temperatura);
            _driverBajo[i].RevivirPorDueno(Temperatura);
            _segundosSobrecorriente[i] = 0;
        }
        _pullupReset.RevivirPorDueno(Temperatura);
        _entradaAref.RevivirPorDueno(Temperatura);
        _consumoVin.RevivirPorDueno(Temperatura);
        _segundosSobretensionVin = 0;
        _chip.RepararHardware();
        AplicarSalidasAlCircuito();
    }

    // =====================================================================
    //  Las tripas: el regulador de 3.3V y las aristas de los drivers
    // =====================================================================

    /// <summary>
    /// El regulador de 3.3V de la placa: una fuente anidada cuyo borne
    /// positivo es el pin 3V3 y cuyo negativo es EL MISMO GND de la placa.
    /// Si lo cortocircuitás, la restricción del regulador (3.3V entre sus
    /// nodos) colapsa sobre un solo nodo: el conflicto es SUYO, y muere él
    /// solo — su corto de ~4A aplasta sus 150mA de tope. La placa sigue
    /// viva, sin riel de 3.3, igual que un Uno real al que le cocinan el
    /// reguladorcito.
    /// </summary>
    private sealed class Rail3V3 : FuenteBase
    {
        public override TipoCorriente Tipo => TipoCorriente.DC;

        public Rail3V3(string name, Pin borne3v3, Pin gnd) : base(name, 3.3, borne3v3, gnd)
        {
            MaxCurrent = 0.15;               // el regulador chico del Uno aguanta ~150mA
            ResistenciaInternaNominal = 2;
            FraccionCalorDisipado = 1;
        }

        /// <summary>Solo fija SU borne positivo: el GND compartido ya lo fija (y lo posee) la placa.</summary>
        public override void FixVoltages()
        {
            PinA.Voltage = Voltage;
        }

        public void MatarPorDueno() => Quemar("se quemó la placa");

        public void QuemarPorCarga(string motivo) => Quemar(motivo);

        public void RevivirPorDueno(double temperatura) => Reparar(temperatura);
        protected override void Quemar()
        {
            bool gndSeguiaFijo = PinB.IsFixed;
            base.Quemar();
            PinB.IsFixed = gndSeguiaFijo; // restaurar: el GND es de la placa, no mío
        }
    }

    /// <summary>
    /// Una de las dos resistencias internas de un pin (al rail de 5V o a
    /// GND). La placa le fija la resistencia cada frame según lo que el chip
    /// esté haciendo con el pin. Sin límites propios: el daño por
    /// sobrecorriente lo administra la placa a nivel de PIN (que es la
    /// unidad que se rompe), y el calor que disipan calienta a la placa por
    /// vecindad térmica.
    /// </summary>
    private sealed class AristaDriver : Component
    {
        private double _resistencia = CircuitConstants.OpenResistance;

        public AristaDriver(string name, Pin a, Pin b) : base(name, a, b)
        {
            MaxVoltage = double.PositiveInfinity;
            MaxCurrent = double.PositiveInfinity;
            MaxPower = double.PositiveInfinity;
            MaxTemperature = double.PositiveInfinity;
        }

        public override double GetResistance() => _resistencia;

        public void FijarResistencia(double ohms) =>
            _resistencia = Math.Max(CircuitConstants.MinResistance, ohms);

        public void MatarPorDueno() => Quemar("se quemó la placa");

        public void RevivirPorDueno(double temperatura) => Reparar(temperatura);
    }
}
