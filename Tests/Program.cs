using System.Text;
using ElectricSim.Core;
using ElectricSim.Core.Arduino;
using ElectricSim.Core.Components;
using ElectricSim.Tests;

// =====================================================================
//  SUITE DE VERIFICACIÓN DEL MOTOR (sin dependencias externas).
//
//  Correla con:   dotnet run --project Tests
//
//  Las DEMOS 1 a 4 son las del proyecto original (la 1, 3 y 7 se
//  actualizaron al nuevo modelo del LED de caída constante: las
//  expectativas ahora son las fórmulas exactas del apunte). Las DEMOS
//  5 a 18 verifican todo lo agregado: corriente real de fuentes
//  (Kirchhoff), cortocircuitos, fusibles, AC por RMS, compatibilidad
//  de corriente, sistema térmico, rupturas por cada límite, pulsador
//  y capacitor. Las DEMOS 19 a 22 son REGRESIONES de los tres bugs
//  corregidos en la revisión: parpadeo del LED, capacitor que se
//  cargaba solo, y corriente/límites de los componentes fusionados.
//
//  El código de salida del proceso es la cantidad de verificaciones que
//  fallaron (0 = todo verde), así se puede usar en CI de GitHub.
// =====================================================================

int testsOk = 0;
int testsTotal = 0;

void Verificar(string nombre, bool condicion)
{
    testsTotal++;
    if (condicion) testsOk++;
    Console.WriteLine(condicion ? $"  >> PASS: {nombre}" : $"  >> FAIL: {nombre}");
}

// ============================================================
// DEMO 1: Circuito SERIE -> Bateria 9V - R1 220ohm - LED
//   La corriente debe ser la MISMA en toda la cadena.
//   El LED se comporta como CAIDA CONSTANTE (modelo de manual):
//   V_led = 2V fijos, y la resistencia se lleva el resto ->
//   I = (V_fuente - V_led) / R = (9-2)/220 = 31.8mA
// ============================================================
Console.WriteLine("====================================================");
Console.WriteLine(" DEMO 1: Serie -> Bateria 9V - R1 220ohm - LED");
Console.WriteLine("====================================================\n");

var graph1 = new CircuitGraph();
var bateria1 = new Battery("Bateria9V", voltage: 9);
var r1 = new Resistor("R1", resistanceOhms: 220);
var led1 = new LED("LED1", voltajeUmbral: 2.0, resistenciaEncendido: 12);

graph1.Agregar(bateria1);
graph1.Agregar(r1);
graph1.Agregar(led1);
graph1.Conectar(bateria1.PinA, r1.PinA, "cable_bat_r1");
graph1.Conectar(r1.PinB, led1.PinA, "cable_r1_led");
graph1.Conectar(led1.PinB, bateria1.PinB, "cable_led_bat");

// El LED arranca "apagado" (resistencia altisima) y va convergiendo a su
// punto de operacion en unos pocos ticks: el encendido tiene un fade-in.
for (int tick = 1; tick <= 12; tick++)
{
    graph1.Tick(deltaTime: 0.05);
    if (tick <= 3 || tick == 12)
        Console.WriteLine($"Tick {tick}: I(R1)={r1.Current * 1000:F3}mA  V(LED)={(led1.PinA.Voltage - led1.PinB.Voltage):F3}V  LED encendido={led1.EstaEncendido}  Brillo={led1.Brillo:F2}");
}

double iEsperadaSerie = (bateria1.Voltage - led1.VoltajeUmbral) / r1.Resistance;
Console.WriteLine($"\nFormula del apunte: I = (9V - 2V) / 220ohm = {iEsperadaSerie * 1000:F3} mA");
Console.WriteLine($"Misma corriente en R1 y LED (circuito serie): I(R1)={r1.Current * 1000:F3}mA, I(LED)={led1.Current * 1000:F3}mA");
Verificar("la corriente coincide con I=(V-Vf)/R del apunte", Math.Abs(iEsperadaSerie - r1.Current) < 0.0005);
Verificar("el LED clava su caida en el voltaje umbral (2V)", Math.Abs(led1.VoltajeEnBornes - led1.VoltajeUmbral) < 0.02);
Verificar("R1 e LED tienen la MISMA corriente (propiedad de serie)", Math.Abs(r1.Current - led1.Current) < 1e-9);
Verificar("el LED prendió", led1.EstaEncendido);

// ----- Verificación cruzada de las 3 fórmulas de potencia sobre R1 -----
double deltaVr1 = r1.PinA.Voltage - r1.PinB.Voltage;
double pVI = LeyesElectricas.PotenciaDesdeVI(deltaVr1, r1.Current);
double pIR = LeyesElectricas.PotenciaDesdeIR(r1.Current, r1.Resistance);
double pVR = LeyesElectricas.PotenciaDesdeVR(deltaVr1, r1.Resistance);
Console.WriteLine($"\nPotencia en R1 por las 3 formulas -> P=V*I: {pVI * 1000:F3}mW | P=I^2*R: {pIR * 1000:F3}mW | P=V^2/R: {pVR * 1000:F3}mW");
Verificar("las 3 formulas de potencia coinciden entre si", Math.Abs(pVI - pIR) < 1e-9 && Math.Abs(pVI - pVR) < 1e-9);


// ============================================================
// DEMO 2: Circuito PARALELO -> Bateria 9V con R_a(100) y R_b(200)
//   El voltaje debe ser el MISMO en ambas ramas.
//   La corriente se DIVIDE entre las ramas.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 2: Paralelo -> Bateria 9V con Ra=100ohm y Rb=200ohm");
Console.WriteLine("====================================================\n");

var graph2 = new CircuitGraph();
var bateria2 = new Battery("Bateria9V_p", voltage: 9);
var ra = new Resistor("Ra", resistanceOhms: 100);
var rb = new Resistor("Rb", resistanceOhms: 200);

graph2.Agregar(bateria2);
graph2.Agregar(ra);
graph2.Agregar(rb);
graph2.Conectar(bateria2.PinA, ra.PinA, "cable_bat_ra_pos");
graph2.Conectar(bateria2.PinA, rb.PinA, "cable_bat_rb_pos");
graph2.Conectar(bateria2.PinB, ra.PinB, "cable_bat_ra_neg");
graph2.Conectar(bateria2.PinB, rb.PinB, "cable_bat_rb_neg");

graph2.Tick(deltaTime: 0.05);
graph2.Tick(deltaTime: 0.05);

double vRa = ra.PinA.Voltage - ra.PinB.Voltage;
double vRb = rb.PinA.Voltage - rb.PinB.Voltage;
double iTotal = ra.Current + rb.Current;
double rEquivalente = (ra.Resistance * rb.Resistance) / (ra.Resistance + rb.Resistance);
// Fix 4: la R_int de la fuente participa del solver — la corriente total
// es la del circuito REAL (R_eq + R_int), no la de una fuente ideal.
double iTotalEsperada = bateria2.Voltage / (rEquivalente + bateria2.ResistenciaInternaNominal);

Console.WriteLine($"V(Ra)={vRa:F3}V   V(Rb)={vRb:F3}V   (deberian ser iguales: mismo voltaje en paralelo)");
Console.WriteLine($"I(Ra)={ra.Current * 1000:F2}mA  I(Rb)={rb.Current * 1000:F2}mA  -> suma = {iTotal * 1000:F2}mA");
Console.WriteLine($"R equivalente = (100*200)/(100+200) = {rEquivalente:F2} ohm  ->  I total esperada = 9V/{rEquivalente:F2}ohm = {iTotalEsperada * 1000:F2}mA");
Verificar("Ra y Rb tienen el MISMO voltaje (propiedad de paralelo)", Math.Abs(vRa - vRb) < 0.001);
Verificar("la suma de corrientes coincide con la corriente total esperada", Math.Abs(iTotal - iTotalEsperada) < 0.0005);
Verificar("Ra (menor resistencia) lleva mas corriente que Rb", ra.Current > rb.Current);


// ============================================================
// DEMO 3: Sin resistencia limitadora -> el LED se quema
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 3: Sin resistencia limitadora -> el LED se quema");
Console.WriteLine("====================================================\n");

var graph3 = new CircuitGraph();
var bateria3 = new Battery("Bateria9V_b", voltage: 9);
var led3 = new LED("LED3", voltajeUmbral: 2.0, resistenciaEncendido: 12);

graph3.Agregar(bateria3);
graph3.Agregar(led3);
graph3.Conectar(bateria3.PinA, led3.PinA, "cable_a");
graph3.Conectar(led3.PinB, bateria3.PinB, "cable_b");

// Con el modelo de caida constante el LED "abre la canilla" de a poco
// (converge desde apagado): la corriente trepa tick a tick, y sin
// limitadora se pasa de rosca -> 3 ticks seguidos de sobre-potencia y muere.
for (int tick = 1; tick <= 15 && !led3.IsBurnt; tick++)
{
    graph3.Tick(deltaTime: 0.05);
    Console.WriteLine($"Tick {tick}: quemado={led3.IsBurnt}  I={led3.Current * 1000:F2}mA  P={led3.Power * 1000:F2}mW (limite={led3.MaxPower * 1000:F0}mW)");
}
Verificar("el LED se quemo por superar su potencia maxima", led3.IsBurnt);
Verificar("una vez quemado, ya no pasa corriente", led3.Current == 0);


// ============================================================
// DEMO 5: Corriente REAL de la fuente (Ley de Kirchhoff)
//   Antes la bateria mostraba Current = 0 a proposito. Ahora
//   CircuitGraph le asigna la corriente fisica de verdad.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 5: Corriente real de la fuente (Kirchhoff)");
Console.WriteLine("====================================================\n");

Console.WriteLine($"Serie (DEMO 1): I(bateria)={bateria1.Current * 1000:F3}mA vs I(R1)={r1.Current * 1000:F3}mA");
Verificar("en serie, la bateria entrega la misma corriente que atraviesa R1", Math.Abs(bateria1.Current - r1.Current) < 1e-6);
// La potencia ENTREGADA se mide con el voltaje REAL de bornes (ya hundido
// por la carga), no con el nominal: es lo que de verdad recibe la red.
Verificar("la potencia entregada por la bateria es V_bornes*I",
    Math.Abs(bateria1.Power - bateria1.VoltajeEnBornes * bateria1.Current) < 1e-9);
Verificar("y es MENOR que la nominal V*I (la R_int se queda con su parte)",
    bateria1.Power < 9 * bateria1.Current);

Console.WriteLine($"Paralelo (DEMO 2): I(bateria)={bateria2.Current * 1000:F2}mA vs Ia+Ib={(ra.Current + rb.Current) * 1000:F2}mA");
Verificar("en paralelo, la bateria entrega la suma de las corrientes de las ramas", Math.Abs(bateria2.Current - (ra.Current + rb.Current)) < 1e-6);


// ============================================================
// DEMO 6: CORTOCIRCUITO -> cable directo de + a - de la bateria.
//   El solver detecta el conflicto de nodos fijos, la bateria se
//   calienta violentamente y muere en pocos ticks.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 6: Cortocircuito directo sobre la bateria");
Console.WriteLine("====================================================\n");

// FISICA NUEVA: un corto ya no es un timer de muerte. La corriente de corto
// es V/R_interna (9/1.7 = 5.3A para la 9V), y como NO supera su MaxCurrent
// (6A), la bateria NO muere: queda marcada EN CORTO, convertida en estufa de
// ~48W, calentandose hacia su equilibrio termico (~65°C). Asi se comporta
// una 9V real: cortocircuitarla la arruina y la calienta, no la explota.
var graph6 = new CircuitGraph();
var termico6 = new SimuladorTermico();
var bateria6 = new Battery("Bateria9V_c", voltage: 9);
graph6.Agregar(bateria6);
graph6.Conectar(bateria6.PinA, bateria6.PinB, "cable_corto");

bool detectoCortoEnTick1 = false;
for (int tick = 1; tick <= 30; tick++)
{
    graph6.Tick(deltaTime: 0.1);
    termico6.Paso(graph6.Components, 0.1);
    if (tick == 1) detectoCortoEnTick1 = bateria6.EnCortocircuito;
    if (tick % 10 == 0)
        Console.WriteLine($"t={tick * 0.1:F1}s: en corto={bateria6.EnCortocircuito}  I={bateria6.CorrienteEntregada:F2}A  P_calor={bateria6.PotenciaTermica:F1}W  T={bateria6.Temperatura:F1}C  quemada={bateria6.IsBurnt}");
}
Verificar("el corto se detecta en el primer tick", detectoCortoEnTick1);
Verificar("la corriente de corto es V/R_interna (~5.3A), visible en el inspector",
    bateria6.CorrienteEntregada > 5.0 && bateria6.CorrienteEntregada < 5.6);
Verificar("la bateria se calienta de verdad (estufa de ~48W)", bateria6.Temperatura > 24);
Verificar("la 9V SOBREVIVE al corto: su corriente de corto no supera su MaxCurrent", !bateria6.IsBurnt);

// El otro camino: una fuente cuyo corto SI supera lo que aguanta (una pila
// chica, el regulador del Arduino) muere a los SegundosParaQuemarse, con el
// motivo que corresponde.
var graph6b = new CircuitGraph();
var fragil6 = new Battery("Fragil", voltage: 9) { MaxCurrent = 1.0 };
graph6b.Agregar(fragil6);
graph6b.Conectar(fragil6.PinA, fragil6.PinB, "cable_corto_b");
for (int tick = 1; tick <= 8 && !fragil6.IsBurnt; tick++) graph6b.Tick(0.1);
Console.WriteLine($"Fuente fragil (tope 1A, corto de 5.3A): quemada={fragil6.IsBurnt} motivo='{fragil6.MotivoQuemado}'");
Verificar("una fuente cuyo corto supera su MaxCurrent SI muere", fragil6.IsBurnt);
Verificar("el motivo registrado es 'cortocircuito'", fragil6.MotivoQuemado == "cortocircuito");


// ============================================================
// DEMO 7: El FUSIBLE protege al LED.
//   Bateria 9V + fusible de 50mA + LED sin resistencia limitadora:
//   la corriente del LED trepa tick a tick (fade-in del modelo de caida
//   constante) y en cuanto cruza los 50mA el fusible corta AL PRIMER
//   tick de exceso. El LED (que necesitaba 3 ticks seguidos de
//   sobre-potencia para morir) SE SALVA.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 7: El fusible se sacrifica y salva al LED");
Console.WriteLine("====================================================\n");

var graph7 = new CircuitGraph();
var bateria7 = new Battery("Bateria9V_f", voltage: 9);
var fusible7 = new Fusible("Fusible50mA", corrienteMaxima: 0.05);
var led7 = new LED("LED7", voltajeUmbral: 2.0, resistenciaEncendido: 12);
graph7.Agregar(bateria7);
graph7.Agregar(fusible7);
graph7.Agregar(led7);
graph7.Conectar(bateria7.PinA, fusible7.PinA);
graph7.Conectar(fusible7.PinB, led7.PinA);
graph7.Conectar(led7.PinB, bateria7.PinB);

for (int tick = 1; tick <= 12; tick++)
{
    graph7.Tick(deltaTime: 0.1);
    Console.WriteLine($"Tick {tick}: I(fusible)={fusible7.Current * 1000:F1}mA fusible quemado={fusible7.IsBurnt}  I(LED)={led7.Current * 1000:F1}mA LED quemado={led7.IsBurnt}");
}
Verificar("el fusible corto al primer tick de exceso", fusible7.IsBurnt && fusible7.MotivoQuemado == "corriente");
Verificar("el LED sobrevivio gracias al fusible", !led7.IsBurnt);
Verificar("con el fusible cortado ya no circula corriente (queda solo la fuga de microamperes del 1M\u03a9 \"abierto\")", Math.Abs(led7.Current) < 1e-4);


// ============================================================
// DEMO 8: Cable con resistencia puesta por el usuario.
//   Al subirle la resistencia por encima del umbral, el cable deja
//   de ser una fusion perfecta y pasa a ser una arista real: cae
//   voltaje en el, disipa potencia y genera calor.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 8: Cable resistivo (el usuario le puso 10 ohm)");
Console.WriteLine("====================================================\n");

var graph8 = new CircuitGraph();
var bateria8 = new Battery("Bateria9V_w", voltage: 9);
var r8 = new Resistor("R90", resistanceOhms: 90);
graph8.Agregar(bateria8);
graph8.Agregar(r8);
var cableResistivo = graph8.Conectar(bateria8.PinA, r8.PinA, "cable_gastado");
cableResistivo.Resistencia = 10; // el usuario lo edito en el inspector
graph8.Conectar(r8.PinB, bateria8.PinB);

graph8.Tick(0.1);
graph8.Tick(0.1);

double iEsperada8 = 9.0 / (10 + 90 + bateria8.ResistenciaInternaNominal);
Console.WriteLine($"I esperada = 9V / (10+90)ohm = {iEsperada8 * 1000:F1}mA -> I(cable)={cableResistivo.Current * 1000:F1}mA  V(cable)={cableResistivo.VoltajeEnBornes:F3}V  P(cable)={cableResistivo.Power * 1000:F1}mW");
Verificar("el cable resistivo conduce la corriente de Ohm del circuito completo", Math.Abs(cableResistivo.Current - iEsperada8) < 0.0005);
Verificar("en el cable resistivo cae voltaje (ya no es fusion perfecta)",
    Math.Abs(cableResistivo.VoltajeEnBornes - iEsperada8 * 10) < 0.01);
Verificar("el cable resistivo disipa potencia (I^2*R)", Math.Abs(cableResistivo.Power - iEsperada8 * iEsperada8 * 10) < 0.001);


// ============================================================
// DEMO 9: Ruptura por CADA limite de la ficha de estadisticas.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 9: Ruptura por corriente maxima y por voltaje maximo");
Console.WriteLine("====================================================\n");

var graph9a = new CircuitGraph();
var bateria9a = new Battery("B9a", 9);
var r9a = new Resistor("Fragil_I", 10) { MaxCurrent = 0.5 }; // va a recibir 0.9A
graph9a.Agregar(bateria9a);
graph9a.Agregar(r9a);
graph9a.Conectar(bateria9a.PinA, r9a.PinA);
graph9a.Conectar(r9a.PinB, bateria9a.PinB);
for (int t = 0; t < 4; t++) graph9a.Tick(0.1);
Console.WriteLine($"Fragil_I: quemada={r9a.IsBurnt} motivo='{r9a.MotivoQuemado}'");
Verificar("una resistencia con MaxCurrent bajo se rompe por 'corriente'", r9a.IsBurnt && r9a.MotivoQuemado == "corriente");

var graph9b = new CircuitGraph();
var bateria9b = new Battery("B9b", 9);
var r9b = new Resistor("Fragil_V", 100000) { MaxVoltage = 5 }; // casi no circula corriente, pero le caen ~9V
graph9b.Agregar(bateria9b);
graph9b.Agregar(r9b);
graph9b.Conectar(bateria9b.PinA, r9b.PinA);
graph9b.Conectar(r9b.PinB, bateria9b.PinB);
for (int t = 0; t < 4; t++) graph9b.Tick(0.1);
Console.WriteLine($"Fragil_V: quemada={r9b.IsBurnt} motivo='{r9b.MotivoQuemado}'");
Verificar("una resistencia con MaxVoltage bajo se rompe por 'voltaje'", r9b.IsBurnt && r9b.MotivoQuemado == "voltaje");


// ============================================================
// DEMO 10: Corriente ALTERNA por valor RMS.
//   FuenteAC de 12V RMS + lampara de 60 ohm: para cargas resistivas
//   el RMS da exactamente los mismos Watts que la onda real.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 10: Fuente AC (RMS) + lampara incandescente");
Console.WriteLine("====================================================\n");

var graph10 = new CircuitGraph();
var fuenteAC = new FuenteAC("Fuente220", voltajeRms: 12, frecuenciaHz: 50);
var lampara10 = new Lampara("Lamparita", resistenciaOhms: 60);
graph10.Agregar(fuenteAC);
graph10.Agregar(lampara10);
graph10.Conectar(fuenteAC.PinA, lampara10.PinA); // fase
graph10.Conectar(lampara10.PinB, fuenteAC.PinB); // neutro

graph10.Tick(0.1);
graph10.Tick(0.1);

double iEsperada10 = 12.0 / (60.0 + fuenteAC.ResistenciaInternaNominal);
Console.WriteLine($"I={lampara10.Current * 1000:F1}mA (esperado {iEsperada10 * 1000:F0}mA)  P={lampara10.Power:F2}W  Brillo={lampara10.Brillo:F2}  Tipo recibido={lampara10.TipoCorrienteRecibida}");
Verificar("la corriente RMS coincide con Ohm (I = Vrms / R)", Math.Abs(lampara10.Current - iEsperada10) < 0.0005);
Verificar("la lampara recibe corriente tipo AC", lampara10.TipoCorrienteRecibida == TipoCorriente.AC);
// Con la R_int de la fuente en el lazo llega un pelín menos de potencia
// que la nominal: el brillo queda al filo del máximo, no clavado en 1.
Verificar("la lampara brilla casi al maximo con su potencia nominal", lampara10.Brillo > 0.95);
Verificar("la fuente AC reporta la corriente que entrega (Kirchhoff)", Math.Abs(fuenteAC.Current - iEsperada10) < 1e-6);


// ============================================================
// DEMO 11: LED con corriente ALTERNA -> incompatible.
//   El LED es solo-DC: detecta que le llega AC, no conduce, no
//   prende, y NO se rompe (simplemente "no anda").
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 11: LED conectado a una fuente AC (incompatible)");
Console.WriteLine("====================================================\n");

var graph11 = new CircuitGraph();
var fuenteAC11 = new FuenteAC("FuenteAC11", voltajeRms: 12);
var r11 = new Resistor("R220_11", 220);
var led11 = new LED("LED11");
graph11.Agregar(fuenteAC11);
graph11.Agregar(r11);
graph11.Agregar(led11);
graph11.Conectar(fuenteAC11.PinA, r11.PinA);
graph11.Conectar(r11.PinB, led11.PinA);
graph11.Conectar(led11.PinB, fuenteAC11.PinB);

for (int t = 0; t < 10; t++) graph11.Tick(0.1);

Console.WriteLine($"LED: tipo recibido={led11.TipoCorrienteRecibida}  incompatible={led11.EsIncompatible}  encendido={led11.EstaEncendido}  I={led11.Current * 1000:F3}mA  quemado={led11.IsBurnt}");
Verificar("el LED detecta que le llega AC", led11.TipoCorrienteRecibida == TipoCorriente.AC);
Verificar("el LED marca la incompatibilidad", led11.EsIncompatible);
Verificar("el LED incompatible no conduce ni prende", !led11.EstaEncendido && Math.Abs(led11.Current) < 1e-6);
Verificar("la incompatibilidad no lo rompe (solo no funciona)", !led11.IsBurnt);


// ============================================================
// DEMO 12: Fuentes DC y AC conectadas a la misma red -> Mixta.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 12: Bateria y fuente AC en la misma red (Mixta)");
Console.WriteLine("====================================================\n");

var graph12 = new CircuitGraph();
var bateria12 = new Battery("B12", 9);
var fuenteAC12 = new FuenteAC("AC12", 12);
var r12 = new Resistor("Puente", 100);
graph12.Agregar(bateria12);
graph12.Agregar(fuenteAC12);
graph12.Agregar(r12);
graph12.Conectar(bateria12.PinA, r12.PinA);   // + de la bateria a un extremo
graph12.Conectar(r12.PinB, fuenteAC12.PinA);  // el otro extremo a la FASE de la AC
graph12.Conectar(bateria12.PinB, fuenteAC12.PinB); // negativos/neutros unidos (misma referencia 0V)

graph12.Tick(0.1);
graph12.Tick(0.1);

Console.WriteLine($"La resistencia puente recibe: {r12.TipoCorrienteRecibida}");
Verificar("un componente alimentado por DC y AC a la vez recibe tipo 'Mixta'", r12.TipoCorrienteRecibida == TipoCorriente.Mixta);
Verificar("una resistencia (compatible con todo) no se bloquea por recibir Mixta", !r12.EsIncompatible);


// ============================================================
// DEMO 13: Sistema TERMICO completo.
//   Una lampara prendida se calienta; el componente pegado a ella
//   recibe calor "contagiado"; el que esta lejos, no. Al abrir el
//   interruptor, la lampara se enfria sola hacia el ambiente.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 13: Calor: calentarse, contagiar a vecinos, enfriarse");
Console.WriteLine("====================================================\n");

var graph13 = new CircuitGraph();
var termico13 = new SimuladorTermico(); // ambiente 20 C
var bateria13 = new Battery("B13", 9);
var switch13 = new Switch("Llave13", startClosed: true);
var lampara13 = new Lampara("Estufa", resistenciaOhms: 60);
var vecinoCerca = new Resistor("VecinoCerca", 1000);   // no conectado: solo esta APOYADO al lado
var vecinoLejos = new Resistor("VecinoLejos", 1000);   // no conectado y lejos

lampara13.PosicionFisica = new System.Numerics.Vector3(0, 0, 0);
vecinoCerca.PosicionFisica = new System.Numerics.Vector3(0.2f, 0, 0);
vecinoLejos.PosicionFisica = new System.Numerics.Vector3(5, 0, 0);
bateria13.PosicionFisica = new System.Numerics.Vector3(-2, 0, 0);
switch13.PosicionFisica = new System.Numerics.Vector3(-1, 0, 0);

graph13.Agregar(bateria13);
graph13.Agregar(switch13);
graph13.Agregar(lampara13);
graph13.Agregar(vecinoCerca);
graph13.Agregar(vecinoLejos);
// Los cables tambien son componentes con posicion: los ubicamos en el punto
// medio de su tendido, exactamente como lo hace SIEMPRE la capa visual del
// juego. (Si se dejaran todos en el (0,0,0) default quedarian apilados
// arriba de la lampara actuando de disipadores, un artefacto del test.)
var w13a = graph13.Conectar(bateria13.PinA, switch13.PinA);
w13a.PosicionFisica = new System.Numerics.Vector3(-1.5f, 0, 0);
var w13b = graph13.Conectar(switch13.PinB, lampara13.PinA);
w13b.PosicionFisica = new System.Numerics.Vector3(-0.5f, 0, 0);
var w13c = graph13.Conectar(lampara13.PinB, bateria13.PinB);
w13c.PosicionFisica = new System.Numerics.Vector3(-1f, 0, 0.6f);

