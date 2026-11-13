# Plan maestro de ElectricSim

El acuerdo de desarrollo, para que no viva solo en el chat. Cada bloque es
cimiento del siguiente (A→B→C); el D corre en paralelo cuando se quiera.

## Bloque A — Core (motor, testeable sin Godot) — ✔ HECHO
- [x] Modo **corriente objetivo** de la resistencia: "quiero 20mA" y se
      auto-ajusta sola (convergencia un-paso-por-tick, mismo truco que el
      LED). Bandera honesta de objetivo inalcanzable. → DEMOS 23 y 24.
- [x] **Límites ∞ individuales** verificados en el motor: cada límite de
      ruptura puede ser infinito por separado. Combos tipo "inmune al
      voltaje pero mortal por amperaje". → DEMO 25.
- [x] La fuente con corriente ∞ **sobrevive el cortocircuito** (un corto es
      un evento de sobrecorriente). → DEMO 25.
- [x] El inmortal **sigue calentando** a los vecinos (temperatura ∞ no
      apaga la física del calor). → DEMO 26.
- Suite: **185/185 verificaciones** (el Bloque A dejó 89; el resto lo sumaron el cable físico y todo el emulador de Arduino).

## Bloque B — Datos (catálogo) — ✔ HECHO
- [x] Catálogo **jerárquico**: Categoría → Tipo → Variante. El tipo define
      qué ES (modelo, bornes, stats); la variante cómo viene DE FÁBRICA.
- [x] Variantes **realista + sin límites (∞)** para todos los tipos (el
      fusible solo realista: un fusible que no se sacrifica es un cable).
- [x] **StatDef 2.0**: tipo de control (número / número-con-∞ / casilla /
      opciones), descripción en criollo, fórmula, condición de habilitado.
- [x] **Lecturas por tipo** (brillo, carga, estado del objetivo...) también
      declaradas en el catálogo.

## Bloque C — UI (capa Godot; escrita a ciegas, itera con F5) — ✔ CÓDIGO / ⏳ F5
- [x] **Paleta lateral** "📦 Electrónicos": acordeón UNIFORME (una tarjeta
      desplegable idéntica por tipo — v2 tras feedback: sin encabezados de
      categoría mezclados), variantes ∞ en dorado, descripciones educativas.
- [x] **Inspector 2.0**: checks ∞ (chau "999"), descripciones y fórmulas por
      stat, lecturas del catálogo, corriente "(igual en ambas patas)",
      grisado condicional (los Ohms se apagan en modo objetivo).
- [x] **Barra superior** liviana: paleta, pausa, ticks, ambiente, grilla,
      selector de modo de bornes, cartel de modo.
- [x] **Bornes con 3 modos**: Siempre / Al interactuar (hover + selección +
      cableando) / Ocultos (invisibles pero clickeables).
- [x] **Rotación libre**: R 45° · Shift+R 15° · T vuelca 90° · campos
      numéricos X/Y/Z en el inspector sincronizados con el mundo.
- [ ] Verificación en Godot (F5) — pendiente del usuario.

## Bloque D — Mundo 3D — ⏳ PENDIENTE
- [ ] Sesión de **bornes/modelos**: posiciones de terminales, rediseño
      visual de las esferas (más chicas), tamaño de la pila 3V, patas
      gigantes de los GLB (ver tabla abajo).
- [ ] **Cables**: clamp de la comba (que no se hunda bajo la mesa) + nuevo
      tipo "cable rígido" estilo puente de protoboard. (Soga con física
      real: anotada como v2 opcional.)

## Bloque E — Después — ⏳ PENDIENTE
- [ ] Colores de LED (el color ajusta el voltaje umbral: rojo ~1.8V,
      verde ~2.1V, azul ~3.2V). La infraestructura de stats "Opciones" ya
      está lista en el inspector.
- [ ] Voltímetro y amperímetro como componentes (con consecuencias reales
      si se conectan al revés).
- [ ] Etiquetas flotantes de lecturas sobre cada componente (toggle).
- [ ] Ctrl+Z (infraestructura de deshacer).
- [ ] Potenciómetro (la perilla en vivo, hermana del modo objetivo).
- [ ] Estética global (tema, colores, íconos, tipografía).
- [ ] Futuro grande: solver **MNA** (capacitor integrador real, fasores AC,
      y sobre todo fuentes que se HUNDEN bajo carga). Ojo: las fuentes en
      serie ya SUMAN voltajes sin MNA (18V con dos de 9V) — lo que falta es
      la caída bajo carga, no la suma.

## Bloque F — Lista de objetos nueva (pedido 17/07) — estado por ítem
Orden del pedido (de menos a más importante), con lo que quedó de cada uno:

