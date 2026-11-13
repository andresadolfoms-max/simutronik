# Alcance congelado

Objetivos que el proyecto **decide no perseguir**, con el motivo tecnico y
que haria falta para desbloquearlos. No es una lista de pendientes: es una
lista de puertas cerradas a proposito, para que ninguna sesion futura las
empiece a medias y deje el proyecto peor.

Cerrado en la Parte 11 del plan tecnico. Cada item dice: que falta, por que no
se puede AHORA, y que habria que construir primero.

## Indice

| Item | Que se congela | Que lo destrabaria |
|---|---|---|
| **B6a** | Las formulas de reactancia que nadie llamaba | Solver de fasores |
| **A8/7-3** | El motor DC "vibrando y cocinandose" en alterna | Solver de fasores (B6) |
| **P5-3b** | Que el diodo volante clave el pico del inductor | Separar resolver de comprometer estado |
| **B2** | Zona activa / hFE de los transistores | Solver con ecuaciones acopladas (MNA) |
| **B6** | La capa de alterna con onda de verdad | Solver de fasores o de ondas |
| **B9** | Fuentes de distinto voltaje en paralelo | MNA (matriz nodal) |
| **Arduino** | Todo el subsistema | Es una decision de producto, no tecnica |

Hay tres cuellos de botella y no siete: **el solver de fasores** (B6, A8),
**MNA** (B2, B9) y **separar resolver de comprometer estado** (P5-3b). Cada
uno destraba varios items de una.

---

## B6a — Formulas de corriente alterna sin solver de fasores

**Congelado en:** Parte 1 - Paso 5.

`Core/FormulasCorrienteAlterna.cs` esta en `Deprecated/Core/`. El archivo
existia, tenia su test (DEMO 4, ahora en `Deprecated/Tests/`), y **ningun
componente ni el solver lo llamaba nunca**. Reactancia, impedancia, angulo de
fase y factor de potencia estaban implementados y no se usaban.

**Por que se congela:** la AC del simulador se resuelve por valor RMS, sin
onda. Es una decision de diseno deliberada, documentada en el README: para
circuitos resistivos (lamparas, resistencias, cables) el RMS da exactamente
los mismos numeros de potencia que la onda real. Tener las formulas de
reactancia colgadas al costado hace creer que la capa AC hace algo que no
hace.

**Que haria falta para desbloquearlo:** un solver de fasores en
`CircuitGraph` — numeros complejos en toda la relajacion, corrientes y
voltajes con modulo y fase, y una nocion de frecuencia comun a la red. Es un
cambio de fondo en el motor, no un componente nuevo. El dia que se haga, las
formulas archivadas se usan tal cual estan: no son trabajo perdido.

**Como revivirlo:** devolver `Deprecated/Core/FormulasCorrienteAlterna.cs` a
`Core/` y volver a pegar el bloque de `Deprecated/Tests/DEMO4_formulas_ac.txt`
en `Tests/Program.cs`.

---

## A8/7-3 — El motor de continua alimentado con alterna

**Congelado en:** Parte 7 - Paso 3.

El motor DC queda **INERTE** en alterna: no gira, no consume, no se calienta.
Es lo que el código ya hacía; lo que cambia es que ahora lo DICE.

**Qué prometía antes.** El comentario de `Motor.cs:41-43` (hoy en
`Deprecated/Core/Motor.cs`) decía:

> *"con alterna el campo del imán le invierte el par 50 veces por segundo —
> vibra y se cocina, NO gira"*

El código no lo cocinaba: lo desconectaba. Un comentario que promete física
que no existe es peor que no tener comentario, porque el que lee el archivo
para entender el modelo se lleva una idea equivocada.