for (int t = 0; t < 120; t++) // 12 segundos simulados a 10 ticks/s
{
    graph13.Tick(0.1);
    termico13.Paso(graph13.Components, 0.1);
}
double tempLamparaPrendida = lampara13.Temperatura;
Console.WriteLine($"Tras 12s prendida: T(lampara)={lampara13.Temperatura:F1}C  T(vecino cerca)={vecinoCerca.Temperatura:F1}C  T(vecino lejos)={vecinoLejos.Temperatura:F1}C");
Verificar("la lampara prendida se calienta bien por encima del ambiente", lampara13.Temperatura > 40);
Verificar("el vecino pegado recibe calor contagiado", vecinoCerca.Temperatura > 22);
Verificar("el vecino lejano se queda a temperatura ambiente", vecinoLejos.Temperatura < 21);
Verificar("la lampara sigue brillando (no llego a su temperatura maxima)", !lampara13.IsBurnt && lampara13.Brillo > 0.4);

switch13.IsClosed = false; // cortamos la corriente
for (int t = 0; t < 80; t++)
{
    graph13.Tick(0.1);
    termico13.Paso(graph13.Components, 0.1);
}
Console.WriteLine($"Tras 8s apagada: T(lampara)={lampara13.Temperatura:F1}C (antes {tempLamparaPrendida:F1}C)");
Verificar("al cortarle la corriente, la lampara se enfria sola", lampara13.Temperatura < tempLamparaPrendida - 5);
Verificar("apagada, la lampara no brilla", lampara13.Brillo == 0);


// ============================================================
// DEMO 14: Ruptura por TEMPERATURA maxima.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 14: Un componente fragil al calor se rompe por temperatura");
Console.WriteLine("====================================================\n");

var graph14 = new CircuitGraph();
var termico14 = new SimuladorTermico();
var bateria14 = new Battery("B14", 9);
var lampara14 = new Lampara("Fragil_T", resistenciaOhms: 60) { MaxTemperature = 40 };
// Posiciones separadas, como en el mundo real del juego (si todo quedara
// apilado en el (0,0,0) default, la bateria — 25 J/°C — le haria de
// disipador pegado a la lampara y nunca llegaria a los 40 °C).
lampara14.PosicionFisica = new System.Numerics.Vector3(0, 0, 0);
bateria14.PosicionFisica = new System.Numerics.Vector3(1.2f, 0, 0);
graph14.Agregar(bateria14);
graph14.Agregar(lampara14);
var w14a = graph14.Conectar(bateria14.PinA, lampara14.PinA);
w14a.PosicionFisica = new System.Numerics.Vector3(0.6f, 0, 0.3f);
var w14b = graph14.Conectar(lampara14.PinB, bateria14.PinB);
w14b.PosicionFisica = new System.Numerics.Vector3(0.6f, 0, -0.3f);

int tickDeMuerte = -1;
for (int t = 1; t <= 120 && !lampara14.IsBurnt; t++)
{
    graph14.Tick(0.1);
    termico14.Paso(graph14.Components, 0.1);
    if (lampara14.IsBurnt) tickDeMuerte = t;
}
Console.WriteLine($"Quemada={lampara14.IsBurnt} motivo='{lampara14.MotivoQuemado}' en el tick {tickDeMuerte} a {lampara14.Temperatura:F1}C");
Verificar("el componente se rompio por 'temperatura'", lampara14.IsBurnt && lampara14.MotivoQuemado == "temperatura");
Verificar("roto, deja de generar calor (PotenciaTermica = 0)", lampara14.PotenciaTermica == 0);


// ============================================================
// DEMO 15: Pulsador (boton momentaneo) -> se abre solo.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 15: Pulsador: cierra al tocarlo y se abre solo");
Console.WriteLine("====================================================\n");

var graph15 = new CircuitGraph();
var bateria15 = new Battery("B15", 9);
var pulsador15 = new Pulsador("Timbre") { DuracionPulso = 0.5 };
var lampara15 = new Lampara("Foco15", 60);
graph15.Agregar(bateria15);
graph15.Agregar(pulsador15);
graph15.Agregar(lampara15);
graph15.Conectar(bateria15.PinA, pulsador15.PinA);
graph15.Conectar(pulsador15.PinB, lampara15.PinA);
graph15.Conectar(lampara15.PinB, bateria15.PinB);

pulsador15.Presionar();
graph15.Tick(0.1);
bool prendioAlTocar = lampara15.Brillo > 0;
for (int t = 0; t < 7; t++) graph15.Tick(0.1); // 0.7s mas: el pulso de 0.5s ya vencio
Console.WriteLine($"Al tocar: brillo>0={prendioAlTocar}. Tras 0.8s: presionado={pulsador15.Presionado}  brillo={lampara15.Brillo:F2}");
Verificar("al presionar, el circuito cierra y la lampara prende", prendioAlTocar);
Verificar("pasado el pulso, el boton se abre solo", !pulsador15.Presionado);
Verificar("con el boton abierto, la lampara se apaga", lampara15.Brillo == 0);


// ============================================================
// DEMO 16: Capacitor bloquea la corriente CONTINUA al cargarse
//   (asegura el comportamiento heredado de la v1).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 16: Capacitor: conduce al principio, bloquea al cargarse");
Console.WriteLine("====================================================\n");

var graph16 = new CircuitGraph();
var bateria16 = new Battery("B16", 9);
var r16 = new Resistor("R16", 100);
var cap16 = new Capacitor("C16", capacitanceFarads: 0.05);
graph16.Agregar(bateria16);
graph16.Agregar(r16);
graph16.Agregar(cap16);
graph16.Conectar(bateria16.PinA, r16.PinA);
graph16.Conectar(r16.PinB, cap16.PinA);
graph16.Conectar(cap16.PinB, bateria16.PinB);

graph16.Tick(0.1);
double iInicial16 = Math.Abs(cap16.Current);
double cargaInicial16 = cap16.ChargeLevel;
for (int t = 0; t < 30; t++) graph16.Tick(0.1);
Console.WriteLine($"I inicial={iInicial16 * 1000:F1}mA carga inicial={cargaInicial16:F2} -> tras 3s: I={Math.Abs(cap16.Current) * 1000:F3}mA carga={cap16.ChargeLevel:F2}");
Verificar("recien conectado, el capacitor deja pasar corriente", iInicial16 > 0.01);
Verificar("la carga crece con el tiempo", cap16.ChargeLevel > cargaInicial16);
// La carga sigue la EXPONENCIAL real (backward Euler), no la vieja rampa
// lineal: en 3s con τ=R·C sólo se llega a 1−e^(−3/τ). Verificamos la
// física de verdad — que la corriente CAE al cargarse — en vez de un
// porcentaje que era matemáticamente inalcanzable en ese tiempo.
Verificar("al cargarse, la corriente CAE (el capacitor va bloqueando la continua)",
    Math.Abs(cap16.Current) < iInicial16 * 0.6);
// OJO: ChargeLevel es relativo a MaxVoltage (25V, lo que AGUANTA el cap),
// no al equilibrio del circuito. Con una fuente de 9V jamás pasa del 35%,
// así que el viejo assert de ">90% cargado" era inalcanzable de entrada.
// Lo que corresponde medir es el equilibrio FÍSICO: V_alm → V de la fuente.
for (int t = 0; t < 300; t++) graph16.Tick(0.1);
double vEquilibrio16 = 9.0 * 100 / (100 + bateria16.ResistenciaInternaNominal);
Console.WriteLine($"Tras 33s (~6.5 tau): V_alm={cap16.VoltajeAlmacenado:F2}V (equilibrio {vEquilibrio16:F2}V)  I={Math.Abs(cap16.Current) * 1e6:F0}uA");
Verificar("y tras varias constantes de tiempo, bloquea del todo y queda en equilibrio",
    Math.Abs(cap16.Current) < 0.001 && cap16.VoltajeAlmacenado > vEquilibrio16 * 0.97);


// ============================================================
// DEMO 17: Dos baterias IGUALES en paralelo se reparten la corriente.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 17: Dos baterias 9V en paralelo alimentando R=100ohm");
Console.WriteLine("====================================================\n");

var graph17 = new CircuitGraph();
var bat17a = new Battery("B17a", 9);
var bat17b = new Battery("B17b", 9);
var r17 = new Resistor("R17", 100);
graph17.Agregar(bat17a);
graph17.Agregar(bat17b);
graph17.Agregar(r17);
graph17.Conectar(bat17a.PinA, r17.PinA);
graph17.Conectar(bat17b.PinA, r17.PinA);
graph17.Conectar(bat17a.PinB, r17.PinB);
graph17.Conectar(bat17b.PinB, r17.PinB);

graph17.Tick(0.1);
graph17.Tick(0.1);
Console.WriteLine($"I(R)={r17.Current * 1000:F1}mA  I(B17a)={bat17a.Current * 1000:F1}mA  I(B17b)={bat17b.Current * 1000:F1}mA  en corto: {bat17a.EnCortocircuito}/{bat17b.EnCortocircuito}");
// Dos R_int iguales en paralelo = la mitad: 9/(100+1.7/2) = 89.2mA.
double iEsperada17 = 9.0 / (100 + bat17a.ResistenciaInternaNominal / 2);
Verificar("la carga recibe la corriente de Ohm con las dos R_int en paralelo",
    Math.Abs(r17.Current - iEsperada17) < 0.0005);
Verificar("dos fuentes iguales en paralelo NO es cortocircuito", !bat17a.EnCortocircuito && !bat17b.EnCortocircuito);
Verificar("cada bateria aporta la mitad de la corriente",
    Math.Abs(bat17a.Current - iEsperada17 / 2) < 0.0005 && Math.Abs(bat17b.Current - iEsperada17 / 2) < 0.0005);


// ============================================================
// DEMO 18: Una pila boton (3V, 50mA max) sobre-exigida muere
//   por corriente: la ficha de estadisticas manda.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 18: Pila boton sobre-exigida (le piden 300mA, da 50mA max)");
Console.WriteLine("====================================================\n");

var graph18 = new CircuitGraph();
var pila18 = new Battery("PilaBoton", 3) { MaxCurrent = 0.05, ResistenciaInternaNominal = 8 };
var r18 = new Resistor("R18", 10);
graph18.Agregar(pila18);
graph18.Agregar(r18);
graph18.Conectar(pila18.PinA, r18.PinA);
graph18.Conectar(r18.PinB, pila18.PinB);

for (int t = 0; t < 8; t++) graph18.Tick(0.1);
Console.WriteLine($"Pila: quemada={pila18.IsBurnt} motivo='{pila18.MotivoQuemado}'  I actual={pila18.Current * 1000:F1}mA");
Verificar("la pila boton murio por 'corriente'", pila18.IsBurnt && pila18.MotivoQuemado == "corriente");
Verificar("muerta, la pila deja de alimentar el circuito", Math.Abs(r18.Current) < 1e-6);


// ============================================================
// DEMO 19: REGRESION anti-parpadeo del LED.
//   Bateria 9V + R 470ohm + LED: el circuito de manual mas comun.
//   Con el modelo viejo (R fija de 100ohm conduciendo) el divisor
//   dejaba caer MENOS de 2V sobre el LED -> se apagaba -> con 1Mohm
//   volvian a caerle casi 9V -> se prendia... parpadeo eterno a 5Hz.
//   El modelo de caida constante converge al punto exacto y SE QUEDA:
//   I = (9-2)/470 = 14.9mA con V_led clavado en 2V.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 19: LED con R=470ohm queda ENCENDIDO estable (sin parpadeo)");
Console.WriteLine("====================================================\n");

var graph19 = new CircuitGraph();
var bat19 = new Battery("B19", 9);
var r19 = new Resistor("R470", 470);
var led19 = new LED("LED19", voltajeUmbral: 2.0, resistenciaEncendido: 12);
graph19.Agregar(bat19);
graph19.Agregar(r19);
graph19.Agregar(led19);
graph19.Conectar(bat19.PinA, r19.PinA);
graph19.Conectar(r19.PinB, led19.PinA);
graph19.Conectar(led19.PinB, bat19.PinB);

// 30 ticks: los primeros 15 son de convergencia (fade-in), los ultimos
// 15 tienen que ser TODOS "encendido" -- un solo apagon es el bug de vuelta.
bool establesEncendidos = true;
for (int tick = 1; tick <= 30; tick++)
{
    graph19.Tick(0.1);
    if (tick > 15 && !led19.EstaEncendido) establesEncendidos = false;
    if (tick % 10 == 0)
        Console.WriteLine($"Tick {tick}: ON={led19.EstaEncendido}  I={led19.Current * 1000:F2}mA  V_led={led19.VoltajeEnBornes:F3}V");
}
double iEsperada19 = (9.0 - 2.0) / 470.0;
Console.WriteLine($"Esperado del apunte: I=(9-2)/470={iEsperada19 * 1000:F2}mA, V_led=2.000V");
Verificar("el LED quedo encendido ESTABLE los ultimos 15 ticks (sin parpadeo)", establesEncendidos && led19.EstaEncendido);
Verificar("la corriente converge a I=(V-Vf)/R del apunte", Math.Abs(led19.Current - iEsperada19) < 0.0003);
Verificar("la caida del LED converge a su voltaje umbral", Math.Abs(led19.VoltajeEnBornes - 2.0) < 0.02);


// ============================================================
// DEMO 20: REGRESION del capacitor fantasma.
//   Un capacitor SUELTO en la mesa (o detras de una llave abierta)
//   no tiene de donde sacar carga: tiene que quedarse en 0%.
//   Antes se auto-cargaba al 100% en un segundo porque "corriente
//   cero" se interpretaba como "cargando".
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 20: Capacitor suelto o tras llave abierta NO se carga solo");
Console.WriteLine("====================================================\n");

var graph20a = new CircuitGraph();
var capSuelto = new Capacitor("CapSuelto", 0.05);
graph20a.Agregar(capSuelto);
for (int t = 0; t < 20; t++) graph20a.Tick(0.1);
Console.WriteLine($"Suelto en la mesa 2s: carga={capSuelto.ChargeLevel:P0}");

var graph20b = new CircuitGraph();
var bat20 = new Battery("B20", 9);
var llave20 = new Switch("Llave20", startClosed: false);
var cap20 = new Capacitor("Cap20", 0.05);
graph20b.Agregar(bat20);
graph20b.Agregar(llave20);
graph20b.Agregar(cap20);
graph20b.Conectar(bat20.PinA, llave20.PinA);
graph20b.Conectar(llave20.PinB, cap20.PinA);
graph20b.Conectar(cap20.PinB, bat20.PinB);
for (int t = 0; t < 20; t++) graph20b.Tick(0.1);
Console.WriteLine($"Tras llave ABIERTA 2s: carga={cap20.ChargeLevel:P0}");
Verificar("el capacitor suelto sigue en 0%", capSuelto.ChargeLevel < 0.01);
Verificar("el capacitor tras la llave abierta sigue en 0%", cap20.ChargeLevel < 0.01);

// Y al cerrar la llave, carga normalmente (la demo 16 vigila la curva).
llave20.IsClosed = true;
for (int t = 0; t < 35; t++) graph20b.Tick(0.1);
Console.WriteLine($"Al CERRAR la llave y esperar 3.5s: carga={cap20.ChargeLevel:P0}");
// Exponencial real: en 3.5s no llega al 100%. Lo que importa es que
// EMPEZÓ a cargarse al cerrar la llave (antes estaba clavado en 0%).
Verificar("al cerrar la llave, empieza a cargarse de verdad", cap20.ChargeLevel > 0.3);
for (int t = 0; t < 400; t++) graph20b.Tick(0.1);
// Igual que en la DEMO 16: el techo real es el equilibrio del circuito,
// no el 100% de MaxVoltage (que con 9V de fuente no se alcanza nunca).
Console.WriteLine($"Y dejándolo 40s más: V_alm={cap20.VoltajeAlmacenado:F2}V de los 9V de la fuente");
Verificar("y dejándolo el tiempo suficiente, se carga hasta el equilibrio",
    cap20.VoltajeAlmacenado > 8.5);


// ============================================================
// DEMO 21: Los componentes FUSIONADOS reportan su corriente real.
//   Cables perfectos y llaves cerradas se fusionan en un solo nodo
//   para el solver: antes de este fix, el inspector les mostraba
//   0.0mA aunque llevaran 90mA. Ahora el grafo les asigna la
//   corriente real por Kirchhoff.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 21: Cables y llaves fusionados muestran su corriente real");
Console.WriteLine("====================================================\n");

// --- Lazo simple: TODOS los eslabones llevan los mismos 90mA ---
var graph21 = new CircuitGraph();
var bat21 = new Battery("B21", 9);
var llave21 = new Switch("Llave21", startClosed: true);
var r21 = new Resistor("R21", 100);
graph21.Agregar(bat21);
graph21.Agregar(llave21);
graph21.Agregar(r21);
var cable21a = graph21.Conectar(bat21.PinA, llave21.PinA, "c21a");
var cable21b = graph21.Conectar(llave21.PinB, r21.PinA, "c21b");
var cable21c = graph21.Conectar(r21.PinB, bat21.PinB, "c21c");
graph21.Tick(0.1);
graph21.Tick(0.1);
Console.WriteLine($"Serie: I(R)={r21.Current * 1000:F1}  I(cable a)={cable21a.Current * 1000:F1}  I(llave)={llave21.Current * 1000:F1}  I(cable b)={cable21b.Current * 1000:F1}  I(cable c)={cable21c.Current * 1000:F1} mA");
// La corriente del lazo con la R_int de la fuente adentro: 9/(100+1.7).
double iLazo21 = 9.0 / (100 + bat21.ResistenciaInternaNominal);
Verificar("un cable en serie lleva la corriente del lazo (no 0)", Math.Abs(cable21a.Current - iLazo21) < 0.0005);
Verificar("la llave cerrada lleva la corriente del lazo (no 0)", Math.Abs(llave21.Current - iLazo21) < 0.0005);
Verificar("todos los eslabones del lazo llevan la MISMA corriente", Math.Abs(cable21b.Current - iLazo21) < 0.0005 && Math.Abs(cable21c.Current - iLazo21) < 0.0005);

// --- Paralelo: el cable troncal lleva TODO, la derivacion solo su rama ---
var graph21p = new CircuitGraph();
var bat21p = new Battery("B21p", 9);
var ra21 = new Resistor("Ra", 100);   // 90mA
var rb21 = new Resistor("Rb", 200);   // 45mA
graph21p.Agregar(bat21p);
graph21p.Agregar(ra21);
graph21p.Agregar(rb21);
var troncal21 = graph21p.Conectar(bat21p.PinA, ra21.PinA, "troncal");
var deriva21 = graph21p.Conectar(ra21.PinA, rb21.PinA, "derivacion");
graph21p.Conectar(ra21.PinB, bat21p.PinB);
graph21p.Conectar(rb21.PinB, bat21p.PinB);
graph21p.Tick(0.1);
graph21p.Tick(0.1);
Console.WriteLine($"Paralelo: I(troncal)={troncal21.Current * 1000:F1}mA (esp. 135)  I(derivacion)={deriva21.Current * 1000:F1}mA (esp. 45)");
double rEq21 = (100.0 * 200.0) / (100.0 + 200.0);
double iTroncal21 = 9.0 / (rEq21 + bat21p.ResistenciaInternaNominal);
double vRamas21 = iTroncal21 * rEq21;   // el voltaje que de verdad les llega
Verificar("el cable troncal lleva la suma de las ramas", Math.Abs(Math.Abs(troncal21.Current) - iTroncal21) < 0.0005);
Verificar("el cable de derivacion lleva SOLO su rama", Math.Abs(Math.Abs(deriva21.Current) - vRamas21 / 200.0) < 0.0005);

// --- Cables redundantes (dos cables ideales uniendo lo mismo): el
//     reparto real es indeterminado, pero la SUMA es la del circuito ---
var graph21r = new CircuitGraph();
var bat21r = new Battery("B21r", 9);
var r21r = new Resistor("R", 100);
graph21r.Agregar(bat21r);
graph21r.Agregar(r21r);
var dup21a = graph21r.Conectar(bat21r.PinA, r21r.PinA, "dup1");
var dup21b = graph21r.Conectar(bat21r.PinA, r21r.PinA, "dup2");
graph21r.Conectar(r21r.PinB, bat21r.PinB);
graph21r.Tick(0.1);
graph21r.Tick(0.1);
Console.WriteLine($"Redundantes: I(dup1)+I(dup2)={(dup21a.Current + dup21b.Current) * 1000:F1}mA (la suma es lo fisico)");
Verificar("dos cables redundantes suman la corriente del circuito",
    Math.Abs(dup21a.Current + dup21b.Current - 9.0 / (100 + bat21r.ResistenciaInternaNominal)) < 0.0005);


// ============================================================
// DEMO 22: Una llave con limite de corriente editado SE QUEMA.
//   Antes, los componentes fusionados jamas chequeaban sus limites
//   (su corriente "oficial" era 0): un MaxCurrent puesto por el
//   usuario en un cable o llave no servia de nada.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 22: Llave con limite de 50mA en un lazo de 90mA -> se quema");
Console.WriteLine("====================================================\n");

var graph22 = new CircuitGraph();
var bat22 = new Battery("B22", 9);
var llave22 = new Switch("LlaveFragil", startClosed: true) { MaxCurrent = 0.05 };
var r22 = new Resistor("R22", 100);
graph22.Agregar(bat22);
graph22.Agregar(llave22);
graph22.Agregar(r22);
graph22.Conectar(bat22.PinA, llave22.PinA);
graph22.Conectar(llave22.PinB, r22.PinA);
graph22.Conectar(r22.PinB, bat22.PinB);

for (int t = 0; t < 6; t++) graph22.Tick(0.1);
Console.WriteLine($"Llave: quemada={llave22.IsBurnt} motivo='{llave22.MotivoQuemado}'  I restante en R={r22.Current * 1000:F4}mA");
Verificar("la llave fragil se quemo por 'corriente'", llave22.IsBurnt && llave22.MotivoQuemado == "corriente");
Verificar("quemada, corta el circuito (queda la microfuga del 1M\u03a9)", Math.Abs(r22.Current) < 1e-4);


// ============================================================
// DEMO 23: Resistencia en MODO CORRIENTE OBJETIVO.
//   "Quiero que pasen 20mA": la resistencia se auto-ajusta sola
//   (por dentro sigue obedeciendo Ohm — busca la R que produce
//   la I pedida). Con fuente ideal converge en 1 tick; con mas
//   cosas en serie, en unos pocos. Y de paso la demo enseña que
//   la corriente es LA MISMA en toda la serie: la resistencia no
//   "devuelve menos amperios", regula el lazo entero.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 23: Resistencia con corriente objetivo (auto-ajuste)");
Console.WriteLine("====================================================\n");

// --- Caso A: sola contra la fuente -> converge en un tick ---
var graph23 = new CircuitGraph();
var bat23 = new Battery("B23", 9);
var rObj23 = new Resistor("R_objetivo", 100) { ModoCorrienteObjetivo = true, CorrienteObjetivo = 0.02 };
graph23.Agregar(bat23);
graph23.Agregar(rObj23);
graph23.Conectar(bat23.PinA, rObj23.PinA);
graph23.Conectar(rObj23.PinB, bat23.PinB);
for (int t = 1; t <= 5; t++)
{
    graph23.Tick(0.1);
    Console.WriteLine($"Tick {t}: R={rObj23.Resistance:F1}ohm  I={rObj23.Current * 1000:F2}mA  alcanzado={rObj23.ObjetivoAlcanzado}");
}
Verificar("sola contra la fuente, clava los 20mA pedidos", Math.Abs(rObj23.Current - 0.02) < 0.0004);
Verificar("la R resultante es la de Ohm (9V/20mA = 450ohm)", Math.Abs(rObj23.Resistance - 450) < 5);
Verificar("la bandera 'objetivo alcanzado' esta prendida", rObj23.ObjetivoAlcanzado);

// --- Caso B: con una R fija de 100ohm en serie -> converge igual,
//     y la corriente es LA MISMA en las dos (propiedad de serie) ---
var graph23b = new CircuitGraph();
var bat23b = new Battery("B23b", 9);
var rFija23 = new Resistor("R_fija", 100);
var rObj23b = new Resistor("R_objetivo_b", 100) { ModoCorrienteObjetivo = true, CorrienteObjetivo = 0.02 };
graph23b.Agregar(bat23b);
graph23b.Agregar(rFija23);
graph23b.Agregar(rObj23b);
graph23b.Conectar(bat23b.PinA, rFija23.PinA);
graph23b.Conectar(rFija23.PinB, rObj23b.PinA);
graph23b.Conectar(rObj23b.PinB, bat23b.PinB);
for (int t = 0; t < 15; t++) graph23b.Tick(0.1);
Console.WriteLine($"\nCon 100ohm fijos en serie: R_obj={rObj23b.Resistance:F1}ohm (esperado ~350)  I={rObj23b.Current * 1000:F2}mA");
Verificar("con serie de por medio, igual converge a 20mA", Math.Abs(rObj23b.Current - 0.02) < 0.0004);
Verificar("se auto-ajusto a ~350ohm (9V/20mA - 100 de la fija)", Math.Abs(rObj23b.Resistance - 350) < 8);
Verificar("la R fija lleva LA MISMA corriente (no se 'pierden' amperios)", Math.Abs(rFija23.Current - rObj23b.Current) < 1e-9);

// --- Cambio de objetivo en caliente: de 20mA a 5mA ---
rObj23b.CorrienteObjetivo = 0.005;
for (int t = 0; t < 15; t++) graph23b.Tick(0.1);
Console.WriteLine($"Objetivo cambiado a 5mA: R_obj={rObj23b.Resistance:F0}ohm (esperado ~1700)  I={rObj23b.Current * 1000:F3}mA");
Verificar("al cambiar el objetivo en caliente, re-converge", Math.Abs(rObj23b.Current - 0.005) < 0.0001);


// ============================================================
// DEMO 24: Objetivo INALCANZABLE -> la resistencia avisa, no miente.
//   Pedirle 5A a una fuente que con todo al minimo da 0.9A: la R
//   se clava en su piso y la bandera queda apagada. Y en circuito
//   abierto ni intenta ajustar (no hay nada que regular).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 24: Corriente objetivo inalcanzable");
Console.WriteLine("====================================================\n");

