/*
 * Proyecto 17: Estacion de Control y Telemetria Automatica (Simutronik)
 * ----------------------------------------------------------------------
 * Proyecto complejo multiproposito para Arduino UNO R3 (ATmega328P).
 *
 * Conexiones del circuito (coinciden con 17_estacion_control_arduino.json):
 *   - 5V / GND2    -> Extremos A y B del Potenciometro (10 kOhm)
 *   - A0           -> Cursor del Potenciometro (ajusta el umbral / Setpoint)
 *   - D2 (INT0)    -> Pulsador a GND2 (INPUT_PULLUP, alterna Modo Normal / Turbo)
 *   - D4           -> Resistencia 470 Ohm -> Caja con pantalla (+)
 *                     Caja (-) -> Capacitor 2000 uF (+) -> A1 (y Cap - a GND)
 *   - D6 (PWM OC0A)-> Resistencia 220 Ohm -> LED Violeta (latido de estado)
 *   - D7           -> Resistencia 220 Ohm -> LED Azul (nivel >= 25%)
 *   - D8           -> Resistencia 220 Ohm -> LED Esmeralda (nivel >= 50%)
 *   - D9 (PWM OC1A)-> Resistencia 330 Ohm -> Base Transistor NPN (2N2222)
 *                     Bateria 9V (+) -> Motor DC (+R 22 Ohm) y Lampara en paralelo
 *                     Cargas -> Colector NPN | Emisor NPN -> GND comun
 *   - D12          -> Resistencia 220 Ohm -> LED Ambar (nivel >= 75%)
 *   - D13          -> Resistencia 220 Ohm -> LED Rojo (Alarma: RC >= Setpoint o Turbo)
 *
 * Como compilar desde Arduino IDE:
 *   1. Abrir este archivo en Arduino IDE (Placa: "Arduino Uno").
 *   2. Menu: Programa -> Exportar binario compilado (Ctrl+Alt+S).
 *   3. Cargar el archivo .hex resultante en el Arduino UNO de Simutronik.
 */

const int PIN_BOTON     = 2;   // Interrupcion externa INT0
const int PIN_RC_CTRL   = 4;   // Salida de carga/descarga del tanque RC
const int PIN_LED_PWM   = 6;   // PWM Timer0 OC0A (latido)
const int PIN_LED_25    = 7;   // Barra nivel 25%
const int PIN_LED_50    = 8;   // Barra nivel 50%
const int PIN_MOTOR_PWM = 9;   // PWM Timer1 OC1A (potencia Transistor NPN)
const int PIN_LED_75    = 12;  // Barra nivel 75%
const int PIN_LED_ALM   = 13;  // Alarma / Modo Turbo

const int RC_MIN_ADC = 205;    // ~1.00 V (inicio de carga)
const int RC_MAX_ADC = 820;    // ~4.00 V (inicio de descarga)

volatile bool botonPendiente = false;
volatile unsigned long ultimaIsrMs = 0;

bool modoTurbo = false;
bool cargandoRc = true;

byte faseOnda = 0;
unsigned long ultimoPasoMs = 0;
unsigned long ultimoSerialMs = 0;

void alPresionarBotonISR() {
  unsigned long ahora = millis();
  if ((unsigned long)(ahora - ultimaIsrMs) >= 180UL) {
    botonPendiente = true;
    ultimaIsrMs = ahora;
  }
}

void setup() {
  Serial.begin(9600);

  pinMode(PIN_RC_CTRL, OUTPUT);
  digitalWrite(PIN_RC_CTRL, HIGH); // Arranca cargando el tanque RC

  pinMode(PIN_BOTON, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_BOTON), alPresionarBotonISR, FALLING);

  pinMode(PIN_LED_PWM, OUTPUT);
  pinMode(PIN_LED_25, OUTPUT);
  pinMode(PIN_LED_50, OUTPUT);
  pinMode(PIN_MOTOR_PWM, OUTPUT);
  pinMode(PIN_LED_75, OUTPUT);
  pinMode(PIN_LED_ALM, OUTPUT);

  Serial.println("SIMUTRONIK ESTACION v1.0 INICIADA");
}

void loop() {
  unsigned long ahora = millis();

  // 1. Cambio de modo por pulsador (ISR INT0) o comando por Monitor Serie ('m'/'M')
  if (botonPendiente) {
    botonPendiente = false;
    modoTurbo = !modoTurbo;
    Serial.println(modoTurbo ? "EVENTO: MODO TURBO ON" : "EVENTO: MODO NORMAL");
  }

  while (Serial.available() > 0) {
    char cmd = (char)Serial.read();
    if (cmd == 'm' || cmd == 'M') {
      modoTurbo = !modoTurbo;
      Serial.println(modoTurbo ? "EVENTO: MODO TURBO ON" : "EVENTO: MODO NORMAL");
    }
  }

  // 2. Lectura real del ADC de 10 bits en A0 (Potenciometro) y A1 (Capacitor RC)
  int valSetpoint = analogRead(A0);
  int valTanque   = analogRead(A1);

  // 3. Lazo de histeresis del oscilador RC (genera rampa de ~1.0V <-> ~4.0V)
  if (cargandoRc && valTanque >= RC_MAX_ADC) {
    cargandoRc = false;
    digitalWrite(PIN_RC_CTRL, LOW);
  } else if (!cargandoRc && valTanque <= RC_MIN_ADC) {
    cargandoRc = true;
    digitalWrite(PIN_RC_CTRL, HIGH);
  }

  // 4. Vumetro de 4 LEDs + LED de alarma cuando RC supera al Potenciometro o en Turbo
  digitalWrite(PIN_LED_25,  (valTanque >= 256) ? HIGH : LOW);
  digitalWrite(PIN_LED_50,  (valTanque >= 480) ? HIGH : LOW);
  digitalWrite(PIN_LED_75,  (valTanque >= 700) ? HIGH : LOW);
  digitalWrite(PIN_LED_ALM, (valTanque >= valSetpoint || modoTurbo) ? HIGH : LOW);

  // 5. Control PWM del Transistor NPN (Motor DC + Lampara 9V)
  //    Normal: escala proporcional 0..255 sobre el rango RC 180..690 | Turbo: 255 (100% fijo)
  int pwmMotor;
  if (modoTurbo) {
    pwmMotor = 255;
  } else {
    pwmMotor = map(constrain(valTanque, 180, 690), 180, 690, 0, 255);
  }
  analogWrite(PIN_MOTOR_PWM, pwmMotor);

  // 6. LED de latido por PWM en D6 (onda triangular suave, mas rapida en Turbo)
  unsigned int intervaloLatido = modoTurbo ? 20U : 45U;
  if ((unsigned long)(ahora - ultimoPasoMs) >= intervaloLatido) {
    ultimoPasoMs = ahora;
    faseOnda = (faseOnda + 1) & 31;
    byte nivelLatido = (faseOnda < 16) ? (byte)(faseOnda * 17) : (byte)((31 - faseOnda) * 17);
    analogWrite(PIN_LED_PWM, nivelLatido);
  }

  // 7. Telemetria en tiempo real hacia el Monitor Serie de Simutronik
  if ((unsigned long)(ahora - ultimoSerialMs) >= 400UL) {
    ultimoSerialMs = ahora;
    Serial.print("MODO=");
    Serial.print(modoTurbo ? "TURBO" : "NORM");
    Serial.print(" SP=");
    Serial.print(valSetpoint);
    Serial.print(" RC=");
    Serial.print(valTanque);
    Serial.print(" PWM=");
    Serial.println(pwmMotor);
  }
}