**Por qué se congela.** Modelar el "vibra y se cocina" bien pide par
INSTANTÁNEO siguiendo la onda: en cada semiciclo el par cambia de signo, el
rotor no llega a arrancar y toda la energía se va en calor de bobinado. Eso
necesita una onda de verdad, no un valor RMS — o sea el solver de fasores del
ítem B6, que también está congelado. Hacer una aproximación ("le meto un
calor proporcional a V²/R y listo") daría un número inventado con cara de
medición.

**Qué haría falta para desbloquearlo:** el solver de ondas del B6. Con la
onda instantánea disponible, el motor ya tiene todo lo demás: par = k_t·I sale
solo, y el promedio del par sobre un ciclo da cero mientras I²R sigue
calentando. Son diez líneas EL DÍA que exista la onda.

**Mientras tanto:** la descripción del catálogo dice *"Con alterna NO
funciona: queda inerte"*, sin prometer que se cocina.

---

## P5-3b — El diodo volante no clava el pico del inductor

**Congelado en:** Parte 8 (decidido al cerrar la Parte 7).

Con el inductor nuevo, abrir la llave produce el pico inverso — pero poner un
diodo volante en antiparalelo **no lo clava**:

```
  SIN diodo volante: picos =  21.07V   0.01V   0.00V   0.00V
  CON diodo volante: picos =  21.06V   0.00V   0.01V   0.00V
```

**Por qué.** El diodo arranca en 1 MΩ y necesita UN TICK para entrar en
conducción: la siembra del Paso 2-2 decide la rama en `ActualizarEstado`, que
corre al final del tick, así que la resistencia sembrada recién aplica al tick
siguiente. Pero el transitorio del inductor dura exactamente un tick. Para
cuando el diodo despierta, el campo ya se descargó contra el 1 MΩ de la llave
abierta.

**Qué haría falta.** Una SEGUNDA PASADA del solver dentro del mismo tick
cuando algún componente cambia de rama — que es como los simuladores de verdad
resuelven los dispositivos lineales por tramos (iteración de Newton).

**Por qué no se hace acá.** No alcanza con volver a llamar al solver: hoy
`PostSolve` resuelve Y COMPROMETE estado en la misma pasada (avanza
`CorrienteAnterior` del inductor, `VoltajeAlmacenado` del capacitor, los
contadores de quemado, la temperatura). Volver a correrlo integraría todo dos
veces. Hace falta separar la fase "resolver" de la fase "comprometer estado"
en TODO el modelo de componentes — un cambio arquitectónico que merece su
propia Parte, no un rincón de otra.

**Consecuencia mientras tanto:** el pico inverso se ve (esa mitad de la
lección funciona), pero la lección del diodo volante / la protección del relé
NO se puede enseñar. Vale la pena hacerlo: destrabaría también cualquier
componente con umbral que tenga que reaccionar dentro del mismo tick.

---

## B2 — Transistores sin zona activa (no amplifican, sólo conmutan)

**Congelado en:** Parte 11.

`TransistorNPN` y `TransistorPNP` funcionan como LLAVE: la corriente de base
decide si el canal colector-emisor conduce o no, y eso está bien hecho. Lo que
NO existe es la zona activa: no hay hFE, la corriente de colector no es
`hFE · I_base`, y no se puede armar un amplificador.

**Por qué se congela.** No es un componente que le falte un número: es un
modelo distinto. Un transistor en zona activa es una fuente de corriente
controlada por corriente, con DOS ecuaciones acopladas sobre TRES bornes
(`I_C = hFE·I_B` y `V_BE ≈ 0.7` simultáneamente). El modelo compañero de este
solver resuelve una rama por vez; acoplar dos ramas de un mismo componente
pide que el solver sepa resolver un sistema, no una relajación rama a rama.
A medio hacer —por ejemplo, forzando `I_C` sin que el resto de la red se
entere— queda peor que ahora: números que parecen amplificación y no cierran
por Kirchhoff.

**El componente NO se toca ni se saca de la paleta.** Como llave anda, y
conmutar es el 80% de para qué un alumno pone un transistor. Lo que se congela
es el OBJETIVO de la amplificación, no el componente.

**Qué haría falta:** un solver capaz de resolver componentes con más de una
ecuación acoplada. Es el mismo cambio de fondo que pide MNA (ver B9).

---

## B6 — La capa de alterna es RMS, no onda

**Congelado en:** Parte 11. (El archivado de `FormulasCorrienteAlterna.cs` fue
el Paso 1-5, arriba.)

La alterna del simulador se resuelve por valor RMS. Para circuitos resistivos
—lámparas, resistencias, cables, que es la enorme mayoría de lo que se arma—
da exactamente los mismos números de potencia que la onda real, y por eso la
decisión es defendible. Pero tiene consecuencias que conviene tener escritas,
porque desde afuera parecen bugs:

- **Un capacitor en alterna deja la rama muerta.** Medido: 0.012 mA donde
  deberían circular ~200 (50 mF con Xc = 0.06Ω sobre 12 V AC). Sin onda no hay
  reactancia: el capacitor se carga al pico y ahí queda.
- **El inductor no tiene reactancia.** Su modelo (Parte 5) es correcto en el
  tiempo, pero en alterna RMS no hay nada que lo haga oponerse a la frecuencia.
- **El diodo no rectifica.** No hay semiciclos que recortar.
- **La stat `Frecuencia` de la FuenteAC no hace ABSOLUTAMENTE NADA.** Es
  informativa. Está declarado en el README, pero vale repetirlo acá: es el
  ítem más fácil de confundir con un bug.

**Qué haría falta:** un solver de fasores (números complejos en toda la
relajación, corrientes y voltajes con módulo y fase, una noción de frecuencia
común a la red) o directamente uno de ondas con paso fino. Es otro proyecto,
no una parte de éste.

---

## B9 — Fuentes de distinto voltaje en paralelo

**Congelado en:** Parte 11.

Poner una batería de 9 V en paralelo con una de 3 V da números que no son los
reales. Medido hoy: `b9 = 5290 mA, b3 = 50 mA`, cuando la corriente
circulante de verdad sería de unos **619 mA** (la de 9 V empujando a la de 3 V
a través de las dos resistencias internas).

Nota: este número **cambió con la Parte 6**. Antes de que las pilas tuvieran
descarga real, daba `b9 = 41 mA, b3 = 0 mA`. Ahora la de 9 V reporta su
corriente de cortocircuito, porque al quedar los dos pares de bornes en el
mismo grupo fusionado el solver la clasifica como en corto. Los dos resultados
están mal; simplemente están mal de manera distinta.

**Por qué se congela.** La relajación resuelve nodo por nodo empujando hacia
el equilibrio. Dos fuentes ideales de distinto voltaje unidas por sus bornes
son una CONTRADICCIÓN para ese método: el nodo no puede estar en 9 y en 3 al
mismo tiempo, y lo que decide el resultado es el orden en que se relajó, no la
física. Resolverlo bien pide **MNA** (Modified Nodal Analysis): armar la
matriz de todo el circuito y resolverla de una, con las fuentes como
incógnitas de corriente. Eso es reemplazar el corazón del motor.

**Aceptable como aproximación:** el caso es raro en un banco de trabajo
educativo y el resultado (la pila chica sufre) apunta en la dirección
correcta, aunque la magnitud esté mal.

**Qué haría falta:** MNA. Es el mismo cambio que destrabaría B2.

---

## Arduino — fuera de alcance, y no lo toca nadie

**Congelado en:** Parte 11 (ya lo estaba por decisión del proyecto).

`Core/Arduino/`, `ArduinoUno.cs`, `Sketches/` y los pines asociados están
fuera de alcance y documentados en `docs/ARDUINO.md`.

Se registra acá por una razón concreta: **ninguna de las once partes de este
plan toca un solo archivo de Arduino.** Si aparece uno en un diff, es un
accidente, no una decisión.

---

# Encontrado durante la ejecución (no estaba en la auditoría)

Ítems que salieron midiendo, no auditando. No son objetivos de este plan;
quedan anotados para que no se pierdan.

## Los tiempos de quemado no escalan con la gravedad de la sobrecarga

`ChequearQuemado` cuenta segundos por encima del límite y quema al llegar a
`SegundosParaQuemarse`, sin mirar CUÁNTO se pasó. Consecuencia medida (Parte
2): una pila botón al **152%** de su corriente sobrevive y el LED que tiene al
lado, al **1%** de su potencia, muere — porque el fusible interno del LED es
de 0.3 s y el de la pila de 0.4 s. Gana el reloj más corto, no el más
maltratado.

El Paso 10-4 le puso curva I-t al Fusible, que es el componente donde más se
nota. Generalizarlo a todos (`_segundosSobrecarga += dt · (exceso/límite)`)
sería el arreglo de fondo.

## El solver resuelve y compromete estado en la misma pasada

Ver el ítem P5-3b del diodo volante. Es la traba de fondo para cualquier
componente con umbral que tenga que reaccionar DENTRO del mismo tick, y
destrabarla es probablemente la mejora individual más valiosa que le queda
al motor.

## La lámpara con R(T) obliga a tener térmica encendida

Desde el Paso 10-3 la resistencia del filamento sale de su temperatura. Con el
sistema térmico apagado (el botón de la barra) no hay temperatura que seguir,
así que `Lampara.GetResistance()` devuelve la resistencia de TRABAJO en vez de
la fría. Es la aproximación honesta —una lámpara prendida está caliente— pero
significa que **con la térmica apagada no se ve el pico de arranque**.


---

# LEVANTADO despues del plan (auditoria de voltajes, amperajes, AC y faradios)

## B6 — La alterna con reactancias: **resuelto en regimen permanente**

`Core/SolverAlterna.cs` resuelve con **fasores** las redes de alterna que tienen
un capacitor o un inductor (matriz nodal compleja, eliminacion gaussiana). Medido
contra la solucion analitica: inductor de 1H en 220V → 0.700 A (antes **151 A**,
se comportaba como un corto); capacitor de 10µF → 0.691 A (antes 0 A); RC, RL y
RLC serie y paralelo exactos; resonancia; la frecuencia de la fuente cambia los
numeros.

**Lo que sigue congelado dentro de B6** (y por que):
- **Transitorios de alterna** (el instante de enchufar): el solver es de regimen
  permanente. Pide integrar la onda en el tiempo.
- **Un diodo suelto rectificando**: no hay semiciclos. El *Puente rectificador*
  sigue funcionando porque esta modelado como bloque.
- **Mas de una frecuencia en la misma red**: se usa la de la primera fuente.
- **Redes de alterna con reactancias que ademas tengan un convertidor o un
  semiconductor compatible con alterna** (ej. el primario de un trafo en serie
  con un capacitor): la red se deja con el solver RMS de siempre.
- **Cables fusionados de una rama reactiva**: muestran la corriente del solver
  RMS (no afecta el calculo, solo lo que se ve en el cable).

El motor de continua en alterna **sigue inerte** (A8): el par instantaneo con la
onda no se puede sacar de un fasor.