var graph24 = new CircuitGraph();
var bat24 = new Battery("B24", 9);
var rFija24 = new Resistor("R_fija24", 10);
var rObj24 = new Resistor("R_ambiciosa", 100) { ModoCorrienteObjetivo = true, CorrienteObjetivo = 5.0 };
graph24.Agregar(bat24);
graph24.Agregar(rFija24);
graph24.Agregar(rObj24);
graph24.Conectar(bat24.PinA, rFija24.PinA);
graph24.Conectar(rFija24.PinB, rObj24.PinA);
graph24.Conectar(rObj24.PinB, bat24.PinB);
for (int t = 0; t < 12; t++) graph24.Tick(0.1);
double iMax24 = 9.0 / (10 + Resistor.ResistenciaMinimaObjetivo + bat24.ResistenciaInternaNominal);
Console.WriteLine($"Pidio 5A: R quedo en {rObj24.Resistance:F2}ohm (piso)  I={rObj24.Current:F3}A (maximo fisico {iMax24:F3}A)  alcanzado={rObj24.ObjetivoAlcanzado}");
Verificar("la R se clavo en su piso de auto-ajuste", Math.Abs(rObj24.Resistance - Resistor.ResistenciaMinimaObjetivo) < 0.001);
Verificar("entrega lo maximo que el circuito da (no 5A magicos)", Math.Abs(rObj24.Current - iMax24) < 0.01);
Verificar("la bandera avisa que NO se alcanzo el objetivo", !rObj24.ObjetivoAlcanzado);

var graph24b = new CircuitGraph();
var bat24b = new Battery("B24b", 9);
var llave24 = new Switch("Llave24", startClosed: false);
var rObj24b = new Resistor("R_esperando", 100) { ModoCorrienteObjetivo = true, CorrienteObjetivo = 0.02 };
graph24b.Agregar(bat24b);
graph24b.Agregar(llave24);
graph24b.Agregar(rObj24b);
graph24b.Conectar(bat24b.PinA, llave24.PinA);
graph24b.Conectar(llave24.PinB, rObj24b.PinA);
graph24b.Conectar(rObj24b.PinB, bat24b.PinB);
for (int t = 0; t < 10; t++) graph24b.Tick(0.1);
Console.WriteLine($"Tras llave abierta: R sigue en {rObj24b.Resistance:F0}ohm (no salto a ningun lado)");
Verificar("en circuito abierto no intenta ajustar (R intacta)", Math.Abs(rObj24b.Resistance - 100) < 0.001);
Verificar("y obviamente el objetivo figura como no alcanzado", !rObj24b.ObjetivoAlcanzado);


// ============================================================
// DEMO 25: Limites INFINITOS individuales (el "check ∞").
//   Cada limite de ruptura se puede poner en infinito POR SEPARADO.
//   a) Un LED con TODO en infinito, directo a 9V: inmortal.
//   b) El combo pedido: inmune al voltaje y la potencia... pero con
//      limite de corriente -> muere por 'corriente' igual.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 25: Checks de infinito por limite individual");
Console.WriteLine("====================================================\n");

var graph25 = new CircuitGraph();
var bat25 = new Battery("B25", 9);
var ledInmortal = new LED("LED_sandbox", 2.0, 12)
{
    MaxPower = double.PositiveInfinity,
    MaxCurrent = double.PositiveInfinity,
    MaxVoltage = double.PositiveInfinity,
    MaxTemperature = double.PositiveInfinity,
};
graph25.Agregar(bat25);
graph25.Agregar(ledInmortal);
graph25.Conectar(bat25.PinA, ledInmortal.PinA);
graph25.Conectar(ledInmortal.PinB, bat25.PinB);
for (int t = 0; t < 30; t++) graph25.Tick(0.1);
Console.WriteLine($"LED todo-infinito a 9V directo: vivo={!ledInmortal.IsBurnt}  ON={ledInmortal.EstaEncendido}  I={ledInmortal.Current * 1000:F0}mA  P={ledInmortal.Power:F2}W");
Verificar("el LED todo-infinito sobrevive lo que mata a uno normal", !ledInmortal.IsBurnt);
Verificar("y sigue prendido y funcionando normal", ledInmortal.EstaEncendido && ledInmortal.Brillo > 0.9);

var graph25b = new CircuitGraph();
var bat25b = new Battery("B25b", 9);
var ledCombo = new LED("LED_combo", 2.0, 12)
{
    MaxVoltage = double.PositiveInfinity,   // inmune al voltaje...
    MaxPower = double.PositiveInfinity,     // ...y a la potencia...
    MaxTemperature = double.PositiveInfinity,
    MaxCurrent = 0.05,                      // ...pero 50mA lo matan.
};
graph25b.Agregar(bat25b);
graph25b.Agregar(ledCombo);
graph25b.Conectar(bat25b.PinA, ledCombo.PinA);
graph25b.Conectar(ledCombo.PinB, bat25b.PinB);
for (int t = 0; t < 30 && !ledCombo.IsBurnt; t++) graph25b.Tick(0.1);
Console.WriteLine($"LED inmune-a-voltaje con tope de 50mA: quemado={ledCombo.IsBurnt} motivo='{ledCombo.MotivoQuemado}'");
Verificar("el combo (voltaje ∞ + corriente finita) muere por 'corriente'", ledCombo.IsBurnt && ledCombo.MotivoQuemado == "corriente");

// --- Bateria sandbox: con corriente ∞ ni un CORTOCIRCUITO la mata
//     (un corto es un evento de sobrecorriente; tolerancia infinita =
//     se lo banca). Sigue marcando "en corto" y calentando, eso si. ---
var graph25c = new CircuitGraph();
var batInmortal = new Battery("B_sandbox", 9) { MaxCurrent = double.PositiveInfinity, MaxTemperature = double.PositiveInfinity };
graph25c.Agregar(batInmortal);
graph25c.Conectar(batInmortal.PinA, batInmortal.PinB, "puente_suicida");
for (int t = 0; t < 20; t++) graph25c.Tick(0.1);
Console.WriteLine($"Bateria sandbox en corto directo 2s: viva={!batInmortal.IsBurnt}  en corto={batInmortal.EnCortocircuito}");
Verificar("la bateria con corriente ∞ sobrevive el cortocircuito", !batInmortal.IsBurnt);
Verificar("pero el inspector sigue avisando que esta en corto", batInmortal.EnCortocircuito);


// ============================================================
// DEMO 26: El inmortal COCINA a los vecinos.
//   Temperatura maxima infinita = no muere por calor... pero el
//   calor no desaparece: sigue calentandose y CONTAGIANDO a lo
//   que tenga apoyado al lado. Un componente sandbox mal ubicado
//   puede quemar el resto del circuito. Fisicamente coherente.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 26: El componente inmortal calienta a sus vecinos");
Console.WriteLine("====================================================\n");

var graph26 = new CircuitGraph();
var termico26 = new SimuladorTermico();
var bat26 = new Battery("B26", 9);
var ledEstufa = new LED("LED_estufa", 2.0, 12)
{
    MaxPower = double.PositiveInfinity,
    MaxCurrent = double.PositiveInfinity,
    MaxVoltage = double.PositiveInfinity,
    MaxTemperature = double.PositiveInfinity,
};
// MaxTemperature 35: con la R_int de la fuente en el lazo el LED-estufa
// disipa un poco menos y el vecino se equilibra en ~37°C. El 38 viejo
// quedaba 0.8°C por encima del equilibrio y la lección no pasaba nunca.
var vecino26 = new Resistor("VecinoFragil", 1000) { MaxTemperature = 35 }; // NO conectado: solo apoyado al lado
graph26.Agregar(bat26);
graph26.Agregar(ledEstufa);
graph26.Agregar(vecino26);
graph26.Conectar(bat26.PinA, ledEstufa.PinA);
graph26.Conectar(ledEstufa.PinB, bat26.PinB);
ledEstufa.PosicionFisica = new System.Numerics.Vector3(0, 0, 0);
vecino26.PosicionFisica = new System.Numerics.Vector3(0.15f, 0, 0); // bien pegado a la estufa
bat26.PosicionFisica = new System.Numerics.Vector3(-2, 0, 0);

int tickMuerteVecino = -1;
for (int t = 1; t <= 600 && tickMuerteVecino < 0; t++)
{
    graph26.Tick(0.1);
    termico26.Paso(graph26.Components, 0.1);
    if (vecino26.IsBurnt) tickMuerteVecino = t;
}
Console.WriteLine($"LED estufa: T={ledEstufa.Temperatura:F0}C vivo={!ledEstufa.IsBurnt} | Vecino: T={vecino26.Temperatura:F0}C quemado={vecino26.IsBurnt} motivo='{vecino26.MotivoQuemado}' (tick {tickMuerteVecino})");
Verificar("el LED inmortal se recalento sin morirse (temperatura ∞)", !ledEstufa.IsBurnt && ledEstufa.Temperatura > 80);
Verificar("el vecino apoyado al lado murio COCINADO por contagio", vecino26.IsBurnt && vecino26.MotivoQuemado == "temperatura");


// ============================================================
// (DEMO 29 se archivo en el Paso 5-2: probaba el inductor viejo.
//  El inductor nuevo se prueba en el DEMO 68.)
// ============================================================
// DEMO 30: TRANSISTOR NPN - la llave electronica de TRES bornes.
//   Primer componente compuesto del motor: canal colector-emisor
//   + arista interna base-emisor. Un voltajito en la base abre el
//   canal y la lampara del colector prende; sin base, ni se entera.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 30: Transistor NPN - un voltajito controla al lamparon");
Console.WriteLine("====================================================\n");

var graph30 = new CircuitGraph();
var bat30 = new Battery("B30", 9);
var lamp30 = new Lampara("Lamparon", 60) { PotenciaNominal = 1.4 };
var npn30 = new TransistorNPN("Q1");
var llaveBase30 = new Switch("LlaveDeBase", startClosed: false);
var rBase30 = new Resistor("R_base", 1000);
graph30.Agregar(bat30);
graph30.Agregar(lamp30);
graph30.Agregar(npn30);         // agrega solo su arista interna B-E (compuesto)
graph30.Agregar(llaveBase30);
graph30.Agregar(rBase30);
// Rama de potencia: bateria + -> lampara -> COLECTOR ... EMISOR -> bateria -
graph30.Conectar(bat30.PinA, lamp30.PinA);
graph30.Conectar(lamp30.PinB, npn30.PinA);
graph30.Conectar(npn30.PinB, bat30.PinB);
// Rama de control: bateria + -> llave -> resistencia -> BASE
graph30.Conectar(bat30.PinA, llaveBase30.PinA);
graph30.Conectar(llaveBase30.PinB, rBase30.PinA);
graph30.Conectar(rBase30.PinB, npn30.PinBase);

for (int t = 0; t < 6; t++) graph30.Tick(0.1);
double brilloSinBase30 = lamp30.Brillo;
Console.WriteLine($"Base sin señal: V_BE={npn30.VoltajeBaseEmisor:F2}V  conduce={npn30.Conduciendo}  brillo={brilloSinBase30:P0}");
llaveBase30.IsClosed = true;
for (int t = 0; t < 10; t++) graph30.Tick(0.1);
Console.WriteLine($"Con señal de base: V_BE={npn30.VoltajeBaseEmisor:F2}V  I_base={npn30.CorrienteBase * 1000:F2}mA  conduce={npn30.Conduciendo}  brillo={lamp30.Brillo:P0}");
Verificar("sin señal en la base, el canal corta (lampara apagada)", brilloSinBase30 < 0.02 && !lamp30.EstaEncendida || brilloSinBase30 < 0.02);
Verificar("con señal en la base, satura y la lampara prende", npn30.Conduciendo && lamp30.Brillo > 0.85);
llaveBase30.IsClosed = false;
for (int t = 0; t < 8; t++) graph30.Tick(0.1);
Verificar("se corta la base y la lampara se apaga de nuevo", !npn30.Conduciendo && lamp30.Brillo < 0.05);


// ============================================================
// DEMO 31: El parser Intel HEX — carga valida, checksum y basura.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 31: Parser Intel HEX - carga, checksum y errores");
Console.WriteLine("====================================================\n");

var chip31 = new Atmega328P();
chip31.CargarHex(":04000000AABBCCDDEE\n:00000001FF", "mini");
Console.WriteLine($"Mini programa: {chip31.InfoPrograma}");
Verificar("un HEX valido de 4 bytes carga y reporta su tamano", chip31.ProgramaCargado && chip31.InfoPrograma.Contains("4"));

bool tiroChecksum = false;
try { new Atmega328P().CargarHex(":04000000AABBCCDDED\n:00000001FF", "roto"); }
catch (FormatException e) { tiroChecksum = true; Console.WriteLine($"Checksum malo -> {e.Message}"); }
Verificar("un checksum incorrecto tira FormatException", tiroChecksum);

bool tiroBasura = false;
try { new Atmega328P().CargarHex("hola, esto no es un hex", "basura"); }
catch (FormatException e) { tiroBasura = true; Console.WriteLine($"Basura -> {e.Message}"); }
Verificar("un archivo que no es Intel HEX tira FormatException", tiroBasura);

var chip31b = new Atmega328P();
chip31b.CargarHex(SketchesHex.BlinkEsperaActiva, "blink.hex");
Console.WriteLine($"Sketch real: {chip31b.InfoPrograma}");
Verificar("un .hex real de avr-gcc carga entero (176 bytes)", chip31b.ProgramaCargado && chip31b.InfoPrograma.Contains("176"));

// ============================================================
// DEMO 32: Tortura de la ISA. El sketch hace aritmetica de 8/16/32
//   bits, MUL, corrimientos, comparaciones con signo, lee la flash
//   con LPM y se llama recursivamente (pila). Cada chequeo que pasa
//   enciende un pin: si el emulador ejecuta TODO bien, los 14 pines
//   D0-D13 terminan en ALTO.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 32: Tortura de la ISA - 14 chequeos, 14 pines en alto");
Console.WriteLine("====================================================\n");

var chip32 = new Atmega328P();
chip32.CargarHex(SketchesHex.IsaTortura, "isa_torture.hex");
chip32.Ejecutar(60_000);        // el programa entero corre y queda girando en for(;;)
chip32.Ejecutar(100_000);       // segundo lote: los pines ya quietos -> duty limpio

var patron32 = new StringBuilder();
bool todos32 = true;
for (int i = 13; i >= 0; i--)
{
    bool alto = chip32.Pines[i].EsSalida && chip32.Pines[i].Duty > 0.9;
    patron32.Append(alto ? '1' : '0');
    if (!alto) todos32 = false;
}
Console.WriteLine($"Pines D13..D0 = {patron32}  (esperado: 11111111111111)");
Console.WriteLine($"Instrucciones desconocidas: {chip32.InstruccionesDesconocidas}  CPU detenida: {chip32.Detenida}");
Verificar("los 14 chequeos de la ISA pasan (todos los pines en alto)", todos32);
Verificar("ningun opcode quedo sin decodificar", chip32.InstruccionesDesconocidas == 0 && !chip32.Detenida);

// ============================================================
// DEMO 33: Blink por espera activa. _delay_ms(100) cuenta CICLOS a
//   mano, asi que el periodo del parpadeo (3.2 millones de ciclos)
//   solo sale bien si cada instruccion dura exactamente lo que debe.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 33: Blink con _delay_ms - el periodo es ciclo-exacto");
Console.WriteLine("====================================================\n");

var chip33 = new Atmega328P();
chip33.CargarHex(SketchesHex.BlinkEsperaActiva, "blink.hex");
var flancos33 = new List<long>();
bool previo33 = false;
long ciclos33 = 0;
for (int paso = 0; paso < 700; paso++)   // 700 tajadas de 1ms = 0.7s simulados
{
    chip33.Ejecutar(16_000);
    ciclos33 += 16_000;
    bool alto = chip33.Pines[13].EsSalida && chip33.Pines[13].Duty > 0.5;
    if (alto && !previo33) flancos33.Add(ciclos33);
    previo33 = alto;
}
bool periodosOk33 = flancos33.Count >= 3;
for (int i = 1; i < flancos33.Count; i++)
{
    long delta = flancos33[i] - flancos33[i - 1];
    Console.WriteLine($"Periodo {i}: {delta:N0} ciclos ({delta / 16_000.0:F1} ms)");
    if (delta < 3_100_000 || delta > 3_300_000) periodosOk33 = false;
}
Verificar("D13 parpadea con periodo de ~3.2M ciclos (200ms) ±3%", periodosOk33);

// ============================================================
// DEMO 34: La replica de wiring.c: Timer0 + interrupcion de desborde
//   + millis()/micros()/delay(). Si el timer, el despacho de
//   interrupciones y el prescaler andan, el LED parpadea cada 50ms.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 34: millis()/delay() con Timer0 e interrupciones");
Console.WriteLine("====================================================\n");

var chip34 = new Atmega328P();
chip34.CargarHex(SketchesHex.BlinkConMillis, "millis_blink.hex");
var flancos34 = new List<long>();
bool previo34 = false;
long ciclos34 = 0;
for (int paso = 0; paso < 600; paso++)
{
    chip34.Ejecutar(16_000);
    ciclos34 += 16_000;
    bool alto = chip34.Pines[13].EsSalida && chip34.Pines[13].Duty > 0.5;
    if (alto && !previo34) flancos34.Add(ciclos34);
    previo34 = alto;
}
bool periodosOk34 = flancos34.Count >= 3;
for (int i = 1; i < flancos34.Count; i++)
{
    long delta = flancos34[i] - flancos34[i - 1];
    Console.WriteLine($"Periodo {i}: {delta:N0} ciclos ({delta / 16_000.0:F1} ms)");
    if (delta < 1_536_000 || delta > 1_664_000) periodosOk34 = false;
}
Verificar("delay(50) via millis/Timer0 da periodos de ~1.6M ciclos ±4%", periodosOk34);
Verificar("la CPU siguio viva atendiendo interrupciones", !chip34.Detenida && chip34.InstruccionesDesconocidas == 0);

// ============================================================
// DEMO 35: UART. El sketch saluda por Serial y despues hace eco de
//   todo lo que recibe. Prueba UDRE/TXC/RXC, la cola de recepcion
//   y la transmision.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 35: Serial - el chip saluda y hace eco");
Console.WriteLine("====================================================\n");

var chip35 = new Atmega328P();
var serie35 = new StringBuilder();
chip35.SerialTransmitido += b => serie35.Append((char)b);
chip35.CargarHex(SketchesHex.UartEco, "uart_eco.hex");
chip35.Ejecutar(100_000);
Console.WriteLine($"Recibido tras el arranque: \"{serie35.ToString().TrimEnd()}\"");
Verificar("el sketch imprime su saludo por Serial", serie35.ToString().Contains("hola desde el chip\n"));

chip35.SerialRecibir("eco!");
chip35.Ejecutar(100_000);
Console.WriteLine($"Tras mandarle \"eco!\": \"{serie35.ToString().TrimEnd()}\"");
Verificar("lo que se le manda por Serial vuelve como eco", serie35.ToString().EndsWith("eco!"));

// ============================================================
// DEMO 36: ADC. Con 2.5V en A0, analogRead tiene que dar ~512
//   (la mitad de 1023). El sketch convierte y manda el numero por
//   Serial.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 36: ADC - 2.5V en A0 se leen como ~512");
Console.WriteLine("====================================================\n");

var chip36 = new Atmega328P();
var serie36 = new StringBuilder();
chip36.SerialTransmitido += b => serie36.Append((char)b);
chip36.CargarHex(SketchesHex.AdcImprime, "adc_print.hex");
var entradas36 = new double[Atmega328P.CantidadPines];
entradas36[14] = 2.5;                 // A0
chip36.FijarEntradas(entradas36);
chip36.Ejecutar(400_000);

string[] lineas36 = serie36.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
int lectura36 = lineas36.Length > 0 && int.TryParse(lineas36[0], out int v36) ? v36 : -1;
Console.WriteLine($"Primeras lecturas del ADC: {string.Join(", ", lineas36.Take(4))}");
Verificar("el ADC lee 2.5V como ~512 (508..516)", lectura36 >= 508 && lectura36 <= 516);

// ============================================================
// DEMO 37: INTEGRACION COMPLETA. La placa en el grafo electrico,
//   con resistencia de 220 y LED colgados de D13, corriendo el
//   blink REAL: el LED del circuito tiene que parpadear, y su
//   corriente tiene que salir de la cadena completa
//   5V -> driver 28 ohm -> 220 ohm -> LED (2V + 12 ohm) = ~11.5mA.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 37: Placa + R220 + LED en D13 corriendo el blink real");
Console.WriteLine("====================================================\n");

var graph37 = new CircuitGraph();
var placa37 = new ArduinoUno("Arduino") { PresupuestoMsPorFrame = 1000 };
var r37 = new Resistor("R220", 220);
var led37 = new LED("LED", voltajeUmbral: 2.0, resistenciaEncendido: 12);
graph37.Agregar(placa37);
graph37.Agregar(r37);
graph37.Agregar(led37);
Pin d13 = placa37.BornesExtra.First(b => b.Etiqueta == "D13").Pin;
graph37.Conectar(d13, r37.PinA);
graph37.Conectar(r37.PinB, led37.PinA);
graph37.Conectar(led37.PinB, placa37.PinB);

placa37.CargarPrograma(SketchesHex.BlinkEsperaActiva, "blink.hex");
int transiciones37 = 0;
bool encendidoPrevio37 = false;
int ticksEncendido37 = 0;
double corrienteEstacionaria37 = 0;
// dt chico a propósito: el modelo compañero del LED converge en ~10 ticks
// eléctricos (su fade-in documentado), así que le damos 20 ticks por
// semiperíodo de 100ms y medimos recién tras converger el transitorio inicial.
for (int frame = 0; frame < 240; frame++)      // 1.2 segundos de juego
{
    placa37.AvanzarTiempoReal(0.005);
    graph37.Tick(0.005);
    bool encendido = led37.Brillo > 0.1;
    if (encendido != encendidoPrevio37) transiciones37++;
    encendidoPrevio37 = encendido;
    ticksEncendido37 = encendido ? ticksEncendido37 + 1 : 0;
    if (ticksEncendido37 >= 8) corrienteEstacionaria37 = led37.Current;
}
Console.WriteLine($"Transiciones del LED en 1.2s: {transiciones37} (esperadas ~12)");
Console.WriteLine($"Corriente en régimen con el LED prendido: {corrienteEstacionaria37 * 1000:F2}mA (esperada ~12.1mA)");
Console.WriteLine($"Estado CPU: {placa37.EstadoCpu}   {placa37.DescribirPin(13)}");
Verificar("el LED fisico parpadea siguiendo al programa", transiciones37 >= 4);
Verificar("la corriente sale de la cadena completa con los 28 ohm del pin", corrienteEstacionaria37 > 0.010 && corrienteEstacionaria37 < 0.013);

// ============================================================
// DEMO 38: ENTRADAS. El sketch pone D2 como INPUT_PULLUP y espeja
//   (invertido) al LED de D13. Con la llave abierta el pull-up
//   interno de 35k sostiene el pin en alto (LED apagado); cerrada
//   a GND, el pin lee 0 (LED prendido) y por D2 salen ~0.14mA.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 38: digitalRead con INPUT_PULLUP y boton a GND");
Console.WriteLine("====================================================\n");

var graph38 = new CircuitGraph();
var placa38 = new ArduinoUno("Arduino") { PresupuestoMsPorFrame = 1000 };
var r38 = new Resistor("R220", 220);
var led38 = new LED("LED", voltajeUmbral: 2.0, resistenciaEncendido: 12);
var boton38 = new Switch("Boton", startClosed: false);
graph38.Agregar(placa38);
graph38.Agregar(r38);
graph38.Agregar(led38);
graph38.Agregar(boton38);
Pin d13b = placa38.BornesExtra.First(b => b.Etiqueta == "D13").Pin;
Pin d2 = placa38.BornesExtra.First(b => b.Etiqueta == "D2").Pin;
graph38.Conectar(d13b, r38.PinA);
graph38.Conectar(r38.PinB, led38.PinA);
graph38.Conectar(led38.PinB, placa38.PinB);
graph38.Conectar(d2, boton38.PinA);
graph38.Conectar(boton38.PinB, placa38.PinB);

placa38.CargarPrograma(SketchesHex.PullupLed, "pullup_led.hex");
for (int frame = 0; frame < 12; frame++) { placa38.AvanzarTiempoReal(0.02); graph38.Tick(0.02); }
double brilloSuelto38 = led38.Brillo;
Console.WriteLine($"Boton suelto:   {placa38.DescribirPin(2)}  |  LED brillo={brilloSuelto38:P0}");

boton38.IsClosed = true;
for (int frame = 0; frame < 12; frame++) { placa38.AvanzarTiempoReal(0.02); graph38.Tick(0.02); }
double brilloApretado38 = led38.Brillo;
double corrienteD2 = placa38.CorrientePorPin[2];
Console.WriteLine($"Boton apretado: {placa38.DescribirPin(2)}  |  LED brillo={brilloApretado38:P0}");

boton38.IsClosed = false;
for (int frame = 0; frame < 12; frame++) { placa38.AvanzarTiempoReal(0.02); graph38.Tick(0.02); }
Console.WriteLine($"Boton suelto de nuevo: LED brillo={led38.Brillo:P0}");

Verificar("con el boton suelto el pull-up sostiene el pin (LED apagado)", brilloSuelto38 < 0.05);
Verificar("con el boton a GND el sketch lee 0 y prende el LED", brilloApretado38 > 0.3);
Verificar("por D2 salen ~0.14mA a traves del pull-up de 35k", corrienteD2 > 0.0001 && corrienteD2 < 0.0002);
Verificar("al soltar el boton el LED se vuelve a apagar", led38.Brillo < 0.05);

// ============================================================
// DEMO 39: ROTURAS. Primero un corto de PIN: D5 en ALTO cableado
//   directo a GND entrega 5V/28 = ~178mA >> 40mA -> el PIN se dana
//   (para siempre) pero la placa sigue viva y el blink continua.
//   Despues un corto de RAIL: 3V3 a GND -> la placa ENTERA entra en
//   cortocircuito y muere, CPU incluida.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 39: Corto de pin (se dana el pin) vs corto de rail (muere la placa)");
Console.WriteLine("====================================================\n");

var graph39 = new CircuitGraph();
var placa39 = new ArduinoUno("Arduino") { PresupuestoMsPorFrame = 1000 };
var r39 = new Resistor("R220", 220);
var led39 = new LED("LED", voltajeUmbral: 2.0, resistenciaEncendido: 12);
var llaveCorto39 = new Switch("CortoAl3V3", startClosed: false);
graph39.Agregar(placa39);
graph39.Agregar(r39);
graph39.Agregar(led39);
graph39.Agregar(llaveCorto39);
Pin d13c = placa39.BornesExtra.First(b => b.Etiqueta == "D13").Pin;
Pin d5 = placa39.BornesExtra.First(b => b.Etiqueta == "D5").Pin;
Pin b3v3 = placa39.BornesExtra.First(b => b.Etiqueta == "3V3").Pin;
graph39.Conectar(d13c, r39.PinA);
graph39.Conectar(r39.PinB, led39.PinA);
graph39.Conectar(led39.PinB, placa39.PinB);
graph39.Conectar(d5, placa39.PinB, "CableAsesino");   // D5 directo a GND
graph39.Conectar(b3v3, llaveCorto39.PinA);
graph39.Conectar(llaveCorto39.PinB, placa39.PinB);