1. **Dimmer ("dimme")** → ✔ FUNCIONANDO: `Potenciometro` (perilla 0-100%,
   escala logarítmica como los reales) + en catálogo con primitiva. DEMO 27.
2. **Alambre puro (sin aislante)** → ◐ PREPARADO: flag `Wire.SinAislante`
   listo en el motor. La mecánica de "conduce AL CONTACTO" (dos alambres
   pelados que se tocan quedan conectados → cortos accidentales) es física
   de colisiones de la capa Game: pendiente, el motor ya la recibe con un
   simple `Conectar()` entre puntos de contacto.
3. **Formas (círculo/cuadrado/cubo + barra de regalo)** → ✔ FUNCIONANDO:
   clase `Conductor` + tipos Aro/Marco/Cubo(+Barra) con primitivas.
4. **Transistores** → ✔ FUNCIONANDO (motor + UI): `TransistorNPN` de TRES
   bornes (canal C-E + arista interna B-E), modelo corte/saturación con
   histéresis, DEMO 30 verde. YA está en el catálogo (categoría Control),
   con sus bornes C/B/E cableables gracias al soporte multi-borne
   (IComponenteMultiBorne) que estrenó el Arduino.
5. **Baterías AA / AAA / de auto** → ✔ FUNCIONANDO: tres tipos nuevos con
   primitivas (la de auto con 400A de respeto).
6. **Conector de baterías (portapilas + broche 9V)** → ✔/◐: Portapilas
   FUNCIONANDO (suma las pilas en serie POR ADENTRO — expone una sola
   fuente al solver, más prolijo que encadenar varias por fuera). Broche 9V y
   enchufe: colocables como conductores; la mecánica de ENCASTRE
   (broche+batería, enchufe+toma) queda pendiente en Game.
7. **Tomacorriente y enchufe** → ✔/◐: Tomacorriente 220V/50Hz FUNCIONANDO
   (25A y respeto). Enchufe: ver punto 6.
8. **Focos** → ✔: variante "Foco 220V (60W)" del tipo Lámpara, pensada
   para el tomacorriente.
9. **Inductor (con 2 basta)** → ✔ FUNCIONANDO: modelo espejo del capacitor
   (bloquea al principio, conduce después; el campo colapsa al abrir). Dos
   variantes: bobina chica 0.3s y grande 2s. DEMO 29. TODO anotado: chispa
   al abrir + reactancia real con el solver de fasores.
10. **Motores** → ✔ FUNCIONANDO: `Motor` DC con inercia y SENTIDO POR
    POLARIDAD (batería al revés = gira al revés). DEMO 28. TODO: girar el
    "Eje" del modelo en la capa visual según RPM.
11. **TRANSFORMADORES (por último, como pediste)** → ○ ESQUELETO
    DOCUMENTADO: `Transformador` (el "de control" es este con relación
    reductora), `Rectificador` (AC→DC) e `Inversor` (DC→AC) compilan, se
    comportan como abierto (no rompen ni mienten) y su archivo explica
    exactamente qué falta: el acople de energía primario→secundario
    (fuente cuyo voltaje depende de otra arista, conservando potencia) y
    el cambio de TipoCorriente. La puerta de entrada (IComponenteCompuesto)
    ya quedó construida y estrenada por el transistor.

## Checklist del Paso 0 (F5 del usuario, sigue pendiente)
1. Compila y abre.
2. La paleta se ve, abre y cierra; se colocan componentes desde ella.
3. No crashea al borrar un componente mientras se cablea desde su borne.
4. WASD no mueve la cámara mientras se tipea en un SpinBox.

## Tabla GLB (para la sesión de bornes del Bloque D)
| Modelo | Hallazgo |
|---|---|
| `LED.glb` | Nodos `patalarga` (x=−0.516) y `pataccorta` (x=+0.718) coherentes con las anclas. PERO las patas miden ~93 unidades contra ~2 del cuerpo → el autoescalado deja la cabeza del LED de ~1cm. |
| `resistencia.glb` | Ídem patas enormes (AABB min −20.9/−16.7/−28.7; nodos `PATA`, `pata2`, `Cilindro`). |
| `swichlinearriba/abajo.glb` | `PATA1` (x=−0.029) y `PATA2` (x=−0.617): **las dos patas están del mismo lado**. Si se quieren terminales en extremos opuestos hay que tocar modelo Y anclas. |
| `bateria9V.glb` | Bornes abajo (y≈−1.4/−1.85, de ahí la RotacionBase π en X). `Cilindro`/`Cilindro.001` ambos z≈0.383; `Cubo.001` z=−0.614. Anclas actuales Z=+0.23/−0.22. |
| `pilaboton.glb` | Un solo `Cilindro`. Además: ajustar su tamaño en mundo (hoy 10cm de alto; probar ~25cm). |