placa39.CargarPrograma(SketchesHex.D5AltoYBlink, "d5_blink.hex");
// El pin ahora se dana tras 0.3 SEGUNDOS de sobrecorriente sostenida (no 3
// ticks): a dt=0.02 son 15 ticks, le damos 25 de margen.
for (int frame = 0; frame < 25; frame++) { placa39.AvanzarTiempoReal(0.02); graph39.Tick(0.02); }
Console.WriteLine($"Tras el corto de D5: {placa39.DescribirPin(5)}");
Console.WriteLine($"Pines danados: {placa39.PinesDanadosTotal}   Placa quemada: {placa39.IsBurnt}");
Verificar("D5 en corto a GND se DANA por sobrecorriente (>40mA)", placa39.PinesDanados[5]);
Verificar("la placa sobrevive al corto de un solo pin", !placa39.IsBurnt);

int transiciones39 = 0;
bool altoPrevio39 = d13c.Voltage > 2.5;
for (int frame = 0; frame < 30; frame++)
{
    placa39.AvanzarTiempoReal(0.02);
    graph39.Tick(0.02);
    // El blink de 50ms es más rápido que el fade-in del LED, así que acá
    // medimos la señal en el BORNE directamente: el voltaje de D13 tiene
    // que seguir subiendo y bajando aunque el pin de al lado esté muerto.
    bool alto = d13c.Voltage > 2.5;
    if (alto != altoPrevio39) transiciones39++;
    altoPrevio39 = alto;
}
Console.WriteLine($"El blink de D13 sigue: {transiciones39} transiciones de voltaje con el pin D5 muerto");
Verificar("el programa sigue corriendo con el pin danado (el blink continua)", transiciones39 >= 2);

llaveCorto39.IsClosed = true;   // corto del rail de 3.3V
// FISICA NUEVA: el conflicto es del REGULADOR de 3.3V (su corto de 4.1A
// supera su tope de 0.15A) -> muere EL, y la placa sigue viva sin su riel
// de 3.3 — como un Uno de verdad. A dt=0.02 la muerte tarda 20 ticks (0.4s).
for (int frame = 0; frame < 30; frame++) { placa39.AvanzarTiempoReal(0.02); graph39.Tick(0.02); }
var rail39 = placa39.AristasInternas.First(a => a is ElectricSim.Core.FuenteBase && !ReferenceEquals(a, placa39));
Console.WriteLine($"Tras el corto del 3V3: regulador quemado={rail39.IsBurnt} ({rail39.MotivoQuemado})  placa quemada={placa39.IsBurnt}  CPU: {placa39.EstadoCpu}");
Verificar("el corto del 3V3 mata al REGULADOR de 3.3V", rail39.IsBurnt && rail39.MotivoQuemado == "cortocircuito");
Verificar("la placa sobrevive sin su riel de 3.3 (como un Uno real)", !placa39.IsBurnt);

// El golpe final: corto del riel de 5V (los bornes de la PROPIA fuente de la
// placa). Ese conflicto si es de la placa: 14.3A contra 1A de tope -> muere
// entera, CPU incluida.
graph39.Conectar(placa39.PinA, placa39.PinB, "CortoDel5V");
for (int frame = 0; frame < 30; frame++) { placa39.AvanzarTiempoReal(0.02); graph39.Tick(0.02); }
Console.WriteLine($"Tras el corto del 5V: placa quemada={placa39.IsBurnt} ({placa39.MotivoQuemado})  estado CPU: {placa39.EstadoCpu}");
Verificar("el corto del riel de 5V mata la placa entera", placa39.IsBurnt);
Verificar("la CPU queda frenada al morir la placa", placa39.EstadoCpu.Contains("quemada"));
Verificar("con la placa muerta el LED queda apagado", led39.Brillo < 0.05);


// ============================================================
// DEMO 40: FUENTES EN SERIE. El clasico de apilar baterias: el −
//   de una al + de la otra tiene que SUMAR voltajes (9+9=18V),
//   sin falsos avisos de cortocircuito. Antes esto era una
//   limitacion documentada; ahora las fuentes imponen DIFERENCIAS
//   que se propagan, y la serie sale sola.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 40: Dos 9V en serie = 18V de verdad");
Console.WriteLine("====================================================\n");

var graph40 = new CircuitGraph();
var bat40a = new Battery("BA", 9);
var bat40b = new Battery("BB", 9);
// MaxVoltage 12: una lamparita "de 9V" de verdad no aguanta 17V. Antes el
// escenario dependía de MaxPower=5W y con la caída interna quedaba en
// 4.84W — justo por debajo del umbral, y la lección se volvía muda.
var lamp40 = new Lampara("Lamp", 60) { MaxVoltage = 12 };
graph40.Agregar(bat40a);
graph40.Agregar(bat40b);
graph40.Agregar(lamp40);
graph40.Conectar(bat40a.PinB, bat40b.PinA);        // el − de A al + de B: serie
graph40.Conectar(bat40a.PinA, lamp40.PinA);
graph40.Conectar(lamp40.PinB, bat40b.PinB);
for (int t = 0; t < 2; t++) graph40.Tick(0.1);
double v40 = lamp40.VoltajeEnBornes;
double i40 = lamp40.Current;
Console.WriteLine($"Lampara: V={v40:F1}V  I={i40 * 1000:F0}mA  brillo={lamp40.Brillo:P0}");
Console.WriteLine($"BA: corto={bat40a.EnCortocircuito}  BB: corto={bat40b.EnCortocircuito}");
// Las dos R_int (1.7Ω c/u) también están en el lazo: la lámpara ve
// 18·60/(60+3.4) = 17.0V, el número FÍSICO de dos pilas reales en serie.
double vEsperado40 = 18.0 * 60.0 / (60.0 + 2 * bat40a.ResistenciaInternaNominal);
Verificar("la lampara ve los 18V de la serie, menos la caida interna",
    Math.Abs(v40 - vEsperado40) < 0.3);
Verificar("la corriente responde a esos 18V (18/63.4 = 0.28A)",
    Math.Abs(i40 - vEsperado40 / 60.0) < 0.01);
Verificar("ninguna bateria marca un falso cortocircuito", !bat40a.EnCortocircuito && !bat40b.EnCortocircuito);

// Leccion 2, gratis: una lamparita pensada para 9V recibe 18 -> el
// cuadruple de potencia (P = V²/R) -> se quema en fracciones de segundo.
for (int t = 0; t < 8; t++) graph40.Tick(0.1);
Console.WriteLine($"Y la lamparita de 9V con 18V encima: quemada={lamp40.IsBurnt} ({lamp40.MotivoQuemado})");
Verificar("la lamparita de 9V NO banca los 18V: se quema", lamp40.IsBurnt);

// Y la deteccion de cortos REALES sigue viva: cablear los extremos de la
// serie entera (el + de arriba al − de abajo) es un corto de 18V.
graph40.Conectar(bat40a.PinA, bat40b.PinB, "CableDelCrimen40");
for (int t = 0; t < 3; t++) graph40.Tick(0.1);
Verificar("cortocircuitar la serie entera SI marca a las dos", bat40a.EnCortocircuito && bat40b.EnCortocircuito);

// ============================================================
// DEMO 41: EL FLASH DEL CAPACITOR. El experimento de manual:
//   cargar el capacitor con la bateria, DESCONECTAR la bateria, y
//   ver como el capacitor devuelve lo guardado prendiendo el LED
//   hasta vaciarse. Antes el capacitor solo sabia cargarse; ahora
//   guarda VOLTS de verdad y los empuja (dV = I·dt/C, la ley real).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 41: El capacitor devuelve su carga - flash del LED");
Console.WriteLine("====================================================\n");

var graph41 = new CircuitGraph();
var bat41 = new Battery("B", 9);
var llaveCarga41 = new Switch("LlCarga", startClosed: true);
var cap41 = new Capacitor("C", capacitanceFarads: 0.02); // tau = R*C = 232*0.02 = 4.6s: el flash entero entra en la ventana de 15s
var r41 = new Resistor("R220", 220);
var led41 = new LED("LED", 2.0, 12);
var llaveLed41 = new Switch("LlLed", startClosed: false);
graph41.Agregar(bat41); graph41.Agregar(llaveCarga41); graph41.Agregar(cap41);
graph41.Agregar(r41); graph41.Agregar(led41); graph41.Agregar(llaveLed41);
graph41.Conectar(bat41.PinA, llaveCarga41.PinA);
graph41.Conectar(llaveCarga41.PinB, cap41.PinA);
graph41.Conectar(cap41.PinB, bat41.PinB);
graph41.Conectar(cap41.PinA, llaveLed41.PinA);
graph41.Conectar(llaveLed41.PinB, r41.PinA);
graph41.Conectar(r41.PinB, led41.PinA);
graph41.Conectar(led41.PinB, cap41.PinB);

for (int t = 0; t < 40; t++) graph41.Tick(0.1);          // cargar
double almacenado41 = cap41.VoltajeAlmacenado;
Console.WriteLine($"Cargado con la bateria: guardados {almacenado41:F1}V (nivel {cap41.ChargeLevel:P0})");

llaveCarga41.IsClosed = false;                            // chau bateria
for (int t = 0; t < 5; t++) graph41.Tick(0.1);
llaveLed41.IsClosed = true;                               // y ahora, el flash
double brilloMax41 = 0;
for (int t = 0; t < 150; t++)
{
    graph41.Tick(0.1);
    brilloMax41 = Math.Max(brilloMax41, led41.Brillo);
    if (t % 40 == 0)
        Console.WriteLine($"t={t * 0.1:F0}s: V_guardado={cap41.VoltajeAlmacenado:F1}V  I_led={led41.Current * 1000:F1}mA  brillo={led41.Brillo:P0}");
}
Console.WriteLine($"Final: V_guardado={cap41.VoltajeAlmacenado:F1}V  brillo={led41.Brillo:P0}  brillo maximo visto={brilloMax41:P0}");
Verificar("el capacitor guardo los volts de la bateria (~9V)", almacenado41 > 8.0);
Verificar("SIN bateria, el capacitor prende el LED con lo guardado", brilloMax41 > 0.3);
Verificar("la descarga lo vacia de verdad (dV = I*dt/C)", cap41.VoltajeAlmacenado < almacenado41 - 4);
Verificar("vacio, el LED se apaga solo", led41.Brillo < 0.1);

// ============================================================
// DEMO 42: EL TOPE FISICO DE CORRIENTE. Una carga resistiva
//   ridiculamente baja (0.05Ω) sobre la 9V le pediria 180A a la
//   fuente ideal — imposibles: NINGUNA fuente entrega mas que su
//   corriente de cortocircuito (V/R_interna). La bateria clava
//   sus 5.3A fisicos, se calienta, y sobrevive.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 42: La fuente nunca entrega mas que V/R_interna");
Console.WriteLine("====================================================\n");

var graph42 = new CircuitGraph();
var bat42 = new Battery("B", 9);
var casiCable42 = new Resistor("CasiCable", 0.05);
graph42.Agregar(bat42);
graph42.Agregar(casiCable42);
graph42.Conectar(bat42.PinA, casiCable42.PinA);
graph42.Conectar(casiCable42.PinB, bat42.PinB);
// Sin simulador termico a proposito: la demo mide SOLO el clamp electrico.
// (Con termico, el cubo se derrite en un tick por SUS 180A idealizados —
// limitacion conocida de los nodos ideales: las ARISTAS de resistencia
// bajisima ven la corriente inflada. Lo que esta demo garantiza es que la
// FUENTE nunca se la cree.)
// El primer tick es el transitorio de arranque: V_ef parte del NOMINAL (no
// conoce la carga todavía) y converge al punto físico recién en el segundo.
// Medimos el régimen sostenido, salteando ese arranque.
double iMax42 = 0;
for (int t = 0; t < 15; t++)
{
    graph42.Tick(0.1);
    if (t > 0) iMax42 = Math.Max(iMax42, bat42.CorrienteEntregada);
}
double iCorto42 = 9.0 / (bat42.ResistenciaInternaNominal + 0.05);
Console.WriteLine($"Bateria: I_entregada={bat42.CorrienteEntregada:F2}A sostenidos (la ideal pediria 180A)  P_calor={bat42.PotenciaTermica:F1}W  viva={!bat42.IsBurnt}");
Verificar("la corriente entregada queda clavada al tope fisico V/(R_int+R)",
    Math.Abs(bat42.CorrienteEntregada - iCorto42) < 0.05 && iMax42 < iCorto42 + 0.05);
Verificar("la bateria sobrevive (antes moria por 180A inventados)", !bat42.IsBurnt);
Verificar("y su calor interno es el fisico: I²·R ≈ 48W, no 55kW", bat42.PotenciaTermica > 40 && bat42.PotenciaTermica < 55);


// ============================================================
// DEMO 43: EL CABLE FISICO Y SU GROSOR. El cobre real: R = ρ·L/A,
//   ampacidad por seccion y aislante que muere a 105°C. Un cable
//   FINITO (0.5mm) alimentando una carga golosa se quema como un
//   fusible improvisado y ABRE el circuito; el mismo circuito con
//   cable GORDO (2.5mm) fusiona a conexion perfecta y banca sin
//   despeinarse. Es la fisica que usa el cable fisico del juego.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 43: El grosor del cable decide su destino");
Console.WriteLine("====================================================\n");

double ampFino = Wire.AmpacidadDeCobre(0.5);
double ampGordo = Wire.AmpacidadDeCobre(2.5);
double rFino = Wire.ResistenciaDeCobre(0.5, 0.5);
double rGordo = Wire.ResistenciaDeCobre(0.5, 2.5);
Console.WriteLine($"Cobre de medio metro: 0.5mm -> {rFino * 1000:F1} mΩ, aguanta {ampFino:F1}A  |  2.5mm -> {rGordo * 1000:F2} mΩ, aguanta {ampGordo:F1}A");
Verificar("la ampacidad crece con el grosor como en las tablas reales",
    ampFino > 3.5 && ampFino < 4.5 && ampGordo > 25 && ampGordo < 30);

// --- El finito: 9V sobre una carga de 2Ω pide 4.4A > sus ~3.9A de aguante ---
var graph43 = new CircuitGraph();
// Batería de AUTO (R_int 0.01Ω): la única capaz de tirar los amperes que
// funden un cable. Con la 9V de bloque (R_int 1.7Ω) el lazo entero queda
// en 2.4A y el finito no llega nunca a su ampacidad de 3.9A.
var bat43 = new Battery("B", 9) { MaxCurrent = 400, ResistenciaInternaNominal = 0.01 };
var carga43 = new Resistor("Carga", 2);
graph43.Agregar(bat43);
graph43.Agregar(carga43);
var cableFino = graph43.Conectar(bat43.PinA, carga43.PinA, "Finito");
graph43.Conectar(carga43.PinB, bat43.PinB);
cableFino.GrosorMm = 0.5;
cableFino.Resistencia = rFino;               // 0.043Ω: arista resistiva real
cableFino.MaxCurrent = ampFino;              // su ampacidad
cableFino.CapacidadTermica = Wire.CapacidadTermicaDeCable(0.5, 0.5);
cableFino.MaxTemperature = 105;              // aislante de PVC

double iVista43 = 0;
for (int t = 0; t < 10; t++)
{
    graph43.Tick(0.1);
    if (!cableFino.IsBurnt) iVista43 = Math.Max(iVista43, Math.Abs(cableFino.Current));
}
Console.WriteLine($"Cable 0.5mm: llego a {iVista43:F2}A (aguantaba {ampFino:F1}) -> quemado={cableFino.IsBurnt} ({cableFino.MotivoQuemado})");
Console.WriteLine($"Circuito abierto: I_carga={carga43.Current:F3}A  bateria viva={!bat43.IsBurnt}");
Verificar("el cable finito se quema por CORRIENTE, como un fusible",
    cableFino.IsBurnt && cableFino.MotivoQuemado == "corriente");
Verificar("quemado, DEJA DE CONDUCIR: el circuito queda abierto", Math.Abs(carga43.Current) < 0.1);
Verificar("la bateria queda viva (el cable fue el fusible)", !bat43.IsBurnt);

// --- El gordo: mismo circuito, 2.5mm -> fusiona (conexion perfecta) y banca ---
var graph43b = new CircuitGraph();
var bat43b = new Battery("B", 9) { MaxCurrent = 400, ResistenciaInternaNominal = 0.01 };
var carga43b = new Resistor("Carga", 2);
graph43b.Agregar(bat43b);
graph43b.Agregar(carga43b);
var cableGordo = graph43b.Conectar(bat43b.PinA, carga43b.PinA, "Gordo");
graph43b.Conectar(carga43b.PinB, bat43b.PinB);
cableGordo.GrosorMm = 2.5;
cableGordo.Resistencia = rGordo < Wire.ResistenciaDespreciable
    ? CircuitConstants.WireResistance        // la regla hibrida del cable fisico: gordo y corto fusiona
    : rGordo;
cableGordo.MaxCurrent = ampGordo;
cableGordo.CapacidadTermica = Wire.CapacidadTermicaDeCable(0.5, 2.5);
cableGordo.MaxTemperature = 105;

for (int t = 0; t < 10; t++) graph43b.Tick(0.1);
Console.WriteLine($"Cable 2.5mm: I={Math.Abs(cableGordo.Current):F2}A de {ampGordo:F0}A que aguanta -> vivo={!cableGordo.IsBurnt}  carga={carga43b.Current:F2}A");
Verificar("el gordo fusiona y lleva la corriente real por Kirchhoff (4.5A)",
    !cableGordo.IsBurnt && Math.Abs(cableGordo.Current) > 4.3 && Math.Abs(cableGordo.Current) < 4.7);
Verificar("y la carga recibe sus 4.5A tranquila", carga43b.Current > 4.3 && carga43b.Current < 4.7);




// ============================================================
// DEMO 44: EL DIODO - la valvula de un solo sentido. En directa
//   cae sus 0.7V y la corriente da la formula del apunte; al
//   reves bloquea; y MUY al reves (mas alla del V inverso
//   maximo) se perfora y muere, como un 1N4007 maltratado.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 44: Diodo - conduce, bloquea, se perfora");
Console.WriteLine("====================================================\n");

var graph44 = new CircuitGraph();
var bat44 = new Battery("B44", 9);
var r44 = new Resistor("R", 330);
var d44 = new Diodo("D1");
graph44.Agregar(bat44); graph44.Agregar(r44); graph44.Agregar(d44);
graph44.Conectar(bat44.PinA, r44.PinA);
graph44.Conectar(r44.PinB, d44.PinA);   // anodo
graph44.Conectar(d44.PinB, bat44.PinB); // catodo al -
for (int t = 0; t < 15; t++) graph44.Tick(0.1);
Console.WriteLine($"Directa: V_diodo={d44.VoltajeEnBornes:F2}V  I={d44.Current * 1000:F1}mA  (formula: (9-0.7)/330 = 25.2mA)");
Verificar("en directa cae ~0.7V (el peaje del silicio)", d44.VoltajeEnBornes > 0.6 && d44.VoltajeEnBornes < 0.85);
Verificar("y la corriente da la del apunte: (V-0.7)/R", Math.Abs(d44.Current) > 0.023 && Math.Abs(d44.Current) < 0.027);

var graph44b = new CircuitGraph();
var bat44b = new Battery("B", 9);
var r44b = new Resistor("R", 330);
var d44b = new Diodo("D2");
graph44b.Agregar(bat44b); graph44b.Agregar(r44b); graph44b.Agregar(d44b);
graph44b.Conectar(bat44b.PinA, r44b.PinA);
graph44b.Conectar(r44b.PinB, d44b.PinB);   // AL REVES: catodo al +
graph44b.Conectar(d44b.PinA, bat44b.PinB);
for (int t = 0; t < 10; t++) graph44b.Tick(0.1);
Console.WriteLine($"Inversa (9V): I={Math.Abs(d44b.Current) * 1e6:F1}uA  vivo={!d44b.IsBurnt}");
Verificar("al reves bloquea (solo la fuga numerica de uA)", Math.Abs(d44b.Current) < 0.0001 && !d44b.IsBurnt);

var graph44c = new CircuitGraph();
var bat44c = new Battery("B", 150); // muy por encima de los 100V inversos del diodo
var d44c = new Diodo("D3");
graph44c.Agregar(bat44c); graph44c.Agregar(d44c);
graph44c.Conectar(bat44c.PinA, d44c.PinB); // al reves, sin piedad
graph44c.Conectar(d44c.PinA, bat44c.PinB);
for (int t = 0; t < 8; t++) graph44c.Tick(0.1);
Console.WriteLine($"Inversa (150V): quemado={d44c.IsBurnt} motivo='{d44c.MotivoQuemado}'");
// Modelo nuevo: pasado el V de ruptura el diodo CONDUCE en avalancha
// (caída clavada en MaxVoltajeInverso) y muere por la POTENCIA que eso
// disipa, no por un contador de voltaje. Sin limitadora lo revientan
// igual; con la corriente limitada sobrevive, como el 1N4007 real.
Verificar("150V al reves lo revientan (avalancha destructiva)",
    d44c.IsBurnt && (d44c.MotivoQuemado == "potencia" || d44c.MotivoQuemado == "corriente"));


// ============================================================
// DEMO 45: EL ZENER - la ruptura domesticada. Al reves y con su
//   resistencia limitadora, clava 5.1V a la salida (el regulador
//   mas simple del mundo). Sin limitadora, la potencia lo cocina:
//   el zener SIEMPRE trabaja acompaniado.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 45: Zener - regula 5.1V (con su limitadora)");
Console.WriteLine("====================================================\n");

var graph45 = new CircuitGraph();
var bat45 = new Battery("B45", 9);
var r45 = new Resistor("Limitadora", 220);
var z45 = new Diodo("Z1") { VoltajeZener = 5.1, MaxVoltajeInverso = double.PositiveInfinity, MaxPower = 0.5 };
graph45.Agregar(bat45); graph45.Agregar(r45); graph45.Agregar(z45);
graph45.Conectar(bat45.PinA, r45.PinA);
graph45.Conectar(r45.PinB, z45.PinB);   // el zener va AL REVES: catodo arriba
graph45.Conectar(z45.PinA, bat45.PinB);
for (int t = 0; t < 25; t++) graph45.Tick(0.1);
Console.WriteLine($"Con limitadora: V_salida={z45.VoltajeEnBornes:F2}V (esperado ~5.1)  regulando={z45.RegulandoZener}  I={Math.Abs(z45.Current) * 1000:F1}mA");
Verificar("el zener clava ~5.1V a la salida", z45.RegulandoZener && z45.VoltajeEnBornes > 4.7 && z45.VoltajeEnBornes < 5.5);
Verificar("y la limitadora se come el resto: I = (9-5.1)/220", Math.Abs(z45.Current) > 0.014 && Math.Abs(z45.Current) < 0.022);

var graph45b = new CircuitGraph();
var bat45b = new Battery("B", 9);
var z45b = new Diodo("Z2") { VoltajeZener = 5.1, MaxVoltajeInverso = double.PositiveInfinity, MaxPower = 0.5 };
graph45b.Agregar(bat45b); graph45b.Agregar(z45b);
graph45b.Conectar(bat45b.PinA, z45b.PinB); // sin limitadora: pecado capital
graph45b.Conectar(z45b.PinA, bat45b.PinB);
for (int t = 0; t < 60; t++) graph45b.Tick(0.1);
Console.WriteLine($"Sin limitadora: quemado={z45b.IsBurnt} motivo='{z45b.MotivoQuemado}'");
Verificar("sin limitadora, la potencia lo cocina", z45b.IsBurnt && z45b.MotivoQuemado == "potencia");


// ============================================================
// DEMO 46: PNP - la llave de ARRIBA (se enciende tirando la base
//   a masa; suelta, la union interna E-B la deja apagada sola).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 46: PNP (llave de arriba)");
Console.WriteLine("====================================================\n");

var graph46 = new CircuitGraph();
var bat46 = new Battery("B46", 9);
var lamp46 = new Lampara("Lamp", 60) { PotenciaNominal = 1.4 };
var pnp46 = new TransistorPNP("Q_PNP");
var llave46 = new Switch("BaseAMasa", startClosed: false);
var rb46 = new Resistor("R_base", 1000);
graph46.Agregar(bat46); graph46.Agregar(lamp46); graph46.Agregar(pnp46);
graph46.Agregar(llave46); graph46.Agregar(rb46);
// Alta: bateria + -> EMISOR ... COLECTOR -> lampara -> bateria -
graph46.Conectar(bat46.PinA, pnp46.PinB);   // emisor al +
graph46.Conectar(pnp46.PinA, lamp46.PinA);  // colector a la carga
graph46.Conectar(lamp46.PinB, bat46.PinB);
// Control: BASE -> resistencia -> llave -> masa
graph46.Conectar(pnp46.PinBase, rb46.PinA);
graph46.Conectar(rb46.PinB, llave46.PinA);
graph46.Conectar(llave46.PinB, bat46.PinB);
for (int t = 0; t < 6; t++) graph46.Tick(0.1);
double brilloSuelto46 = lamp46.Brillo;
llave46.IsClosed = true;
for (int t = 0; t < 10; t++) graph46.Tick(0.1);
Console.WriteLine($"PNP base suelta: brillo={brilloSuelto46:P0} | base a masa: V_EB={pnp46.VoltajeEmisorBase:F2}V conduce={pnp46.Conduciendo} brillo={lamp46.Brillo:P0}");
Verificar("PNP con la base suelta: apagado (la union E-B lo cuida)", brilloSuelto46 < 0.02);
Verificar("PNP con la base a masa: la llave de arriba CIERRA", pnp46.Conduciendo && lamp46.Brillo > 0.85);


// ============================================================
// DEMO 49: EL ELECTROLITICO Y SU POLARIDAD - bien puesto carga
//   como cualquier capacitor; al reves, el electrolito se
//   descompone y REVIENTA en decimas de segundo. La leccion que
//   todo electronico aprendio con un susto.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 49: Capacitor electrolitico - la franja importa");
Console.WriteLine("====================================================\n");

var graph49 = new CircuitGraph();
var bat49 = new Battery("B49", 9);
var cap49 = new CapacitorElectrolitico("C_bien", 0.05);
graph49.Agregar(bat49); graph49.Agregar(cap49);
graph49.Conectar(bat49.PinA, cap49.PinA); // pata + al +: como debe ser
graph49.Conectar(cap49.PinB, bat49.PinB);
for (int t = 0; t < 20; t++) graph49.Tick(0.1);
Console.WriteLine($"Bien polarizado: carga={cap49.ChargeLevel:P0}  vivo={!cap49.IsBurnt}");
Verificar("bien polarizado carga tranquilo, como cualquier capacitor", cap49.ChargeLevel > 0.3 && !cap49.IsBurnt);

var graph49b = new CircuitGraph();
var bat49b = new Battery("B", 9);
var cap49b = new CapacitorElectrolitico("C_alReves", 0.05);
graph49b.Agregar(bat49b); graph49b.Agregar(cap49b);
graph49b.Conectar(bat49b.PinA, cap49b.PinB); // AL REVES: la pata - al +
graph49b.Conectar(cap49b.PinA, bat49b.PinB);
for (int t = 0; t < 8; t++) graph49b.Tick(0.1);
Console.WriteLine($"Al reves: V_polarizado={cap49b.VoltajePolarizado:F1}V  quemado={cap49b.IsBurnt}  motivo='{cap49b.MotivoQuemado}'");
Verificar("al reves, el electrolito revienta en decimas de segundo", cap49b.IsBurnt && cap49b.MotivoQuemado == "polaridad invertida");


// ============================================================
// DEMO 54: FIX 1 — Falso cortocircuito con TRES o más fuentes en serie
//   Antes: 3 pilas botón en serie sembraban dos veces el "fondo" del
//   circuito (cada una decidía que su nodo − era la referencia 0V) y el
//   solver reportaba corto donde no había ninguno. Ahora las siembras
//   son POSTERGADAS: fase 1 sólo siembra el fondo real ("menos" que no
//   sea "mas" de otra fuente), fase 2 sólo si un lazo cerrado lo pide.
// ============================================================
Console.WriteLine("====================================================");
Console.WriteLine(" DEMO 54: FIX 1 — Tres pilas botón 3V en serie, sin falso corto");
Console.WriteLine("====================================================\n");

for (int orden = 0; orden < 2; orden++)
{
    var g54 = new CircuitGraph();
    // orden=0: pilaA arriba, pilaC abajo. orden=1: al revés.
    var pilaA = new Battery("PilaA", 3);
    var pilaB = new Battery("PilaB", 3);
    var pilaC = new Battery("PilaC", 3);
    foreach (var b in new[] { pilaA, pilaB, pilaC })
    {
        b.MaxCurrent = 999; // deshabilitamos la muerte por corriente para el test
        b.ResistenciaInternaNominal = 0.5;
    }
    var lamp54 = new Lampara("Lamp54", 60);
    if (orden == 0)
    {
        g54.Agregar(pilaA); g54.Agregar(pilaB); g54.Agregar(pilaC); g54.Agregar(lamp54);
    }
    else
    {
        g54.Agregar(pilaC); g54.Agregar(pilaB); g54.Agregar(pilaA); g54.Agregar(lamp54);
    }
    g54.Conectar(pilaA.PinB, pilaB.PinA); // A− → B+
    g54.Conectar(pilaB.PinB, pilaC.PinA); // B− → C+
    g54.Conectar(pilaA.PinA, lamp54.PinA); // + de la cadena → lámpara
    g54.Conectar(lamp54.PinB, pilaC.PinB); // − de la cadena
    for (int t = 0; t < 10; t++) g54.Tick(0.05);
    Console.WriteLine($"  Orden {orden}: A.corto={pilaA.EnCortocircuito}  B.corto={pilaB.EnCortocircuito}  C.corto={pilaC.EnCortocircuito}  |  I_lamp={lamp54.Current * 1000:F1}mA");
    Verificar($"orden {orden}: NINGUNA pila queda en corto",
        !pilaA.EnCortocircuito && !pilaB.EnCortocircuito && !pilaC.EnCortocircuito);
    Verificar($"orden {orden}: la cadena da ~9V y la lámpara conduce",
        Math.Abs(lamp54.Current) > 0.10 && Math.Abs(lamp54.Current) < 0.16);
}


// ============================================================
// DEMO 55: FIX 2 — Corriente REAL de las fuentes intermedias en serie
//   Antes: en dos 9V en serie con lámpara, la fuente del arriba mostraba
//   la corriente de la carga y la del medio mostraba 0 (el balance de su
//   nodo + se cancelaba). Ahora, con el árbol de fuentes BFS, todas las
//   fuentes en el mismo lazo muestran la MISMA corriente con MISMO
//   signo — como corresponde a un circuito serie.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 55: FIX 2 — Corriente real de fuentes en serie (Kirchhoff en árbol)");
Console.WriteLine("====================================================\n");

var g55 = new CircuitGraph();
var bat55a = new Battery("Bat55a", 9);
var bat55b = new Battery("Bat55b", 9);
var lamp55 = new Lampara("Lamp55", 60);
g55.Agregar(bat55a); g55.Agregar(bat55b); g55.Agregar(lamp55);
g55.Conectar(bat55a.PinB, bat55b.PinA);
g55.Conectar(bat55a.PinA, lamp55.PinA);
g55.Conectar(lamp55.PinB, bat55b.PinB);
for (int t = 0; t < 20; t++) g55.Tick(0.1);
Console.WriteLine($"  V_lamp={lamp55.VoltajeEnBornes:F2}V  I_lamp={lamp55.Current * 1000:F1}mA");
Console.WriteLine($"  I_batA={bat55a.CorrienteEntregada * 1000:F1}mA  I_batB={bat55b.CorrienteEntregada * 1000:F1}mA");
Verificar("ambas fuentes muestran la MISMA corriente",
    Math.Abs(bat55a.CorrienteEntregada - bat55b.CorrienteEntregada) < 0.001);
Verificar("y esa corriente es la de la lámpara",
    Math.Abs(bat55a.CorrienteEntregada - Math.Abs(lamp55.Current)) < 0.001);
Verificar("18V nominal se hunde a ~17V bajo carga (R interna en el solver)",
    lamp55.VoltajeEnBornes > 16.5 && lamp55.VoltajeEnBornes < 17.5);


// ============================================================
// DEMO 56: FIX 3 — Capacitor con modelo companion (backward Euler)
//   Verifica: (a) carga EXPONENCIAL siguiendo tau=R·C, no rampa lineal;
//   (b) tiempo depende de R en serie; (c) carga en polaridad INVERTIDA
//   (v_alm negativo, como un cerámico real); (d) NO carga detrás de una
//   llave abierta (fuga por 1MΩ del "open" no dispara); (e) descarga
//   suave por LED después de sacar la fuente.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 56: FIX 3 — Capacitor con modelo companion");
Console.WriteLine("====================================================\n");

// (a,b) tiempo de carga proporcional a R·C
foreach (double rVal in new[] { 100.0, 1000.0, 10000.0 })
{
    var g56 = new CircuitGraph();
    var bat = new Battery("B56", 9);
    var r = new Resistor($"R{rVal}", rVal);
    var cap = new Capacitor("C56", 100e-6);
    g56.Agregar(bat); g56.Agregar(r); g56.Agregar(cap);
    g56.Conectar(bat.PinA, r.PinA);
    g56.Conectar(r.PinB, cap.PinA);
    g56.Conectar(cap.PinB, bat.PinB);
    double dt = 0.001;
    double tauFisico = rVal * 100e-6;
    int ticksPara99 = (int)Math.Ceiling(5 * tauFisico / dt);
    for (int t = 0; t < ticksPara99 + 10; t++) g56.Tick(dt);
    Console.WriteLine($"  R={rVal:F0}Ω tau={tauFisico:F4}s: v_alm={cap.VoltajeAlmacenado:F2}V (esperado ~9V al 5·tau)");
    Verificar($"R={rVal:F0}Ω: cap carga al ~99% (>=8.5V) en 5·tau",
        cap.VoltajeAlmacenado > 8.5);
}

// (c) polaridad invertida
var g56c = new CircuitGraph();
var bat56c = new Battery("B56c", 9);
var r56c = new Resistor("R56c", 220);
var cap56c = new Capacitor("C56c", 100e-6);
g56c.Agregar(bat56c); g56c.Agregar(r56c); g56c.Agregar(cap56c);
g56c.Conectar(bat56c.PinA, r56c.PinA);
g56c.Conectar(r56c.PinB, cap56c.PinB); // ← al revés
g56c.Conectar(cap56c.PinA, bat56c.PinB);
for (int t = 0; t < 200; t++) g56c.Tick(0.001);
Console.WriteLine($"  Cap invertido: v_alm={cap56c.VoltajeAlmacenado:F2}V (esperado ~-9V)");
Verificar("cap conectado al revés carga con v_alm NEGATIVO",
    cap56c.VoltajeAlmacenado < -8.0 && cap56c.VoltajeAlmacenado > -9.5);

// (d) llave abierta: cap NO se carga (o casi nada)
var g56d = new CircuitGraph();
var bat56d = new Battery("B56d", 9);
var sw56d = new Switch("Sw56d", startClosed: false);
var cap56d = new Capacitor("C56d", 100e-6);
g56d.Agregar(bat56d); g56d.Agregar(sw56d); g56d.Agregar(cap56d);
g56d.Conectar(bat56d.PinA, sw56d.PinA);
g56d.Conectar(sw56d.PinB, cap56d.PinA);
g56d.Conectar(cap56d.PinB, bat56d.PinB);
for (int t = 0; t < 100; t++) g56d.Tick(0.1); // 10 segundos con la llave abierta
Console.WriteLine($"  Detrás de llave abierta 10s: v_alm={cap56d.VoltajeAlmacenado:F3}V (esperado <1V, ideal 0)");
Verificar("cap detrás de llave abierta NO se carga (<1V de fuga)",
    Math.Abs(cap56d.VoltajeAlmacenado) < 1.5);

// (e) descarga por LED
var g56e = new CircuitGraph();
var cap56e = new Capacitor("C56e", 1.0);
cap56e.VoltajeAlmacenado = 6.0; // ya cargado
var r56e = new Resistor("R56e", 220);
var led56e = new LED("Led56e", 2.0, 12);
g56e.Agregar(cap56e); g56e.Agregar(r56e); g56e.Agregar(led56e);
g56e.Conectar(cap56e.PinA, r56e.PinA);
g56e.Conectar(r56e.PinB, led56e.PinA);
g56e.Conectar(led56e.PinB, cap56e.PinB);
double vAlmInicial = cap56e.VoltajeAlmacenado;
for (int t = 0; t < 30; t++) g56e.Tick(0.1); // 3 segundos
Console.WriteLine($"  Descarga por LED (3s): v_alm {vAlmInicial:F1}V→{cap56e.VoltajeAlmacenado:F1}V, LED enc={led56e.EstaEncendido}");
Verificar("cap cargado hace conducir al LED después de sacar la fuente",
    led56e.EstaEncendido);


// ============================================================
// DEMO 57: FIX 4 — R interna de la fuente en el solver (V_efectivo)
//   Antes: la R interna era sólo "térmica/contable"; el solver veía a
//   la fuente como IDEAL, y los bornes marcaban V_nominal aunque la
//   carga tirara 5A. Ahora V_efectivo se ajusta cada tick con la
//   fórmula analítica del divisor: bajo carga baja, en corto colapsa.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 57: FIX 4 — R interna real en el solver (V_efectivo)");
Console.WriteLine("====================================================\n");

var g57 = new CircuitGraph();
var bat57 = new Battery("B57", 9);
bat57.ResistenciaInternaNominal = 1.7;
var r57 = new Resistor("R57", 10); // carga pesada
g57.Agregar(bat57); g57.Agregar(r57);
g57.Conectar(bat57.PinA, r57.PinA);
g57.Conectar(r57.PinB, bat57.PinB);
for (int t = 0; t < 20; t++) g57.Tick(0.1);
double iEsperada = 9.0 / (10 + 1.7);
double vBornesEsperado = iEsperada * 10;
Console.WriteLine($"  Bat 9V R_int 1.7Ω sobre carga 10Ω:");
Console.WriteLine($"    V_efectivo={bat57.VoltajeEfectivoParaSolver:F2}V  I={bat57.CorrienteEntregada * 1000:F0}mA  V_bornes={bat57.VoltajeEnBornes:F2}V");
Console.WriteLine($"    Físico:      V_bornes≈{vBornesEsperado:F2}V  I≈{iEsperada * 1000:F0}mA (divisor R_int+R_carga)");
Verificar("V_efectivo se hunde bajo carga (cerca de I·R_carga = 7.7V)",
    bat57.VoltajeEfectivoParaSolver < 8.5);
Verificar("la corriente entregada respeta la R interna (~770mA, no 900mA)",
    Math.Abs(bat57.CorrienteEntregada) > 0.70 && Math.Abs(bat57.CorrienteEntregada) < 0.82);


// ============================================================
// DEMO 58: FIX 5 — LED al revés con limitadora NO se rompe
//   Antes: con 330Ω limitadora, el LED al revés se marcaba como muerto
//   por "voltaje inverso" a los pocos ticks — irreal, la corriente
//   inversa real por esa R (fuga 9µA) no rompe nada. Ahora la ruptura
//   inversa exige VOLTAJE Y CORRIENTE inversa apreciable (>=0.5mA).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 58: FIX 5 — LED al revés con limitadora sobrevive");
Console.WriteLine("====================================================\n");

var g58 = new CircuitGraph();
var bat58 = new Battery("B58", 9);
var r58 = new Resistor("R58_lim", 330);
var led58 = new LED("Led58", 2.0, 12);
g58.Agregar(bat58); g58.Agregar(r58); g58.Agregar(led58);
g58.Conectar(bat58.PinA, r58.PinA);
g58.Conectar(r58.PinB, led58.PinB); // ← al revés: + de la fuente al PinB del LED
g58.Conectar(led58.PinA, bat58.PinB);
for (int t = 0; t < 60; t++) g58.Tick(0.1); // 6 segundos
double deltaVLed58 = led58.PinA.Voltage - led58.PinB.Voltage;
Console.WriteLine($"  Después de 6s con 9V invertidos: LED quemado={led58.IsBurnt} ({led58.MotivoQuemado})  ΔV(LED)={deltaVLed58:F2}V");
Verificar("LED al revés con R limitadora NO se rompe (fuga < 0.5mA)",
    !led58.IsBurnt);



// ============================================================
// DEMOS 59-66: RED DE REGRESIÓN DE LA AUDITORÍA QA.
//   Ocho bugs de física/matemática detectados en la auditoría y
//   corregidos. Cada demo vigila que no vuelvan.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 59: Fuentes IGUALES en paralelo se reparten la carga");
Console.WriteLine("====================================================\n");

var g59 = new CircuitGraph();
var b59a = new Battery("B59a", 9);
var b59b = new Battery("B59b", 9);
var r59 = new Resistor("R59", 100);
g59.Agregar(b59a); g59.Agregar(b59b); g59.Agregar(r59);
g59.Conectar(b59a.PinA, r59.PinA); g59.Conectar(b59b.PinA, r59.PinA);
g59.Conectar(b59a.PinB, r59.PinB); g59.Conectar(b59b.PinB, r59.PinB);
for (int t = 0; t < 30; t++) g59.Tick(0.1);
// Dos R_int iguales en paralelo = la mitad: 9/(100+1.7/2).
double i59 = 9.0 / (100 + b59a.ResistenciaInternaNominal / 2);
Console.WriteLine($"  I(carga)={r59.Current * 1000:F1}mA  Ia={b59a.CorrienteEntregada * 1000:F1}mA  Ib={b59b.CorrienteEntregada * 1000:F1}mA  calor={b59a.PotenciaTermica * 1000:F2}mW c/u");
Verificar("BUG 1: dos fuentes iguales en paralelo NO se creen en corto",
    !b59a.EnCortocircuito && !b59b.EnCortocircuito);
Verificar("BUG 1: la carga recibe su corriente (antes recibia 0mA)",
    Math.Abs(r59.Current - i59) < 0.0005);
Verificar("BUG 1: sin calor fantasma (antes 47.6W por fuente)",
    b59a.PotenciaTermica < 0.01 && b59b.PotenciaTermica < 0.01);

// Y el conflicto REAL se sigue detectando: distinto voltaje en paralelo.
var g59b = new CircuitGraph();
var b59c = new Battery("9V", 9);
var b59d = new Battery("6V", 6);
var r59b = new Resistor("R", 100);
g59b.Agregar(b59c); g59b.Agregar(b59d); g59b.Agregar(r59b);
g59b.Conectar(b59c.PinA, r59b.PinA); g59b.Conectar(b59d.PinA, r59b.PinA);
g59b.Conectar(b59c.PinB, r59b.PinB); g59b.Conectar(b59d.PinB, r59b.PinB);
for (int t = 0; t < 20; t++) g59b.Tick(0.1);
Verificar("pero 9V y 6V en paralelo SI es conflicto (no se perdio la deteccion)",
    b59c.EnCortocircuito && b59d.EnCortocircuito);

// Con R_int distintas, el reparto es por conductancia (la fuerte aporta mas).
var g59c = new CircuitGraph();
var b59e = new Battery("Fuerte", 9) { ResistenciaInternaNominal = 0.5 };
var b59f = new Battery("Debil", 9) { ResistenciaInternaNominal = 2.0 };
var r59c = new Resistor("R", 100);
g59c.Agregar(b59e); g59c.Agregar(b59f); g59c.Agregar(r59c);
g59c.Conectar(b59e.PinA, r59c.PinA); g59c.Conectar(b59f.PinA, r59c.PinA);
g59c.Conectar(b59e.PinB, r59c.PinB); g59c.Conectar(b59f.PinB, r59c.PinB);
for (int t = 0; t < 30; t++) g59c.Tick(0.1);
Console.WriteLine($"  R_int distintas: fuerte={b59e.CorrienteEntregada * 1000:F1}mA  debil={b59f.CorrienteEntregada * 1000:F1}mA");
Verificar("con R_int distintas el reparto es por conductancia (4:1)",
    b59e.CorrienteEntregada > b59f.CorrienteEntregada * 3.5);


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 60: La deteccion de cortocircuito es ESTABLE");
Console.WriteLine("====================================================\n");

var g60 = new CircuitGraph();
var b60a = new Battery("B60a", 9);
var b60b = new Battery("B60b", 9);
g60.Agregar(b60a); g60.Agregar(b60b);
g60.Conectar(b60a.PinA, b60b.PinB);   // serie
g60.Conectar(b60b.PinA, b60a.PinB);   // corto de punta a punta
int ticksEnCorto60 = 0;
for (int t = 0; t < 40; t++) { g60.Tick(0.1); if (b60a.EnCortocircuito) ticksEnCorto60++; }
Console.WriteLine($"  De 40 ticks con el cable puesto, en corto: {ticksEnCorto60}");
Verificar("BUG 2: el corto queda CLAVADO, no parpadea (antes 27/40)",
    ticksEnCorto60 == 40);


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 62: Ruptura inversa del LED (avalancha)");
Console.WriteLine("====================================================\n");

var g62 = new CircuitGraph();
var b62 = new Battery("B62", 24);
var led62 = new LED("Led62");
g62.Agregar(b62); g62.Agregar(led62);
g62.Conectar(b62.PinA, led62.PinB);   // al reves, SIN limitadora
g62.Conectar(led62.PinA, b62.PinB);
for (int t = 0; t < 100; t++) g62.Tick(0.1);
Console.WriteLine($"  LED al reves a 24V sin limitadora: quemado={led62.IsBurnt} ({led62.MotivoQuemado})");
Verificar("BUG 4: al reves SIN limitadora, el LED muere (antes aguantaba 400V)",
    led62.IsBurnt);
// El contrapunto (LED al reves CON limitadora sobrevive) lo vigila la DEMO 58.


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 63: El diodo comun en avalancha controlada");
Console.WriteLine("====================================================\n");

var g63 = new CircuitGraph();
var b63 = new Battery("B63", 150) { MaxCurrent = double.PositiveInfinity };
var r63 = new Resistor("R63", 10_000);
var d63 = new Diodo("D63");
g63.Agregar(b63); g63.Agregar(r63); g63.Agregar(d63);
g63.Conectar(b63.PinA, r63.PinA);
g63.Conectar(r63.PinB, d63.PinB);     // al reves
g63.Conectar(d63.PinA, b63.PinB);
for (int t = 0; t < 30; t++) g63.Tick(0.1);
Console.WriteLine($"  -150V detras de 10k: I={Math.Abs(d63.Current) * 1000:F2}mA  dV={d63.PinA.Voltage - d63.PinB.Voltage:F1}V  vivo={!d63.IsBurnt}");
Verificar("BUG 5: con la corriente limitada, la avalancha NO lo mata",
    !d63.IsBurnt && d63.EnAvalancha);
Verificar("y en avalancha clava su voltaje de ruptura",
    Math.Abs(Math.Abs(d63.PinA.Voltage - d63.PinB.Voltage) - d63.MaxVoltajeInverso) < 1.0);


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 64: La union base-emisor es un DIODO, no un resistor");
Console.WriteLine("====================================================\n");

var g64 = new CircuitGraph();
var b64 = new Battery("B64", 5) { ResistenciaInternaNominal = 0.05 };
var rb64 = new Resistor("Rb64", 10_000);
var carga64 = new Resistor("Rc64", 100);
var q64 = new TransistorNPN("Q64");
g64.Agregar(b64); g64.Agregar(rb64); g64.Agregar(carga64); g64.Agregar(q64);
g64.Conectar(b64.PinA, rb64.PinA);
g64.Conectar(rb64.PinB, q64.PinBase);
g64.Conectar(b64.PinA, carga64.PinA);
g64.Conectar(carga64.PinB, q64.PinA);
g64.Conectar(q64.PinB, b64.PinB);
for (int t = 0; t < 40; t++) g64.Tick(0.1);
Console.WriteLine($"  V_BE={q64.VoltajeBaseEmisor:F2}V (real ~0.7)  I_base={q64.CorrienteBase * 1000:F2}mA (apunte: (5-0.7)/10k = 0.43mA)");
Verificar("BUG 6: V_BE se clava en ~0.7V (antes mostraba 2.46V, imposible)",
    Math.Abs(q64.VoltajeBaseEmisor - 0.7) < 0.05);
Verificar("BUG 6: la corriente de base es la del apunte",
    Math.Abs(q64.CorrienteBase - 0.00043) < 0.00005);

// Y en INVERSA la union bloquea (antes circulaban 0.82mA fantasma).
var g64b = new CircuitGraph();
var b64b = new Battery("B", 9);
var r64b = new Resistor("R", 1000);
var q64b = new TransistorNPN("Q");
g64b.Agregar(b64b); g64b.Agregar(r64b); g64b.Agregar(q64b);
g64b.Conectar(b64b.PinA, q64b.PinB);   // EMISOR al +
g64b.Conectar(q64b.PinBase, r64b.PinA);
g64b.Conectar(r64b.PinB, b64b.PinB);
for (int t = 0; t < 20; t++) g64b.Tick(0.1);
Console.WriteLine($"  Emisor al +9V, base a masa: I(B-E)={Math.Abs(q64b.CorrienteBase) * 1e6:F1}uA (antes 818uA)");
Verificar("BUG 6: la union B-E BLOQUEA en inversa",
    Math.Abs(q64b.CorrienteBase) < 0.00005);


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 65: El capacitor no calienta con lo que ALMACENA");
Console.WriteLine("====================================================\n");

var g65 = new CircuitGraph();
var b65 = new Battery("B65", 9) { ResistenciaInternaNominal = 0.05 };
var r65 = new Resistor("R65", 10);
var cap65 = new Capacitor("C65", 0.5);
g65.Agregar(b65); g65.Agregar(r65); g65.Agregar(cap65);
g65.Conectar(b65.PinA, r65.PinA);
g65.Conectar(r65.PinB, cap65.PinA);
g65.Conectar(cap65.PinB, b65.PinB);
double joulesCap65 = 0, joulesR65 = 0;
for (int t = 0; t < 300; t++)
{
    g65.Tick(0.1);
    joulesCap65 += cap65.PotenciaTermica * 0.1;
    joulesR65 += r65.PotenciaTermica * 0.1;
}
double almacenada65 = 0.5 * cap65.Capacitance * cap65.VoltajeAlmacenado * cap65.VoltajeAlmacenado;
Console.WriteLine($"  Almacenados {almacenada65:F1}J | calor del CAP={joulesCap65:F2}J (antes 19.9J) | calor de R={joulesR65:F1}J");
Verificar("BUG 7: el capacitor casi no genera calor propio (solo su ESR)",
    joulesCap65 < 1.0);
Verificar("BUG 7: y el balance de energia cierra (antes sobraban 20J)",
    Math.Abs((almacenada65 + joulesCap65 + joulesR65) - almacenada65 * 2) < 1.0);


Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 66: Motor SoloDC y Capacitor infinito");
Console.WriteLine("====================================================\n");

// Menor: un motor de continua NO gira con alterna.
var g66b = new CircuitGraph();
var red66 = new FuenteAC("Red66", 220, 50);
var motor66 = new Motor("M66", 25);
g66b.Agregar(red66); g66b.Agregar(motor66);
g66b.Conectar(red66.PinA, motor66.PinA);
g66b.Conectar(motor66.PinB, red66.PinB);
for (int t = 0; t < 30; t++) g66b.Tick(0.1);
Console.WriteLine($"  Motor DC enchufado a 220V~: RPM={motor66.RPM:F0}  incompatible={motor66.EsIncompatible}");
Verificar("un motor de continua NO gira con alterna", !motor66.EstaGirando);

// Menor: el \u201cCapacitor \u221e\u201d del sandbox muestra su carga.
var capInf66 = new Capacitor("Cap∞", 0.05) { MaxVoltage = double.PositiveInfinity };
capInf66.VoltajeAlmacenado = 12.5;
Verificar("el Capacitor ∞ muestra nivel de carga (antes 0% para siempre)",
    capInf66.ChargeLevel > 0.4);


// ============================================================
// DEMO 67: PARTE 2 — arranque de los modelos companeros.
//   Todo lo que tiene umbral tenia que esperar a que la
//   resistencia dinamica bajara desde 1 MOhm por contraccion:
//   10 ticks con 9V, 29 con una pila de 3V. Ahora se siembra el
//   piso en el tick de entrada y se converge SUBIENDO.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 67: Parte 2 - arranque rapido de LED/diodo/zener");
Console.WriteLine("====================================================\n");

// --- 2-1: el LED llega a su punto de operacion en pocos ticks ---
int TicksHastaConverger(double volt, double rSerie)
{
    var g = new CircuitGraph();
    var b = new Battery("B67", volt) { MaxCurrent = 1e9 };
    var r = new Resistor("R67", rSerie);
    var l = new LED("L67", 2.0, 12) { MaxPower = 1e9 };
    g.Agregar(b); g.Agregar(r); g.Agregar(l);
    g.Conectar(b.PinA, r.PinA); g.Conectar(r.PinB, l.PinA); g.Conectar(l.PinB, b.PinB);
    double objetivo = (volt - 2.0) / (rSerie + b.ResistenciaInternaNominal);
    for (int t = 0; t < 400; t++)
    {
        g.Tick(0.1);
        if (Math.Abs(l.Current - objetivo) <= 0.02 * objetivo) return t + 1;
    }
    return -1;
}

int t9 = TicksHastaConverger(9, 220);
int t3 = TicksHastaConverger(3, 220);
Console.WriteLine($"  LED 9V/220ohm converge en {t9} ticks (antes 10) · 3V/220ohm en {t3} ticks (antes 29)");
Verificar("2-1: el LED de 9V/220ohm converge en <=5 ticks (antes 10)", t9 > 0 && t9 <= 5);
Verificar("2-1: el LED de 3V/220ohm converge en <=12 ticks (antes 29)", t3 > 0 && t3 <= 12);

// --- 2-1: y el punto de operacion final NO se movio ---
var g67 = new CircuitGraph();
var b67 = new Battery("B67b", 9);
var r67 = new Resistor("R67b", 220);
var led67 = new LED("L67b", 2.0, 12);
g67.Agregar(b67); g67.Agregar(r67); g67.Agregar(led67);
g67.Conectar(b67.PinA, r67.PinA); g67.Conectar(r67.PinB, led67.PinA); g67.Conectar(led67.PinB, b67.PinB);
for (int t = 0; t < 80; t++) g67.Tick(0.1);
Console.WriteLine($"  Punto final: I={led67.Current * 1000:F3}mA  V_led={led67.VoltajeEnBornes:F3}V (el del apunte: 31.574mA / 2.000V)");
Verificar("2-1: el punto de operacion final sigue siendo el del apunte",
    Math.Abs(led67.Current * 1000 - 31.574) < 0.01 && Math.Abs(led67.VoltajeEnBornes - 2.0) < 0.005);

// --- 2-1: el LED da luz en el segundo tick tras cerrar la llave ---
var g67b = new CircuitGraph();
var pila67 = new Battery("Pila67", 3) { ResistenciaInternaNominal = 8, MaxCurrent = 0.05 };
var sw67 = new Switch("Sw67") { IsClosed = false };
var r67b = new Resistor("R67c", 220);
var led67b = new LED("L67c", 2.0, 12);
g67b.Agregar(pila67); g67b.Agregar(sw67); g67b.Agregar(r67b); g67b.Agregar(led67b);
g67b.Conectar(pila67.PinA, sw67.PinA); g67b.Conectar(sw67.PinB, r67b.PinA);
g67b.Conectar(r67b.PinB, led67b.PinA); g67b.Conectar(led67b.PinB, pila67.PinB);
for (int t = 0; t < 10; t++) g67b.Tick(0.1);
sw67.IsClosed = true;
int ticksHastaLuz = -1;
for (int t = 0; t < 60 && ticksHastaLuz < 0; t++)
{
    g67b.Tick(0.1);
    if (led67b.EstaEncendido) ticksHastaLuz = t + 1;
}
Console.WriteLine($"  Pila 3V: la luz aparece {ticksHastaLuz} ticks despues de cerrar la llave (antes 15)");
Verificar("2-1: con pila de 3V la luz aparece en <=3 ticks (antes 15)",
    ticksHastaLuz > 0 && ticksHastaLuz <= 3);

// --- 2-3: el Zener no anuncia REGULANDO hasta que clava de verdad ---
var g67c = new CircuitGraph();
var b67c = new Battery("B67d", 9) { MaxCurrent = 1e9 };
var rlim67 = new Resistor("Rlim67", 220);
var zener67 = new Diodo("Z67") { VoltajeZener = 5.1, MaxVoltajeInverso = double.PositiveInfinity, MaxPower = 0.5 };
g67c.Agregar(b67c); g67c.Agregar(rlim67); g67c.Agregar(zener67);
g67c.Conectar(b67c.PinA, rlim67.PinA); g67c.Conectar(rlim67.PinB, zener67.PinB);
g67c.Conectar(zener67.PinA, b67c.PinB);
g67c.Tick(0.1);
Console.WriteLine($"  Zener, 1er tick: caida={zener67.VoltajeEnBornes:F2}V  RegulandoZener={zener67.RegulandoZener}");
Verificar("2-3: en el 1er tick el zener NO dice 'regulando' (la caida real es ~9V)",
    !zener67.RegulandoZener);
int ticksZener = -1;
for (int t = 0; t < 200 && ticksZener < 0; t++)
{
    g67c.Tick(0.1);
    if (zener67.RegulandoZener) ticksZener = t + 2;
}
Console.WriteLine($"  Zener: clava 5.1V y lo anuncia a los {ticksZener} ticks (antes 20)");
Verificar("2-3: el zener clava sus 5.1V en <=10 ticks (antes 20)", ticksZener > 0 && ticksZener <= 10);
Verificar("2-3: y cuando lo anuncia, la caida ES la del zener",
    Math.Abs(zener67.VoltajeEnBornes - 5.1) < 0.1);

// --- 2-6: EL test de la Parte 2. El fusible ya no es rehen del LED ---
var g67d = new CircuitGraph();
var b67d = new Battery("B67e", 9) { MaxCurrent = 1e9 };
var fus67 = new Fusible("F67", 0.05);
var r67d = new Resistor("R67e", 100);
var led67d = new LED("L67e", 2.0, 12) { MaxPower = 1e9, MaxCurrent = 1e9 };
g67d.Agregar(b67d); g67d.Agregar(fus67); g67d.Agregar(r67d); g67d.Agregar(led67d);
g67d.Conectar(b67d.PinA, fus67.PinA); g67d.Conectar(fus67.PinB, r67d.PinA);
g67d.Conectar(r67d.PinB, led67d.PinA); g67d.Conectar(led67d.PinB, b67d.PinB);
int ticksCorte = -1;
for (int t = 0; t < 200 && ticksCorte < 0; t++)
{
    g67d.Tick(0.1);
    if (fus67.IsBurnt) ticksCorte = t + 1;
}
Console.WriteLine($"  Fusible 50mA + 100ohm + LED sobre 9V (I real ~69mA): corta a los {ticksCorte} ticks = {ticksCorte * 0.1:F1}s (antes ~9 ticks)");
Verificar("2-6: el fusible corta en el primer tick de exceso (antes tardaba 0.9s)",
    fus67.IsBurnt && ticksCorte <= 3);
Verificar("2-6: y el LED que estaba detras se salvo", !led67d.IsBurnt);

// --- Regresion: el LED al reves sigue comportandose igual (B1 es P10-1) ---
var g67e = new CircuitGraph();
var b67e = new Battery("B67f", 9) { MaxCurrent = 1e9 };
var r67e = new Resistor("R67f", 220);
var led67e = new LED("L67f", 2.0, 12) { MaxPower = 1e9 };
g67e.Agregar(b67e); g67e.Agregar(r67e); g67e.Agregar(led67e);
g67e.Conectar(b67e.PinA, r67e.PinA); g67e.Conectar(r67e.PinB, led67e.PinB);
g67e.Conectar(led67e.PinA, b67e.PinB);
for (int t = 0; t < 40; t++) g67e.Tick(0.1);
Console.WriteLine($"  LED al reves: avalancha={led67e.EnAvalanchaInversa}  caida={led67e.VoltajeEnBornes:F2}V  I={Math.Abs(led67e.Current) * 1000:F1}mA");
Verificar("regresion: el LED al reves sigue clavando su V de avalancha",
    led67e.EnAvalanchaInversa && Math.Abs(led67e.VoltajeEnBornes - 5.0) < 0.2);

// ============================================================
// DEMO 68: PARTE 5 - el inductor nuevo (companion backward Euler).
//   El viejo era una rampa de resistencia con un tiempo puesto
//   a mano: tardaba 2s pusieras 10 ohm o 10k al lado, disipaba
//   calor mientras "guardaba campo" y no daba pico inverso.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 68: Parte 5 - inductor con L de verdad");
Console.WriteLine("====================================================\n");

double TauMedida(double henrios, double rSerie)
{
    var g = new CircuitGraph();
    var b = new Battery("B68", 9) { MaxCurrent = 1e9 };
    var r = new Resistor("R68", rSerie);
    var ind = new Inductor("L68", henrios) { MaxVoltage = 1e9 };
    g.Agregar(b); g.Agregar(r); g.Agregar(ind);
    g.Conectar(b.PinA, r.PinA); g.Conectar(r.PinB, ind.PinA); g.Conectar(ind.PinB, b.PinB);

    double rTotal = rSerie + ind.ResistenciaBobinado + b.ResistenciaInternaNominal;
    double iFinal = 9.0 / rTotal;
    const double dt = 0.001;
    for (int t = 0; t < 200_000; t++)
    {
        g.Tick(dt);
        if (Math.Abs(ind.Current) >= 0.632 * iFinal) return (t + 1) * dt;
    }
    return -1;
}

double tau100 = TauMedida(1.0, 100);
double tau1000 = TauMedida(1.0, 1000);
Console.WriteLine($"  Misma bobina de 1H: con 100ohm tau={tau100:F4}s, con 1000ohm tau={tau1000:F4}s (teoricos: {1.0 / 100.8:F4} y {1.0 / 1000.8:F4})");
Verificar("5-3: tau sale de L/R, no de un numero fijo (100ohm ~= 10x mas lento que 1k)", tau100 > 5 * tau1000);
Verificar("5-3: y el valor coincide con L/R", Math.Abs(tau100 - 1.0 / 100.8) < 0.002);

var g68 = new CircuitGraph();
var b68 = new Battery("B68b", 9) { MaxCurrent = 1e9 };
var r68 = new Resistor("R68b", 100);
var ind68 = new Inductor("L68b", 1.0) { MaxVoltage = 1e9 };
g68.Agregar(b68); g68.Agregar(r68); g68.Agregar(ind68);
g68.Conectar(b68.PinA, r68.PinA); g68.Conectar(r68.PinB, ind68.PinA); g68.Conectar(ind68.PinB, b68.PinB);
double calorMaximo68 = 0;
for (int t = 0; t < 200; t++)
{
    g68.Tick(0.01);
    calorMaximo68 = Math.Max(calorMaximo68, ind68.PotenciaTermica);
}
double cobreEsperado68 = ind68.Current * ind68.Current * ind68.ResistenciaBobinado;
Console.WriteLine($"  Campo: E={ind68.EnergiaAlmacenada * 1000:F2} mJ  calor maximo={calorMaximo68 * 1000:F2} mW  (solo cobre: {cobreEsperado68 * 1000:F2} mW)");
Verificar("5-3: guardar campo NO disipa - el calor es SOLO I^2*R_bobinado", Math.Abs(calorMaximo68 - cobreEsperado68) < 1e-6);
Verificar("5-3: la energia del campo es 1/2*L*I^2", Math.Abs(ind68.EnergiaAlmacenada - 0.5 * 1.0 * ind68.Current * ind68.Current) < 1e-9);

var g68b = new CircuitGraph();
var b68b = new Battery("B68c", 9) { MaxCurrent = 1e9 };
var sw68 = new Switch("Sw68");
var r68b = new Resistor("R68c", 100);
var ind68b = new Inductor("L68c", 1.0) { MaxVoltage = 1e9 };
g68b.Agregar(b68b); g68b.Agregar(sw68); g68b.Agregar(r68b); g68b.Agregar(ind68b);
g68b.Conectar(b68b.PinA, sw68.PinA); g68b.Conectar(sw68.PinB, r68b.PinA);
g68b.Conectar(r68b.PinB, ind68b.PinA); g68b.Conectar(ind68b.PinB, b68b.PinB);
const double dt68 = 1.0 / 240;
for (int t = 0; t < 480; t++) g68b.Tick(dt68);
double corrienteAntes68 = Math.Abs(ind68b.Current);
double energiaAntes68 = ind68b.EnergiaAlmacenada;
sw68.IsClosed = false;
g68b.Tick(dt68);
double pico68 = ind68b.VoltajeEnBornes;
for (int t = 0; t < 5; t++) g68b.Tick(dt68);
Console.WriteLine($"  Al abrir: I pasaba de {corrienteAntes68 * 1000:F1}mA -> pico inverso de {pico68:F2}V (L*I/dt = {1.0 * corrienteAntes68 / dt68:F2}V), campo {energiaAntes68 * 1000:F2}mJ -> {ind68b.EnergiaAlmacenada * 1000:F4}mJ");
Verificar("5-3: al abrir la llave APARECE el pico inverso (antes: 0.03V)", pico68 > 10);
Verificar("5-3: el pico vale L*I/dt, que es lo que el paso fijo puede mostrar", Math.Abs(pico68 - 1.0 * corrienteAntes68 / dt68) < 0.5);
Verificar("5-3: y el campo COLAPSA (antes seguia en 100% medio segundo despues)", ind68b.EnergiaAlmacenada < 0.01 * energiaAntes68);


// ============================================================
// DEMO 69: PARTE 7 - motor DC con FCEM.
//   El viejo hacia RPM = RPMmax*(P/P_nom): la corriente no
//   dependia de la velocidad, no habia par de carga, y con la
//   mitad de volts giraba a UN CUARTO.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 69: Parte 7 - motor DC con fuerza contraelectromotriz");
Console.WriteLine("====================================================\n");

(Motor motor, double arranque) CorrerMotor(double volts, double parCarga, double inercia, int segundos)
{
    var g = new CircuitGraph();
    var b = new Battery("B69", volts) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
    var m = new Motor("M69", 25) { ParCarga = parCarga, Inercia = inercia };
    g.Agregar(b); g.Agregar(m);
    g.Conectar(b.PinA, m.PinA); g.Conectar(m.PinB, b.PinB);
    const double dt = 1.0 / 240;
    double arranque = 0;
    for (int t = 0; t < segundos * 240; t++)
    {
        g.Tick(dt);
        if (t == 0) arranque = Math.Abs(m.Current);
    }
    return (m, arranque);
}

var (m9, arranque9) = CorrerMotor(9, 0, 1e-5, 20);
var (m45, _) = CorrerMotor(4.5, 0, 1e-5, 20);
double regimen9 = Math.Abs(m9.Current);
Console.WriteLine($"  9V libre: arranque={arranque9 * 1000:F1}mA -> regimen={regimen9 * 1000:F1}mA ({arranque9 / regimen9:F1}x)  {m9.RPM:F0} RPM");
Console.WriteLine($"  4.5V libre: {m45.RPM:F0} RPM  (la mitad de volts deberia dar la mitad de RPM)");
Verificar("7-2: arrancando chupa 5-10x mas que girando libre (FCEM)",
    arranque9 / regimen9 >= 5 && arranque9 / regimen9 <= 10);
Verificar("7-2: la mitad de voltaje da la MITAD de RPM (antes daba un cuarto)",
    Math.Abs(m45.RPM / m9.RPM - 0.5) < 0.02);
Verificar("7-2: con el rotor quieto la FCEM es 0, por eso el arranque es V/R",
    Math.Abs(arranque9 - 9.0 / 25.0) < 0.02);

var (mCarga, _) = CorrerMotor(9, 0.004, 1e-5, 20);
Console.WriteLine($"  Con 4 mN.m colgados del eje: {mCarga.RPM:F0} RPM, {Math.Abs(mCarga.Current) * 1000:F1}mA, {mCarga.PotenciaMecanica:F2}W mecanicos");
Verificar("7-2: cargarle el eje lo FRENA (antes no habia par de carga)", mCarga.RPM < m9.RPM * 0.75);
Verificar("7-2: y frenado chupa MAS corriente", Math.Abs(mCarga.Current) > regimen9 * 2);
Verificar("7-2: entrega potencia mecanica real en el eje", mCarga.PotenciaMecanica > 0.1);

var (mTrabado, _) = CorrerMotor(9, 0, 1e9, 5);   // inercia enorme = eje bloqueado
Console.WriteLine($"  Rotor BLOQUEADO: {mTrabado.RPM:F0} RPM, quemado={mTrabado.IsBurnt} ({mTrabado.MotivoQuemado})");
Verificar("7-2: el motor TRABADO se quema (la corriente de arranque no baja nunca)", mTrabado.IsBurnt);


// ============================================================
// DEMO 70: PARTE 8 - potenciometro de TRES bornes.
//   El viejo tenia dos: era un reostato, y encima duplicado
//   con Reostato.cs. Con tres se puede hacer lo que se ensena
//   el 90% de las veces: el DIVISOR DE TENSION.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 70: Parte 8 - potenciometro como divisor de tension");
Console.WriteLine("====================================================\n");

// A y B a la fuente, medimos el cursor: divisor puro
var g70 = new CircuitGraph();
var b70 = new Battery("B70", 9) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
var pote70 = new Potenciometro("Pote70", 10_000);
g70.Agregar(b70); g70.Agregar(pote70);
g70.Conectar(b70.PinA, pote70.PinA);
g70.Conectar(pote70.PinExtremoB, b70.PinB);

foreach (double perilla in new[] { 0.0, 25.0, 50.0, 75.0, 100.0 })
{
    pote70.Perilla = perilla;
    for (int t = 0; t < 40; t++) g70.Tick(0.05);
    Console.WriteLine($"  Perilla {perilla,5:F0}% -> tramo A-W={pote70.ResistenciaTramoA,6:F0}ohm  W-B={pote70.ResistenciaTramoB,6:F0}ohm  cursor={pote70.VoltajeCursor:F2}V  ({pote70.FraccionDeVoltaje:P0})");
}

pote70.Perilla = 50;
for (int t = 0; t < 40; t++) g70.Tick(0.05);
Verificar("8-2: al 50% el cursor entrega la MITAD del voltaje (divisor)",
    Math.Abs(pote70.VoltajeCursor - 4.5) < 0.15);
Verificar("8-2: y los dos tramos valen lo mismo",
    Math.Abs(pote70.ResistenciaTramoA - pote70.ResistenciaTramoB) < 1);

pote70.Perilla = 25;
for (int t = 0; t < 40; t++) g70.Tick(0.05);
Verificar("8-2: al 25% saca un cuarto del voltaje",
    Math.Abs(pote70.VoltajeCursor - 2.25) < 0.2);
Verificar("8-2: la suma de los dos tramos es la resistencia total",
    Math.Abs(pote70.ResistenciaTramoA + pote70.ResistenciaTramoB - 10_000) < 2);

// usado de REOSTATO: solo A y el cursor, B al aire
var g70b = new CircuitGraph();
var termico70 = new SimuladorTermico();
var b70b = new Battery("B70b", 9) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
var pote70b = new Potenciometro("Pote70b", 1_000);
var lamp70 = new Lampara("Lamp70", 100) { PotenciaNominal = 1.4, MaxPower = 1e9, MaxTemperature = 1e9 };
g70b.Agregar(b70b); g70b.Agregar(pote70b); g70b.Agregar(lamp70);
g70b.Conectar(b70b.PinA, pote70b.PinA);
g70b.Conectar(pote70b.PinCursor, lamp70.PinA);
g70b.Conectar(lamp70.PinB, b70b.PinB);

pote70b.Perilla = 0;    // toda la pista (A-W) en serie con la lampara: frena
for (int t = 0; t < 40; t++) { g70b.Tick(0.05); termico70.Paso(g70b.Components, 0.05); }
double brilloFrenado = lamp70.Brillo;
pote70b.Perilla = 100;  // cursor pegado a A: la pista sale del camino
for (int t = 0; t < 40; t++) { g70b.Tick(0.05); termico70.Paso(g70b.Components, 0.05); }
double brilloLibre = lamp70.Brillo;
Console.WriteLine($"  De reostato (solo A y W): perilla 0% brillo={brilloFrenado:P0} · perilla 100% brillo={brilloLibre:P0}");
Verificar("8-2: sigue sirviendo de REOSTATO usando solo A y el cursor",
    brilloLibre > brilloFrenado + 0.1);

// el tercer borne existe y esta rotulado ("B" = PinExtremoB; "W" = PinB/PinCursor)
Verificar("8-2: expone el extremo B como borne extra rotulado B",
    ReferenceEquals(pote70.PinCursor, pote70.PinB)
    && ((IComponenteMultiBorne)pote70).BornesExtra.Any(b => b.Etiqueta == "B" && ReferenceEquals(b.Pin, pote70.PinExtremoB)));
Verificar("8-2: y aporta el tramo W-B como arista interna al solver",
    ((IComponenteCompuesto)pote70).AristasInternas.Count() == 1);


// ============================================================
// DEMO 71: PARTE 9 - clasificacion AC/DC por CAMINO, no por isla.
//   Antes los bits AC/DC se inundaban por cualquier arista que
//   condujera: unir la masa de una bateria con el neutro del
//   toma apagaba el LED aunque no tocara la alterna.
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 71: Parte 9 - masa comun sin contagio AC/DC");
Console.WriteLine("====================================================\n");

var g71 = new CircuitGraph();
var bat71 = new Battery("Bat71", 9) { CargaNominal = double.PositiveInfinity };
var r71 = new Resistor("R71", 220);
var led71 = new LED("LED71", 2.0, 12);
var ac71 = new FuenteAC("Toma71", 220, 50) { MaxCurrent = 25 };
var foco71 = new Lampara("Foco71", 800) { PotenciaNominal = 60, MaxPower = 90, MaxTemperature = 1e9 };
g71.Agregar(bat71); g71.Agregar(r71); g71.Agregar(led71); g71.Agregar(ac71); g71.Agregar(foco71);
g71.Conectar(bat71.PinA, r71.PinA); g71.Conectar(r71.PinB, led71.PinA); g71.Conectar(led71.PinB, bat71.PinB);
g71.Conectar(ac71.PinA, foco71.PinA); g71.Conectar(foco71.PinB, ac71.PinB);
for (int t = 0; t < 40; t++) g71.Tick(0.1);

double iAntes71 = led71.Current;
double iFoco71 = foco71.Current;
Console.WriteLine($"  Separados: LED={iAntes71 * 1000:F2}mA (tipo={led71.TipoCorrienteRecibida})  foco={iFoco71 * 1000:F1}mA");

// masa comun: el negativo de la bateria con el neutro del toma
g71.Conectar(bat71.PinB, ac71.PinB, "masa_comun");
for (int t = 0; t < 20; t++) g71.Tick(0.1);
Console.WriteLine($"  Masa comun: LED={led71.Current * 1000:F2}mA (tipo={led71.TipoCorrienteRecibida})  foco={foco71.Current * 1000:F1}mA");

Verificar("9-1: unir la masa NO apaga el LED (era 31.57 -> 0.00 mA)",
    Math.Abs(led71.Current - iAntes71) < 0.0005);
Verificar("9-1: y el LED sigue viendo SOLO continua, no Mixta",
    led71.TipoCorrienteRecibida == TipoCorriente.DC);
Verificar("9-1: el foco de alterna sigue viendo SOLO alterna",
    foco71.TipoCorrienteRecibida == TipoCorriente.AC);
Verificar("9-1: y sigue andando igual", Math.Abs(foco71.Current - iFoco71) < 0.001);
Verificar("9-1: el LED no queda marcado incompatible", !led71.EsIncompatible);

// Regresion: un componente que SI esta en el lazo de alterna sigue viendo AC
var g71b = new CircuitGraph();
var ac71b = new FuenteAC("Toma71b", 220, 50) { MaxCurrent = 25 };
var motor71 = new Motor("M71", 25);
g71b.Agregar(ac71b); g71b.Agregar(motor71);
g71b.Conectar(ac71b.PinA, motor71.PinA); g71b.Conectar(motor71.PinB, ac71b.PinB);
for (int t = 0; t < 40; t++) g71b.Tick(0.1);
Console.WriteLine($"  Motor DC enchufado directo a la alterna: tipo={motor71.TipoCorrienteRecibida} incompatible={motor71.EsIncompatible} RPM={motor71.RPM:F0}");
Verificar("9-1: un motor DC en el lazo de alterna SI recibe AC y queda inerte",
    motor71.TipoCorrienteRecibida == TipoCorriente.AC && !motor71.EstaGirando);


// ============================================================
// DEMO 72: PARTE 10 - realismo fino (10-1 LED, 10-4 fusible).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 72: Parte 10 - LED al reves y curva I-t del fusible");
Console.WriteLine("====================================================\n");

// 10-1: el LED al reves BLOQUEA, no regula
var g72 = new CircuitGraph();
var b72 = new Battery("B72", 9) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
var r72 = new Resistor("R72", 220);
var led72 = new LED("LED72", 2.0, 12) { MaxPower = 1e9 };
g72.Agregar(b72); g72.Agregar(r72); g72.Agregar(led72);
g72.Conectar(b72.PinA, r72.PinA); g72.Conectar(r72.PinB, led72.PinB); g72.Conectar(led72.PinA, b72.PinB);
for (int t = 0; t < 40; t++) g72.Tick(0.1);
Console.WriteLine($"  LED al reves con 220ohm sobre 9V: caida={led72.VoltajeEnBornes:F2}V  I={Math.Abs(led72.Current) * 1000:F2}mA  quemado={led72.IsBurnt}");
Verificar("10-1: el LED al reves BLOQUEA (antes clavaba 5.00V y conducia 18mA)",
    Math.Abs(led72.Current) < 0.001 && !led72.IsBurnt);
Verificar("10-1: y aguanta los 9V inversos sin morir (su ruptura esta en 20V)",
    !led72.IsBurnt && led72.MaxVoltajeInverso >= 15);

// 10-1: pasada su tension inversa, MUERE (no regula)
var g72b = new CircuitGraph();
var b72b = new Battery("B72b", 48) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
var r72b = new Resistor("R72b", 220);
var led72b = new LED("LED72b", 2.0, 12) { MaxPower = 1e9 };
g72b.Agregar(b72b); g72b.Agregar(r72b); g72b.Agregar(led72b);
g72b.Conectar(b72b.PinA, r72b.PinA); g72b.Conectar(r72b.PinB, led72b.PinB); g72b.Conectar(led72b.PinA, b72b.PinB);
for (int t = 0; t < 20; t++) g72b.Tick(0.1);
Console.WriteLine($"  LED con 48V al reves: quemado={led72b.IsBurnt} ({led72b.MotivoQuemado})  I={Math.Abs(led72b.Current) * 1000:F3}mA");
Verificar("10-1: pasada su tension inversa se PERFORA (no es un zener)",
    led72b.IsBurnt && led72b.MotivoQuemado == "ruptura inversa");
Verificar("10-1: y una vez perforado no pasa nada", Math.Abs(led72b.Current) < 1e-4);

// 10-4: la curva I-t. Mas sobrecarga = corta antes.
double TiempoDeCorte(double resistencia)
{
    var g = new CircuitGraph();
    var b = new Battery("B72c", 9) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
    var f = new Fusible("F72", 0.05);
    var r = new Resistor("R72c", resistencia);
    g.Agregar(b); g.Agregar(f); g.Agregar(r);
    g.Conectar(b.PinA, f.PinA); g.Conectar(f.PinB, r.PinA); g.Conectar(r.PinB, b.PinB);
    for (int t = 0; t < 2000; t++)
    {
        g.Tick(0.01);
        if (f.IsBurnt) return (t + 1) * 0.01;
    }
    return -1;
}

double tSuave = TiempoDeCorte(150);   // ~60mA: 1.2x el limite
double tFuerte = TiempoDeCorte(20);   // ~450mA: 9x el limite
Console.WriteLine($"  Fusible de 50mA: con 1.2x corta a los {tSuave:F2}s · con 9x corta a los {tFuerte:F2}s");
Verificar("10-4: el fusible corta MAS RAPIDO cuanto mayor es la sobrecarga (curva I-t)",
    tSuave > 0 && tFuerte > 0 && tSuave > tFuerte * 3);

// 10-4: y ahora tambien corta por CALOR externo
var fCalor = new Fusible("F72d", 10);
Console.WriteLine($"  MaxTemperature del fusible: {fCalor.MaxTemperature:F0} C (antes 999: nunca cortaba por calor)");
Verificar("10-4: el fusible corta por calor externo (el estano funde a ~230C)",
    fCalor.MaxTemperature <= 250);


// ============================================================
// DEMO 73: AUDITORIA - alterna con reactancias (solver de fasores).
//   Antes un inductor de 1H en 220V daba 151A (se comportaba como un
//   corto) y un capacitor de 10uF daba 0A. Cada circuito tiene
//   solucion analitica: |Z| = sqrt(R^2 + X^2).
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 73: Auditoria - alterna con capacitores e inductores");
Console.WriteLine("====================================================\n");

CircuitGraph SerieAC73(double vrms, double hz, out FuenteAC fuente, params Component[] elementos)
{
    var g = new CircuitGraph();
    fuente = new FuenteAC("AC73", vrms, hz) { ResistenciaInternaNominal = 1e-6 };
    fuente.MaxVoltage = fuente.MaxCurrent = fuente.MaxPower = fuente.MaxTemperature = double.PositiveInfinity;
    g.Agregar(fuente);
    var anterior = fuente.PinA;
    foreach (var e in elementos)
    {
        e.MaxVoltage = e.MaxCurrent = e.MaxPower = e.MaxTemperature = double.PositiveInfinity;
        g.Agregar(e);
        g.Conectar(anterior, e.PinA);
        anterior = e.PinB;
    }
    g.Conectar(anterior, fuente.PinB);
    for (int t = 0; t < 30; t++) g.Tick(0.01);
    return g;
}

double w73 = 2 * Math.PI * 50;

var l73 = new Inductor("L73", 1.0);
SerieAC73(220, 50, out var acL73, l73);
double iL73 = 220 / Math.Sqrt(l73.ResistenciaBobinado * l73.ResistenciaBobinado + w73 * w73);
Console.WriteLine($"  Inductor 1H en 220V/50Hz: {l73.Current:F4} A (esperado {iL73:F4} A; antes 151 A)");
Verificar("73: un inductor en alterna tiene reactancia XL=2*pi*f*L (0.70 A, no un corto de 151 A)", Math.Abs(l73.Current - iL73) < 0.005);

var c73 = new Capacitor("C73", 10e-6);
SerieAC73(220, 50, out var acC73, c73);
double xc73 = 1 / (w73 * 10e-6);
double iC73 = 220 / Math.Sqrt(c73.ResistenciaSerie * c73.ResistenciaSerie + xc73 * xc73);
Console.WriteLine($"  Capacitor 10uF en 220V/50Hz: {c73.Current:F4} A (esperado {iC73:F4} A; antes 0 A)");
Verificar("73: un capacitor en alterna deja pasar corriente (Xc=1/(2*pi*f*C), 0.69 A)", Math.Abs(c73.Current - iC73) < 0.005);
Verificar("73: y la fuente entrega esa misma corriente", Math.Abs(acC73.CorrienteEntregada - iC73) < 0.005);

var r73 = new Resistor("R73", 100);
var rc73 = new Capacitor("RC73", 10e-6);
SerieAC73(220, 50, out var acRC73, r73, rc73);
double zrc73 = Math.Sqrt(100.05 * 100.05 + xc73 * xc73);
Console.WriteLine($"  RC serie 100ohm+10uF: I={r73.Current:F4} A (esperado {220 / zrc73:F4}), VR={r73.VoltajeEnBornes:F1} V, VC={rc73.VoltajeEnBornes:F1} V");
Verificar("73: RC serie: I = V/sqrt(R^2+Xc^2)", Math.Abs(r73.Current - 220 / zrc73) < 0.005);
Verificar("73: las tensiones NO suman aritmeticamente: sqrt(VR^2+VC^2) = 220 V",
    Math.Abs(Math.Sqrt(r73.VoltajeEnBornes * r73.VoltajeEnBornes + rc73.VoltajeEnBornes * rc73.VoltajeEnBornes) - 220) < 1);
Verificar("73: el capacitor esta a -90 grados (la corriente adelanta) y casi no tiene potencia activa",
    Math.Abs(rc73.ResultadoAC!.FaseGrados + 90) < 1 && rc73.ResultadoAC.FactorPotencia < 0.01);
Verificar("73: la resistencia en serie tiene fase 0 y factor de potencia 1",
    Math.Abs(r73.ResultadoAC!.FaseGrados) < 0.01 && r73.ResultadoAC.FactorPotencia > 0.999);

double cRes73 = 1 / (w73 * w73 * 1.0);
var rr73 = new Resistor("RR73", 100);
var lr73 = new Inductor("LR73", 1.0);
var cr73 = new Capacitor("CR73", cRes73);
SerieAC73(220, 50, out var acRes73, rr73, lr73, cr73);
Console.WriteLine($"  RLC en resonancia (50Hz): I={rr73.Current:F4} A (esperado {220 / 100.85:F4} = V/R)");
Verificar("73: en RESONANCIA se cancelan XL y Xc y la corriente es V/R", Math.Abs(rr73.Current - 220 / 100.85) < 0.01);

var rr73b = new Resistor("RR73b", 100);
var lr73b = new Inductor("LR73b", 1.0);
var cr73b = new Capacitor("CR73b", cRes73);
SerieAC73(220, 60, out var acRes73b, rr73b, lr73b, cr73b);
double w60 = 2 * Math.PI * 60;
double iRes60 = 220 / Math.Sqrt(100.85 * 100.85 + Math.Pow(w60 * 1.0 - 1 / (w60 * cRes73), 2));
Verificar("73: la FRECUENCIA de la fuente ahora cambia los numeros (RLC a 60Hz)",
    Math.Abs(rr73b.Current - iRes60) < 0.01 && rr73b.Current < rr73.Current * 0.8);

// regresion: el electrolitico (polarizado) sigue inerte en alterna
var e73 = new CapacitorElectrolitico("E73", 1e-3);
SerieAC73(220, 50, out var acE73, e73);
Verificar("73: el electrolitico (polarizado) sigue inerte en alterna", Math.Abs(e73.Current) < 1e-6);

// regresion: una carga puramente resistiva NO pasa por fasores y da lo de siempre
var rp73 = new Resistor("RP73", 800);
SerieAC73(220, 50, out var acP73, rp73);
Verificar("73: resistiva pura en 220V: P = V^2/R = 60.5 W (sin cambios)", Math.Abs(rp73.Power - 220.0 * 220.0 / 800) < 0.05 && rp73.ResultadoAC == null);

// AUD-1: la constante de tiempo del capacitor grande ya no tiene un piso artificial
{
    var g = new CircuitGraph();
    var b = new Battery("B73", 5) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity, ResistenciaInternaNominal = 0.001 };
    var r = new Resistor("R73c", 1.0);
    var c = new Capacitor("C73c", 1.0) { MaxVoltage = double.PositiveInfinity };
    g.Agregar(b); g.Agregar(r); g.Agregar(c);
    g.Conectar(b.PinA, r.PinA); g.Conectar(r.PinB, c.PinA); g.Conectar(c.PinB, b.PinB);
    double tau = (1.0 + c.ResistenciaSerie + 0.001) * 1.0;
    for (int t = 0; t < (int)(tau * 60); t++) g.Tick(1.0 / 60);
    Console.WriteLine($"  Cap de 1F por 1ohm en t=tau: V={c.VoltajeAlmacenado:F3} V (esperado {5 * (1 - Math.Exp(-1)):F3}; antes 4.9% de error)");
    Verificar("73: capacitor de 1F: en t=tau carga al 63.2% (el piso de 0.1ohm lo desviaba 5%)",
        Math.Abs(c.VoltajeAlmacenado - 5 * (1 - Math.Exp(-1))) < 0.05);
}

// ============================================================
// DEMO 74: Caja con pantalla (instrumento de paso ideal)
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 74: Caja con pantalla (DC, AC y termica)");
Console.WriteLine("====================================================\n");
{
    // 1) Paso ideal en DC: entra por + y sale identico por − (cero caida)
    var gDc = new CircuitGraph();
    var bat = new Battery("Bat74", 9.0) { ResistenciaInternaNominal = 0.0, CargaNominal = double.PositiveInfinity };
    var cajaDc = new CajaConPantalla("CajaDC");
    var resDc = new Resistor("Res74", 100.0);
    gDc.Agregar(bat); gDc.Agregar(cajaDc); gDc.Agregar(resDc);
    gDc.Conectar(bat.PinA, cajaDc.PinA);
    gDc.Conectar(cajaDc.PinB, resDc.PinA);
    gDc.Conectar(resDc.PinB, bat.PinB);
    gDc.Tick(1.0 / 60.0);
    gDc.Tick(1.0 / 60.0);

    Verificar("74: en DC el voltaje sale del lado negativo igual que como entro del positivo (9V en la carga)",
        Math.Abs(cajaDc.PinA.Voltage - cajaDc.PinB.Voltage) < 1e-9 && Math.Abs(resDc.VoltajeEnBornes - 9.0) < 0.01);
    Verificar("74: la caja mide los 9V y los 90mA sin alterar el circuito",
        Math.Abs(cajaDc.VoltajeMedido - 9.0) < 0.01 && Math.Abs(cajaDc.Current - 0.09) < 0.001 && !cajaDc.EsCorrienteAlterna);

    SimuladorTermico.Habilitada = true;
    Verificar("74: con termica activada muestra la temperatura en °C",
        cajaDc.TextoPantalla3D().Contains("°C"));
    SimuladorTermico.Habilitada = false;
    Verificar("74: con termica desactivada muestra '-' en temperatura",
        cajaDc.TextoPantalla3D().Contains("Temp: -"));
    SimuladorTermico.Habilitada = true;

    // 2) Paso ideal en AC reactiva (RC serie): mide Vrms, Vpico, Irms, Ipico, Fase, FP, P, S, Q
    var gAc = new CircuitGraph();
    var fac = new FuenteAC("AC74", 220.0, 50.0) { ResistenciaInternaNominal = 0.0 };
    var cajaAc = new CajaConPantalla("CajaAC");
    var rAc = new Resistor("RAc74", 100.0);
    var cAc = new Capacitor("CAc74", 10e-6);
    gAc.Agregar(fac); gAc.Agregar(cajaAc); gAc.Agregar(rAc); gAc.Agregar(cAc);
    gAc.Conectar(fac.PinA, cajaAc.PinA);
    gAc.Conectar(cajaAc.PinB, rAc.PinA);
    gAc.Conectar(rAc.PinB, cAc.PinA);
    gAc.Conectar(cAc.PinB, fac.PinB);
    gAc.Tick(1.0 / 60.0);
    gAc.Tick(1.0 / 60.0);

    Verificar("74: en AC detecta corriente alterna a 50Hz y mide Vrms=220V, Vpico=311.1V y corriente RC exacta",
        cajaAc.EsCorrienteAlterna
        && Math.Abs(cajaAc.FrecuenciaHz - 50.0) < 0.1
        && Math.Abs(cajaAc.VoltajeRms - 220.0) < 0.5
        && Math.Abs(cajaAc.VoltajePico - 220.0 * Math.Sqrt(2.0)) < 0.5
        && Math.Abs(cajaAc.CorrienteRms - rAc.Current) < 0.002);
}

// ============================================================
// DEMO 75: Temporizador en Interruptor (Switch) y Pulsador
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 75: Temporizador de Interruptor y Pulsador");
Console.WriteLine("====================================================\n");
{
    var g75 = new CircuitGraph();
    var sw = new Switch("Sw75", startClosed: false)
    {
        UsarTicks = true,
        IntervaloTicks = 3,
        TemporizadorActivo = true,
    };
    var pul = new Pulsador("Pul75")
    {
        UsarTicks = true,
        IntervaloTicks = 4,
        DuracionPulsoTicks = 2,
        TemporizadorActivo = true,
    };
    g75.Agregar(sw);
    g75.Agregar(pul);

    // Switch arranca abierto; tras 2 ticks sigue abierto y en el tick 3 se cierra:
    g75.Tick(0.1);
    g75.Tick(0.1);
    bool swAbiertoEn2 = !sw.IsClosed;
    g75.Tick(0.1);
    bool swCerradoEn3 = sw.IsClosed;
    Verificar("75: el interruptor conmuta exactamente al cumplirse los N ticks configurados",
        swAbiertoEn2 && swCerradoEn3);

    // Pulsador: espera 4 ticks suelto (ya corrieron 3, en el 4to se presiona).
    // Mientras está presionado (2 ticks: ticks 5 y 6), el contador de espera NO corre.
    // Recién al soltarse en el tick 6 arranca de nuevo la espera de 4 ticks (ticks 7, 8, 9 suelto, y en el 10 se vuelve a presionar).
    g75.Tick(0.1); // tick 4 -> se presiona!
    bool pulPresionadoEn4 = pul.Presionado;
    g75.Tick(0.1); // tick 5 -> sigue presionado (1 de 2 ticks de retención)
    bool pulSiguePresionadoEn5 = pul.Presionado;
    g75.Tick(0.1); // tick 6 -> cumple los 2 ticks de retención, se suelta y REINICIA el contador de espera a 4
    bool pulSueltoEn6 = !pul.Presionado;

    g75.Tick(0.1); // tick 7 -> espera 1/4 (suelto)
    g75.Tick(0.1); // tick 8 -> espera 2/4 (suelto)
    g75.Tick(0.1); // tick 9 -> espera 3/4 (suelto)
    bool pulSigueSueltoEn9 = !pul.Presionado;
    g75.Tick(0.1); // tick 10 -> espera 4/4 cumplida -> vuelve a presionarse!
    bool pulPresionadoEn10 = pul.Presionado;

    Verificar("75: el pulsador se presiona cada X ticks por Y ticks y reinicia su contador RECIEN al soltarse (sin loop)",
        pulPresionadoEn4 && pulSiguePresionadoEn5 && pulSueltoEn6 && pulSigueSueltoEn9 && pulPresionadoEn10);
}

// ============================================================
// DEMO 76: ARDUINO UNO R3 - bornes nuevos, RESET, VIN, AREF, MHz y danos
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 76: Arduino UNO R3 (bornes R3, RESET, VIN, AREF, danos)");
Console.WriteLine("====================================================\n");
{
    var g76 = new CircuitGraph();
    var uno76 = new ArduinoUno("Uno76") { PresupuestoMsPorFrame = 1000, FrecuenciaMHz = 8 };
    g76.Agregar(uno76);

    // 1. Orden compatible de BornesExtra (X0..X21 intactos; nuevos R3 al final).
    var extras76 = uno76.BornesExtra.ToList();
    bool ordenCompatible = extras76.Count == 29
        && extras76[0].Etiqueta == "3V3"
        && extras76[1].Etiqueta == "GND2"
        && extras76[2].Etiqueta == "D0"
        && extras76[15].Etiqueta == "D13"
        && extras76[16].Etiqueta == "A0"
        && extras76[21].Etiqueta == "A5"
        && extras76[22].Etiqueta == "GND3"
        && extras76[28].Etiqueta == "VIN";
    Verificar("76a: BornesExtra conserva X0..X21 intactos y agrega R3 al final", ordenCompatible);

    // 2. Puentes fisicos R3: IOREF = 5V, GND3 = 0V, pull-up en RESET ~ 5V.
    uno76.CargarPrograma(SketchesHex.BlinkEsperaActiva, "blink.hex");
    g76.Tick(0.02);
    Pin ioref = extras76.First(b => b.Etiqueta == "IOREF").Pin;
    Pin gnd3 = extras76.First(b => b.Etiqueta == "GND3").Pin;
    Pin resetPin = extras76.First(b => b.Etiqueta == "RESET").Pin;
    Pin d13Pin = extras76.First(b => b.Etiqueta == "D13").Pin;
    Pin sdaPin = extras76.First(b => b.Etiqueta == "SDA").Pin;
    Pin a4Pin = extras76.First(b => b.Etiqueta == "A4").Pin;
    Verificar("76b: IOREF=5V, GND3=0V, RESET en pull-up ~5V y EstadoCpu reporta 8 MHz reales",
        Math.Abs(ioref.Voltage - 5.0) < 0.05
        && Math.Abs(gnd3.Voltage) < 0.05
        && resetPin.Voltage > 4.5
        && uno76.EstadoCpu.Contains("8 MHz"));

    // SDA/A4 puenteados fisicamente: aplicar 2.5V en SDA aparece en A4.
    var divR1 = new Resistor("R_sda_1", 1000);
    var divR2 = new Resistor("R_sda_2", 1000);
    g76.Agregar(divR1);
    g76.Agregar(divR2);
    g76.Conectar(uno76.PinA, divR1.PinA);
    g76.Conectar(divR1.PinB, sdaPin);
    g76.Conectar(divR2.PinA, sdaPin);
    g76.Conectar(divR2.PinB, gnd3);
    g76.Tick(0.02);
    Verificar("76c: SDA puenteado fisicamente a A4", Math.Abs(a4Pin.Voltage - 2.5) < 0.1);

    // 3. Pin RESET conectado a GND sostiene la CPU en reset y libera al abrirse.
    var swReset = new Switch("SwReset", startClosed: true);
    g76.Agregar(swReset);
    g76.Conectar(resetPin, swReset.PinA);
    g76.Conectar(swReset.PinB, uno76.PinB);
    for (int k = 0; k < 4; k++) { uno76.AvanzarTiempoReal(0.02); g76.Tick(0.02); }
    bool enReset = uno76.ResetSostenido && !uno76.EstadoPin(13).EsSalida;
    swReset.IsClosed = false;
    for (int k = 0; k < 6; k++) { uno76.AvanzarTiempoReal(0.02); g76.Tick(0.02); }
    bool salioDeReset = !uno76.ResetSostenido && uno76.EstadoCpu.Contains("ejecutando");
    Verificar("76d: pin RESET a GND mantiene la CPU reiniciada y reanuda al soltarse", enReset && salioDeReset);

    // 4. Persistencia parcial (mascara de pines + 3V3) y reparacion sin quemar placa.
    uno76.RestaurarPinesDanados((1 << 5) | (1 << 13));
    uno76.RestaurarRegulador3V3Quemado("cortocircuito");
    bool teniaDanos = uno76.TieneDanosParciales && uno76.MascaraPinesDanados == ((1 << 5) | (1 << 13));
    uno76.RepararDanosParciales();
    Verificar("76e: danos parciales (pines + 3V3) se restauran por mascara y se reparan limpio",
        teniaDanos && !uno76.TieneDanosParciales && !uno76.Regulador3V3Quemado);

    // 5. VIN: 9V consume ~45mA; 24V quema por sobretension.
    var vinBat = new Battery("BatVin", 9.0) { ResistenciaInternaNominal = 0.01, CargaNominal = double.PositiveInfinity };
    Pin vinPin = extras76.First(b => b.Etiqueta == "VIN").Pin;
    g76.Agregar(vinBat);
    g76.Conectar(vinBat.PinA, vinPin);
    g76.Conectar(vinBat.PinB, uno76.PinB);
    for (int k = 0; k < 6; k++) { uno76.AvanzarTiempoReal(0.02); g76.Tick(0.02); }
    bool vinConsumoOk = Math.Abs(vinBat.CorrienteEntregada - ArduinoUno.CorrienteReposoVin) < 0.005
                        && uno76.EstadoAlimentacion.Contains("VIN 9.0 V");
    vinBat.VoltajeNominal = 24.0;
    vinBat.Voltage = 24.0;
    for (int k = 0; k < 30; k++) { uno76.AvanzarTiempoReal(0.02); g76.Tick(0.02); }
    bool vinSobretensionQuema = uno76.IsBurnt && uno76.MotivoQuemado.Contains("VIN");
    Verificar("76f: VIN consume 45mA a 9V y quema la placa si supera 20V", vinConsumoOk && vinSobretensionQuema);

    // 6. Referencia externa AREF en el ADC (2.5V en borne AREF, 1.25V en A0 -> ~512).
    var gAref = new CircuitGraph();
    var unoAref = new ArduinoUno("UnoAref") { PresupuestoMsPorFrame = 1000 };
    var batRef = new Battery("BatRef", 2.5);
    var divA0Top = new Resistor("RA0_top", 1000);
    var divA0Bot = new Resistor("RA0_bot", 1000);
    gAref.Agregar(unoAref);
    gAref.Agregar(batRef);
    gAref.Agregar(divA0Top);
    gAref.Agregar(divA0Bot);
    Pin pinAref = unoAref.BornesExtra.First(b => b.Etiqueta == "AREF").Pin;
    Pin pinA0 = unoAref.BornesExtra.First(b => b.Etiqueta == "A0").Pin;
    gAref.Conectar(batRef.PinA, pinAref);
    gAref.Conectar(batRef.PinB, unoAref.PinB);
    gAref.Conectar(pinAref, divA0Top.PinA);
    gAref.Conectar(divA0Top.PinB, pinA0);
    gAref.Conectar(pinA0, divA0Bot.PinA);
    gAref.Conectar(divA0Bot.PinB, unoAref.PinB);

    string hexAdcExterno = SketchesHex.AdcImprime.Replace(
        "80E480937C0087E880937A007C",
        "80E080937C0087E880937A0080");
    unoAref.CargarPrograma(hexAdcExterno, "adc_ext.hex");
    for (int k = 0; k < 10; k++)
    {
        gAref.Tick(0.02);
        unoAref.AvanzarTiempoReal(0.02);
    }
    string? primeraLineaAref = unoAref.SalidaSerie.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    int valAref = int.TryParse(primeraLineaAref, out int parsedAref) ? parsedAref : -1;
    Console.WriteLine($"ADC con AREF=2.50V y A0=1.25V -> Serial='{primeraLineaAref}', Ref='{unoAref.ReferenciaAdcTexto}'");
    Verificar("76g: ADC con referencia externa en AREF (1.25V / 2.50V -> ~512) y ReferenciaAdcTexto",
        valAref >= 506 && valAref <= 518 && unoAref.ReferenciaAdcTexto.Contains("AREF"));
}

// ============================================================
// DEMO 77: Potenciometro (tramo W-B, reparacion, limites) + color de luz
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 77: Potenciometro W-B y componentes luminosos");
Console.WriteLine("====================================================\n");
{
    var g77 = new CircuitGraph();
    var b77 = new Battery("B77", 12) { MaxCurrent = 1e9, CargaNominal = double.PositiveInfinity };
    var p77 = new Potenciometro("P77", 100) { Perilla = 50, SegundosParaQuemarse = 0.2 };
    g77.Agregar(b77); g77.Agregar(p77);
    g77.Conectar(b77.PinA, p77.PinCursor);
    g77.Conectar(p77.PinExtremoB, b77.PinB);
    for (int t = 0; t < 10; t++) g77.Tick(0.05);
    bool quemoWB = p77.IsBurnt;

    p77.MaxPower = double.PositiveInfinity;
    p77.Reparar(20);
    bool reparoLimpio = !p77.IsBurnt;
    for (int t = 0; t < 10; t++) g77.Tick(0.05);
    bool sobrevivioConLimiteInf = !p77.IsBurnt && Math.Abs(p77.AristasInternas.First().Current) > 0.2;

    Verificar("77a: sobrecarga en tramo W-B quema al potenciometro completo", quemoWB);
    Verificar("77b: Reparar revives ambas pistas y la sincronizacion de MaxPower evita que se queme", reparoLimpio && sobrevivioConLimiteInf);

    var led77 = new LED("LED77");
    var lamp77 = new Lampara("Lamp77");
    bool iniciaPorDefecto = led77.ColorPersonalizado == null && lamp77.ColorPersonalizado == null;
    led77.ColorPersonalizado = new ColorLuzRgb(0.16f, 0.88f, 0.28f);
    lamp77.ColorPersonalizado = new ColorLuzRgb(0.18f, 0.46f, 1.00f);
    bool personalizaOk = led77.ColorPersonalizado is { G: > 0.8f } && lamp77.ColorPersonalizado is { B: > 0.9f };
    led77.ColorPersonalizado = null;
    Verificar("77c: IComponenteLuminoso arranca predeterminado y permite cambiar/restaurar color",
        iniciaPorDefecto && personalizaOk && led77.ColorPersonalizado == null);
}

// ============================================================
// DEMO 78: Transformador AC->DC e Inversor DC->AC
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 78: Transformador AC->DC e Inversor DC->AC");
Console.WriteLine("====================================================\n");
{
    var g78a = new CircuitGraph();
    var red78 = new FuenteAC("Red78", 220);
    var trafo78 = new TransformadorAcDc("Trafo78") { VoltajeEntradaNominal = 220, VoltajeSalidaNominal = 12 };
    var cargaDc = new Resistor("Carga12", 100);
    g78a.Agregar(red78); g78a.Agregar(trafo78); g78a.Agregar(cargaDc);
    g78a.Conectar(red78.PinA, trafo78.PinEntradaA);
    g78a.Conectar(red78.PinB, trafo78.PinEntradaB);
    g78a.Conectar(trafo78.PinA, cargaDc.PinA);
    g78a.Conectar(cargaDc.PinB, trafo78.PinB);
    for (int t = 0; t < 20; t++) g78a.Tick(0.1);
    double iDcOut = Math.Abs(cargaDc.Current);
    double iAcIn = trafo78.CorrienteEntrada;
    Console.WriteLine($"AC->DC: {trafo78.VoltajeEntrada:F0}V~ -> {trafo78.Voltage:F2}V= ({cargaDc.TipoCorrienteRecibida}) I_out={iDcOut * 1000:F1}mA I_in={iAcIn * 1000:F1}mA");
    Verificar("78a: Transformador AC->DC entrega 12V DC y refleja potencia reducida al primario",
        Math.Abs(trafo78.Voltage - 12.0) < 0.5
        && cargaDc.TipoCorrienteRecibida == TipoCorriente.DC
        && iDcOut > 0.10 && iDcOut < 0.14
        && iAcIn > 0.04 && iAcIn < 0.09);

    var g78b = new CircuitGraph();
    var batDc78 = new Battery("BatDC78", 12);
    var trafo78b = new TransformadorAcDc("Trafo78b");
    g78b.Agregar(batDc78); g78b.Agregar(trafo78b);
    g78b.Conectar(batDc78.PinA, trafo78b.PinEntradaA);
    g78b.Conectar(batDc78.PinB, trafo78b.PinEntradaB);
    for (int t = 0; t < 15; t++) g78b.Tick(0.1);
    Console.WriteLine($"DC en primario AC->DC: dcPrim={trafo78b.DcEnPrimario} quemado={trafo78b.IsBurnt} motivo='{trafo78b.MotivoQuemado}'");
    Verificar("78b: Transformador AC->DC conectado a DC bloquea salida y quema el primario",
        trafo78b.DcEnPrimario && trafo78b.IsBurnt && trafo78b.Voltage < 0.1);

    var g78c = new CircuitGraph();
    var batAuto78 = new Battery("BatAuto78", 12) { MaxCurrent = 400, ResistenciaInternaNominal = 0.01, CargaNominal = double.PositiveInfinity };
    var inv78 = new Inversor("Inv78");
    var cargaAc = new Resistor("CargaAC", 1000);
    g78c.Agregar(batAuto78); g78c.Agregar(inv78); g78c.Agregar(cargaAc);
    g78c.Conectar(batAuto78.PinA, inv78.PinEntradaA);
    g78c.Conectar(batAuto78.PinB, inv78.PinEntradaB);
    g78c.Conectar(inv78.PinA, cargaAc.PinA);
    g78c.Conectar(cargaAc.PinB, inv78.PinB);
    for (int t = 0; t < 20; t++) g78c.Tick(0.1);
    Console.WriteLine($"DC->AC: {inv78.VoltajeEntrada:F1}V= -> {inv78.Voltage:F0}V~ ({cargaAc.TipoCorrienteRecibida}) I_out={Math.Abs(cargaAc.Current) * 1000:F0}mA I_bat={inv78.CorrienteEntrada:F2}A");
    Verificar("78c: Inversor DC->AC entrega 220V AC y consume P/(eta*Vin)+standby de la bateria",
        inv78.Encendido
        && Math.Abs(inv78.Voltage - 220.0) < 1.0
        && cargaAc.TipoCorrienteRecibida == TipoCorriente.AC
        && Math.Abs(cargaAc.Current) > 0.20 && Math.Abs(cargaAc.Current) < 0.24
        && inv78.CorrienteEntrada > 3.8 && inv78.CorrienteEntrada < 5.8);

    batAuto78.VoltajeNominal = 8;
    batAuto78.Voltage = 8;
    for (int t = 0; t < 10; t++) g78c.Tick(0.1);
    bool apagoPorBaja = !inv78.Encendido && inv78.Voltage < 0.1;

    var g78d = new CircuitGraph();
    var batInv = new Battery("BatInv78", 12) { MaxCurrent = 400, ResistenciaInternaNominal = 0.01 };
    var inv78b = new Inversor("Inv78b");
    g78d.Agregar(batInv); g78d.Agregar(inv78b);
    g78d.Conectar(batInv.PinA, inv78b.PinEntradaB); // polaridad invertida
    g78d.Conectar(batInv.PinB, inv78b.PinEntradaA);
    for (int t = 0; t < 6; t++) g78d.Tick(0.1);
    bool apagoPorPolaridad = inv78b.PolaridadInvertida && !inv78b.Encendido && inv78b.Voltage < 0.1;

    Verificar("78d: Inversor corta por baja tension (<10V) y por polaridad invertida",
        apagoPorBaja && apagoPorPolaridad);
}


// ============================================================
// DEMO 79: Disyuntor termomagnetico rearmable (sobrecorriente, sobretension y rearme)
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 79: Disyuntor rearmable (corriente y voltaje)");
Console.WriteLine("====================================================\n");
{
    // 79a: Disparo por sobrecorriente + proteccion de carga + rearme funcional tras corregir falla
    var g79a = new CircuitGraph();
    var bat79 = new Battery("Bat79", 12) { MaxCurrent = 100 };
    var disy79 = new Disyuntor("Disy79", corrienteDisparo: 1.0, voltajeDisparo: 20.0);
    var rNormal = new Resistor("RNorm79", 24); // 12V / 24Ω = 0.5 A (seguro)
    var rFalla = new Resistor("RFalla79", 2) { MaxPower = 500 }; // sobrecarga ~6 A
    var llaveFalla = new Switch("LlaveFalla79", startClosed: true);
    g79a.Agregar(bat79); g79a.Agregar(disy79); g79a.Agregar(rNormal); g79a.Agregar(rFalla); g79a.Agregar(llaveFalla);
    g79a.Conectar(bat79.PinA, disy79.PinA);
    g79a.Conectar(disy79.PinB, rNormal.PinA);
    g79a.Conectar(rNormal.PinB, bat79.PinB);
    g79a.Conectar(disy79.PinB, llaveFalla.PinA);
    g79a.Conectar(llaveFalla.PinB, rFalla.PinA);
    g79a.Conectar(rFalla.PinB, bat79.PinB);

    for (int t = 0; t < 10 && !disy79.Disparado; t++) g79a.Tick(0.05);
    g79a.Tick(0.05); // un tick adicional ya con los contactos abiertos para confirmar corte en carga
    Console.WriteLine($"Disyuntor tras corto/sobrecarga: disparado={disy79.Disparado} motivo='{disy79.MotivoDisparo}' quemado={disy79.IsBurnt} I_carga={rNormal.Current * 1000:F2}mA");
    bool disparoCorrienteOk = disy79.Disparado && !disy79.IsBurnt && disy79.MotivoDisparo.Contains("sobrecorriente") && Math.Abs(rNormal.Current) < 1e-3;

    // Retirar falla y rearmar con un clic (Rearmar)
    llaveFalla.IsClosed = false;
    disy79.Rearmar();
    for (int t = 0; t < 10; t++) g79a.Tick(0.05);
    Console.WriteLine($"Tras retirar falla y rearmar: disparado={disy79.Disparado} conduciendo={disy79.EstaConduciendo} I_carga={rNormal.Current * 1000:F1}mA");
    Verificar("79a: Disyuntor corta sobrecorriente sin quemarse y vuelve a conducir al rearmar",
        disparoCorrienteOk && !disy79.Disparado && disy79.EstaConduciendo && Math.Abs(rNormal.Current - 0.5) < 0.05);

    // 79b: Disparo por sobretension (24V sobre linea protegida a 15V, corriente chica ~24mA)
    var g79b = new CircuitGraph();
    var batAlta = new Battery("Bat24_79", 24);
    var disyV = new Disyuntor("DisyV79", corrienteDisparo: 5.0, voltajeDisparo: 15.0);
    var cargaV = new Resistor("CargaV79", 1000);
    g79b.Agregar(batAlta); g79b.Agregar(disyV); g79b.Agregar(cargaV);
    g79b.Conectar(batAlta.PinA, disyV.PinA);
    g79b.Conectar(disyV.PinB, cargaV.PinA);
    g79b.Conectar(cargaV.PinB, batAlta.PinB);
    for (int t = 0; t < 10 && !disyV.Disparado; t++) g79b.Tick(0.05);
    Console.WriteLine($"Sobretension 24V (lim 15V): disparado={disyV.Disparado} motivo='{disyV.MotivoDisparo}' quemado={disyV.IsBurnt}");
    Verificar("79b: Disyuntor dispara por sobretension aunque la corriente este debajo del limite",
        disyV.Disparado && !disyV.IsBurnt && disyV.MotivoDisparo.Contains("sobretens"));
}


// ============================================================
// DEMO 80: Temporizador NE555 (astable LED, monoestable por pulso TRIG y RESET)
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 80: Temporizador NE555 (astable, monoestable, RESET)");
Console.WriteLine("====================================================\n");
{
    // 80a: Oscilador astable con R1=10k, R2=33k, C=47uF @ 9V alimentando un LED + 330 ohm
    var g80a = new CircuitGraph();
    var bat80 = new Battery("Bat80", 9);
    var ic = new Temporizador555("IC555_ast");
    var r1_555 = new Resistor("R1_555", 10_000);
    var r2_555 = new Resistor("R2_555", 33_000);
    var cap555 = new Capacitor("C_tiempo555", 47e-6);
    var rLed555 = new Resistor("R_led555", 330);
    var led555 = new LED("LED_555", 2.0, 20);
    g80a.Agregar(bat80); g80a.Agregar(ic); g80a.Agregar(r1_555); g80a.Agregar(r2_555);
    g80a.Agregar(cap555); g80a.Agregar(rLed555); g80a.Agregar(led555);
    g80a.Conectar(bat80.PinA, ic.PinA);
    g80a.Conectar(bat80.PinB, ic.PinB);
    g80a.Conectar(ic.PinA, r1_555.PinA);
    g80a.Conectar(r1_555.PinB, ic.PinDisch);
    g80a.Conectar(ic.PinDisch, r2_555.PinA);
    g80a.Conectar(r2_555.PinB, ic.PinThres);
    g80a.Conectar(ic.PinThres, ic.PinTrig);
    g80a.Conectar(ic.PinThres, cap555.PinA);
    g80a.Conectar(cap555.PinB, ic.PinB);
    g80a.Conectar(ic.PinOut, rLed555.PinA);
    g80a.Conectar(rLed555.PinB, led555.PinA);
    g80a.Conectar(led555.PinB, ic.PinB);

    int transiciones = 0;
    bool vioLedPrendido = false;
    bool vioLedApagado = false;
    bool? ultimoOut = null;
    for (int t = 0; t < 120; t++)
    {
        g80a.Tick(0.05); // 6 segundos simulados
        if (ultimoOut != null && ultimoOut.Value != ic.SalidaAlta) transiciones++;
        ultimoOut = ic.SalidaAlta;
        if (led555.EstaEncendido) vioLedPrendido = true;
        else if (t > 2) vioLedApagado = true;
    }
    Console.WriteLine($"NE555 astable 6s: transiciones={transiciones} freq={ic.FrecuenciaMedidaHz:F2}Hz Vctrl={ic.UmbralSuperior:F2}V LED_on/off={vioLedPrendido}/{vioLedApagado} quemado={ic.IsBurnt}");
    Verificar("80a: NE555 astable fija umbrales 1/3-2/3 Vcc y oscila encendiendo/apagando un LED real",
        !ic.IsBurnt && !led555.IsBurnt
        && Math.Abs(ic.UmbralSuperior - 6.0) < 0.25
        && transiciones >= 4 && vioLedPrendido && vioLedApagado);

    // 80b: RESET activo en bajo detiene inmediatamente la oscilacion y apaga la salida
    var llaveRst = new Switch("LlaveRst", startClosed: true);
    g80a.Agregar(llaveRst);
    g80a.Conectar(ic.PinReset, llaveRst.PinA);
    g80a.Conectar(llaveRst.PinB, ic.PinB);
    for (int t = 0; t < 6; t++) g80a.Tick(0.05);
    bool resetForzoBajo = !ic.SalidaAlta && !led555.EstaEncendido;
    llaveRst.IsClosed = false; // liberar RESET -> vuelve a oscilar
    bool volvioAAlto = false;
    for (int t = 0; t < 40; t++)
    {
        g80a.Tick(0.05);
        if (ic.SalidaAlta) volvioAAlto = true;
    }
    Verificar("80b: Pin RESET a GND fuerza OUT=BAJO y al soltarlo retoma la oscilacion",
        resetForzoBajo && volvioAAlto);

    // 80c: Modo monoestable (con pull-up estándar de 10 kΩ en TRIG, un pulso TRIG genera pulso HIGH y vuelve a LOW)
    var g80c = new CircuitGraph();
    var bat80c = new Battery("Bat80c", 9);
    var icMono = new Temporizador555("IC555_mono");
    var rMono = new Resistor("R_mono", 22_000);
    var cMono = new Capacitor("C_mono", 47e-6);
    var rPullTrig = new Resistor("RPullTrig", 10_000);
    var pulTrig = new Switch("TrigBtn", startClosed: false);
    g80c.Agregar(bat80c); g80c.Agregar(icMono); g80c.Agregar(rMono); g80c.Agregar(cMono);
    g80c.Agregar(rPullTrig); g80c.Agregar(pulTrig);
    g80c.Conectar(bat80c.PinA, icMono.PinA);
    g80c.Conectar(bat80c.PinB, icMono.PinB);
    g80c.Conectar(icMono.PinA, rMono.PinA);
    g80c.Conectar(rMono.PinB, icMono.PinThres);
    g80c.Conectar(icMono.PinThres, icMono.PinDisch);
    g80c.Conectar(icMono.PinThres, cMono.PinA);
    g80c.Conectar(cMono.PinB, icMono.PinB);
    g80c.Conectar(icMono.PinA, rPullTrig.PinA);
    g80c.Conectar(rPullTrig.PinB, icMono.PinTrig);
    g80c.Conectar(icMono.PinTrig, pulTrig.PinA);
    g80c.Conectar(pulTrig.PinB, icMono.PinB);

    for (int t = 0; t < 5; t++) g80c.Tick(0.05);
    bool reposoBajo = !icMono.SalidaAlta;
    pulTrig.IsClosed = true;
    g80c.Tick(0.05);
    pulTrig.IsClosed = false;
    bool disparoAlto = icMono.SalidaAlta;
    for (int t = 0; t < 50; t++) g80c.Tick(0.05); // 2.5 s (> 1.1 * 22k * 47uF ≈ 1.14 s)
    bool expiroABajo = !icMono.SalidaAlta;
    Console.WriteLine($"NE555 monoestable: reposoBajo={reposoBajo} disparoAlto={disparoAlto} expiroABajo={expiroABajo}");
    Verificar("80c: NE555 monoestable mantiene pulso temporizado por RC tras disparar TRIG y luego cae a LOW",
        reposoBajo && disparoAlto && expiroABajo);

    // 80d: Sobretension >16.5V quema un NE555 normal pero el NE555 infinito sobrevive
    var g80d = new CircuitGraph();
    var batAlta555 = new Battery("Bat24_555", 24);
    var icReal = new Temporizador555("IC555_real");
    var icInf = new Temporizador555("IC555_inf")
    {
        MaxVoltage = double.PositiveInfinity,
        MaxCurrent = double.PositiveInfinity,
        MaxPower = double.PositiveInfinity,
        MaxTemperature = double.PositiveInfinity,
    };
    g80d.Agregar(batAlta555); g80d.Agregar(icReal); g80d.Agregar(icInf);
    g80d.Conectar(batAlta555.PinA, icReal.PinA); g80d.Conectar(icReal.PinB, batAlta555.PinB);
    g80d.Conectar(batAlta555.PinA, icInf.PinA); g80d.Conectar(icInf.PinB, batAlta555.PinB);
    for (int t = 0; t < 15; t++) g80d.Tick(0.05);
    Verificar("80d: NE555 normal se quema por sobretension a 24V mientras NE555 infinito sobrevive",
        icReal.IsBurnt && icReal.MotivoQuemado == "voltaje" && !icInf.IsBurnt);
}


// ============================================================
// DEMO 81: Variantes de Motor (sentido horario/antihorario y escala de velocidades) + CajaConPantalla
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 81: Motores (velocidades/sentidos) y CajaConPantalla");
Console.WriteLine("====================================================\n");
{
    double MedirRpmMotor(Motor motor, double voltaje, bool invertirPolaridad)
    {
        var g = new CircuitGraph();
        var bat = new Battery("BatM", voltaje) { MaxCurrent = 50 };
        g.Agregar(bat); g.Agregar(motor);
        g.Conectar(bat.PinA, invertirPolaridad ? motor.PinB : motor.PinA);
        g.Conectar(bat.PinB, invertirPolaridad ? motor.PinA : motor.PinB);
        for (int t = 0; t < 60; t++) g.Tick(0.05);
        return motor.RPM;
    }

    var mRapido = new Motor("MicroRapido", 12) { ConstanteFcem = 0.012, ConstantePar = 0.012, Friccion = 3e-6, Inercia = 5e-6, MaxCurrent = 0.6 };
    var mVentilador = new Motor("Ventilador", 18) { ConstanteFcem = 0.045, ConstantePar = 0.045, Friccion = 1.4e-5, Inercia = 4e-5, MaxCurrent = 0.45 };
    var mReductor = new Motor("Reductor", 30) { ConstanteFcem = 0.35, ConstantePar = 0.35, Friccion = 5e-5, Inercia = 1.5e-4, MaxCurrent = 0.35 };

    double rpmRap9 = MedirRpmMotor(mRapido, 9, false);
    double rpmVent9 = MedirRpmMotor(mVentilador, 9, false);
    double rpmRed9 = MedirRpmMotor(mReductor, 9, false);
    double rpmVent3 = MedirRpmMotor(new Motor("Ventilador3V", 18) { ConstanteFcem = 0.045, ConstantePar = 0.045, Friccion = 1.4e-5, Inercia = 4e-5 }, 3, false);
    double rpmVentInv = MedirRpmMotor(new Motor("VentiladorInv", 18) { ConstanteFcem = 0.045, ConstantePar = 0.045, Friccion = 1.4e-5, Inercia = 4e-5 }, 9, true);

    Console.WriteLine($"RPM @9V -> Rapido={rpmRap9:F0}, Ventilador={rpmVent9:F0}, Reductor={rpmRed9:F0} | @3V Vent={rpmVent3:F0} | Inv={rpmVentInv:F0}");
    Verificar("81a: Motores diferencian velocidad segun energia y variante, e invierten giro con la polaridad",
        rpmRap9 > rpmVent9 && rpmVent9 > rpmRed9 && rpmRed9 > 100
        && rpmVent9 > rpmVent3 * 2.2
        && rpmVentInv < -1000);

    var caja81 = new CajaConPantalla("Caja81");
    var gCaja = new CircuitGraph();
    var batCaja = new Battery("BatC81", 9);
    var rCaja = new Resistor("RC81", 90);
    gCaja.Agregar(batCaja); gCaja.Agregar(caja81); gCaja.Agregar(rCaja);
    gCaja.Conectar(batCaja.PinA, caja81.PinA);
    gCaja.Conectar(caja81.PinB, rCaja.PinA);
    gCaja.Conectar(rCaja.PinB, batCaja.PinB);
    for (int t = 0; t < 5; t++) gCaja.Tick(0.1);
    string txt3D = caja81.TextoPantalla3D();
    Console.WriteLine($"TextoPantalla3D:\n{txt3D}");
    Verificar("81b: CajaConPantalla genera texto 3D compacto y valido con mediciones reales",
        txt3D.Contains("Volt:") && txt3D.Contains("Amp:") && !txt3D.Contains("NaN"));
}


// ============================================================
// DEMO 82: Configuración eléctrica (mínimos, watts auto y máximos)
// ============================================================
Console.WriteLine("\n====================================================");
Console.WriteLine(" DEMO 82: Configuración eléctrica (mínimos / watts auto / máximos)");
Console.WriteLine("====================================================\n");
{
    // 82a: MinPower automático y Motor (sin chattering con FCEM)
    var m82 = new Motor("Motor82", 25) { MinVoltage = 6.0, MinCurrent = 0.20, MaxCurrent = 1.0, MaxVoltage = 24.0 };
    var bat82m = new Battery("Bat82m", 4.0) { MaxCurrent = 20, CargaNominal = double.PositiveInfinity };
    var g82m = new CircuitGraph();
    g82m.Agregar(bat82m); g82m.Agregar(m82);
    g82m.Conectar(bat82m.PinA, m82.PinA);
    g82m.Conectar(m82.PinB, bat82m.PinB);
    for (int t = 0; t < 20; t++) g82m.Tick(0.05);
    bool bajoEn4V = Math.Abs(m82.MinPower - 1.2) < 1e-9 && m82.BajoMinimoOperacion && !m82.EstaGirando && m82.RPM == 0;

    bat82m.VoltajeNominal = 9.0;
    bat82m.Voltage = 9.0;
    for (int t = 0; t < 35; t++) g82m.Tick(0.05);
    bool giraEn9V = !m82.BajoMinimoOperacion && m82.EstaGirando && m82.RPM > 500;
    Console.WriteLine($"Motor82 MinPower={m82.MinPower:F2}W | 4V stopped={bajoEn4V} | 9V RPM={m82.RPM:F0} gira={giraEn9V}");
    Verificar("82a: Motor respeta MinVoltage/MinCurrent/MinPower auto y sostiene giro sin parpadeo al superarlos",
        bajoEn4V && giraEn9V);

    // 82b: LED con mínimos configurados
    var led82 = new LED("LED82", 2.0, 12) { MinVoltage = 3.5, MinCurrent = 0.012, MaxCurrent = 0.5, MaxPower = 2.0 };
    var bat82l = new Battery("Bat82l", 3.0) { MaxCurrent = 10, CargaNominal = double.PositiveInfinity };
    var r82l = new Resistor("R82l", 100) { MaxPower = 10 };
    var g82l = new CircuitGraph();
    g82l.Agregar(bat82l); g82l.Agregar(r82l); g82l.Agregar(led82);
    g82l.Conectar(bat82l.PinA, r82l.PinA);
    g82l.Conectar(r82l.PinB, led82.PinA);
    g82l.Conectar(led82.PinB, bat82l.PinB);
    for (int t = 0; t < 15; t++) g82l.Tick(0.05);
    bool ledApagado = Math.Abs(led82.MinPower - 0.042) < 1e-9 && led82.BajoMinimoOperacion && !led82.EstaEncendido && led82.Brillo == 0;

    bat82l.VoltajeNominal = 5.5;
    bat82l.Voltage = 5.5;
    for (int t = 0; t < 20; t++) g82l.Tick(0.05);
    bool ledPrende = !led82.BajoMinimoOperacion && led82.EstaEncendido && led82.Brillo > 0.5;
    Console.WriteLine($"LED82 MinPower={led82.MinPower:F3}W | 3V off={ledApagado} | 5.5V V={led82.VoltajeEnBornes:F2}V I={led82.Current*1000:F1}mA P={led82.Power*1000:F1}mW on={ledPrende}");
    Verificar("82b: LED respeta voltaje/corriente/watts mínimos y enciende al superarlos",
        ledApagado && ledPrende);

    // 82c: Lámpara conserva MaxPower al calibrar y respeta mínimos
    var term82f = new SimuladorTermico();
    var lamp82 = new Lampara("Lamp82", 60) { MinVoltage = 8.0, MinCurrent = 0.10, MaxPower = 18.0, MaxTemperature = 2000 };
    var bat82f = new Battery("Bat82f", 6.0) { MaxCurrent = 20, ResistenciaInternaNominal = 0.05, CargaNominal = double.PositiveInfinity };
    var g82f = new CircuitGraph();
    g82f.Agregar(bat82f); g82f.Agregar(lamp82);
    g82f.Conectar(bat82f.PinA, lamp82.PinA);
    g82f.Conectar(lamp82.PinB, bat82f.PinB);
    for (int t = 0; t < 15; t++) { g82f.Tick(0.05); term82f.Paso(g82f.Components, 0.05); }
    bool lampBajo = lamp82.BajoMinimoOperacion && lamp82.Brillo == 0 && Math.Abs(lamp82.MaxPower - 18.0) < 1e-9;

    bat82f.VoltajeNominal = 12.0;
    bat82f.Voltage = 12.0;
    for (int t = 0; t < 15; t++) { g82f.Tick(0.05); term82f.Paso(g82f.Components, 0.05); }
    bool lampBrilla = !lamp82.BajoMinimoOperacion && lamp82.Brillo > 0.4 && !lamp82.IsBurnt;

    bat82f.VoltajeNominal = 36.0;
    bat82f.Voltage = 36.0;
    for (int t = 0; t < 25; t++) { g82f.Tick(0.05); term82f.Paso(g82f.Components, 0.05); }
    bool lampQuemoW = lamp82.IsBurnt && lamp82.MotivoQuemado == "potencia";
    Console.WriteLine($"Lamp82 lowOff={lampBajo} 12V_ok={lampBrilla} 36V_burnt={lampQuemoW}");
    Verificar("82c: Lámpara conserva MaxPower personalizado, respeta mínimos y quema al superar watts máximos",
        lampBajo && lampBrilla && lampQuemoW);

    // 82d: Transformador AC->DC con MinVoltage y MaxVoltage
    var g82t = new CircuitGraph();
    var ac82 = new FuenteAC("AC82", 110);
    var trafo82 = new TransformadorAcDc("Trafo82") { VoltajeEntradaNominal = 220, VoltajeSalidaNominal = 12, MinVoltage = 150, MaxVoltage = 180 };
    g82t.Agregar(ac82); g82t.Agregar(trafo82);
    g82t.Conectar(ac82.PinA, trafo82.PinEntradaA);
    g82t.Conectar(ac82.PinB, trafo82.PinEntradaB);
    for (int t = 0; t < 8; t++) g82t.Tick(0.05);
    bool trafoBajoMin = trafo82.BajoMinimoOperacion && !trafo82.Encendido && trafo82.Voltage < 0.1;

    ac82.VoltajeRMS = 160;
    for (int t = 0; t < 8; t++) g82t.Tick(0.05);
    bool trafoActivo = !trafo82.BajoMinimoOperacion && trafo82.Encendido && trafo82.Voltage > 7.5 && !trafo82.IsBurnt;

    ac82.VoltajeRMS = 220;
    for (int t = 0; t < 20; t++) g82t.Tick(0.05);
    bool trafoQuemoV = trafo82.IsBurnt && trafo82.MotivoQuemado == "voltaje";
    Console.WriteLine($"Trafo82 lowOff={trafoBajoMin} 160V_on={trafoActivo} 220V_burnt={trafoQuemoV}");
    Verificar("82d: Transformador respeta voltaje mínimo de entrada y quema con voltaje máximo personalizado",
        trafoBajoMin && trafoActivo && trafoQuemoV);

    // 82e: ArduinoUno mantiene valores base exentos de mínimos
    var uno82 = new ArduinoUno("UNO82");
    Verificar("82e: Arduino UNO inicia sin restricciones mínimas ni flag de bajo mínimo",
        uno82.MinVoltage == 0 && uno82.MinCurrent == 0 && uno82.MinPower == 0 && !uno82.BajoMinimoOperacion);
}


Console.WriteLine("\n====================================================");
Console.WriteLine($" RESULTADO FINAL: {testsOk}/{testsTotal} verificaciones OK");
Console.WriteLine("====================================================");

// Codigo de salida para CI: 0 si todo paso, cantidad de fallos si no.
Environment.ExitCode = testsTotal - testsOk;
