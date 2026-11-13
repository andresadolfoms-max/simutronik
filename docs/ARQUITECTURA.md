# Arquitectura de ElectricSim

Este documento explica cómo está organizado el proyecto, qué contrato cumple
cada capa, y las recetas concretas para extenderlo. Es la lectura recomendada
antes de meter mano.

## Las dos capas (y por qué la frontera es sagrada)

```
┌─────────────────────────────────────────────────────┐
│  Game/  (depende de Godot)                          │
│  Visual + interacción + UI. NUNCA calcula nada      │
│  eléctrico: lee el estado del motor y lo muestra.   │
├─────────────────────────────────────────────────────┤
│  Core/  (C# puro, CERO dependencia de Godot)        │
│  Toda la física: solver, componentes, calor,        │
│  rupturas. Se testea entero sin abrir el juego.     │
└─────────────────────────────────────────────────────┘
```

La regla de oro: **Core no conoce a Godot**. Ni un `using Godot;`. Por eso la
posición física de los componentes usa `System.Numerics.Vector3`, y por eso
`dotnet run --project Tests` puede verificar TODO el motor en dos segundos.
El único traductor entre los dos mundos es `Game/Simulacion.cs`.

## El flujo de un tick (el corazón de todo)

`Game/Simulacion.cs` acumula el tiempo de cada frame y dispara N ticks por
segundo (10 por defecto). Cada tick ejecuta, EN ESTE ORDEN:

1. **`CircuitGraph.Tick(dt)`** — el paso eléctrico:
   1. `FijarFuentes()`: cada fuente viva escribe el voltaje de sus bornes
      (pines fijos) y se resetea la bandera de cortocircuito.
   2. **Fusión** (Union-Find): cables e interruptores cerrados — las
      "conexiones perfectas" — fusionan sus dos pines en un solo nodo.
   3. `ResolverGruposFijos()`: los nodos con pines fijos toman su voltaje.
      Si un nodo junta DOS voltajes fijos distintos → **cortocircuito**: se
      les avisa a las fuentes involucradas.
   4. `ClasificarTiposDeCorriente()`: los bits DC/AC se propagan desde las
      fuentes por los componentes que hoy conducen. Cada componente sabe qué
      tipo de corriente recibe (y si es compatible con eso).
   5. **Relajación** (~20 iteraciones): cada nodo libre toma el promedio de
      sus vecinos ponderado por conductancia. Los componentes con resistencia
      real (LED, resistencias, capacitores, fusibles, cables resistivos) son
      las aristas.
   6. `CalcularCorrientesDeFuentes()`: Kirchhoff en el nodo positivo de cada
      fuente → corriente real entregada.
   7. `AsignarCorrientesDeFusionados()`: los componentes fusionados (cables
      perfectos, llaves cerradas) no pueden calcular su corriente con
      (Va−Vb)/R — sus dos bornes son el MISMO nodo. El grafo se las asigna
      por Kirchhoff: arma un árbol de las conexiones perfectas dentro de
      cada nodo fusionado y propaga las inyecciones de corriente externas
      de hoja a raíz. (Caso borde documentado: cables ideales redundantes
      en paralelo → reparto indeterminado, uno lleva todo y el otro 0,
      la suma siempre es la física.)
   8. `PostSolve(dt)` de cada componente: con los voltajes finales, cada uno
      calcula su corriente/potencia (los fusionados usan la corriente
      asignada en el paso 7 — y con ella también chequean sus límites, así
      un cable o llave con `MaxCurrent` editado se quema como cualquiera),
      actualiza su lógica propia (capacitor carga, LED converge su punto de
      operación, pulsador descuenta su timer) y chequea sus límites
      eléctricos de ruptura.

2. **`SimuladorTermico.Paso(componentes, dt)`** — el paso térmico:
   1. Cada componente suma °C según su `PotenciaTermica` y su capacidad.
   2. Todos tienden hacia la temperatura ambiente (enfriamiento).
   3. Los pares VECINOS (a menos de `RadioVecindad`, cacheados — solo se
      recalculan cuando algo se movió) intercambian calor por diferencia de
      temperatura.
   4. `RevisarTemperatura()`: el que superó su máximo, se quema.

3. **`TickCompletado`** (evento): la capa visual sincroniza luces, palancas,
   humo, y el inspector refresca sus lecturas.

## Contratos por clase

### Core

| Clase | Contrato |
|---|---|
| `Pin` | Un punto eléctrico: voltaje + si es fijo + polaridad (para colores). |
| `Component` | Base de TODO. Implementá `GetResistance()`. Hooks: `ActualizarEstado(dt)` (lógica propia), `Quemar()` (efectos al morir), `EsConexionPerfecta()` (si fusiona sus pines), `PotenciaTermica` (si tu calor no es potencia×fracción). La ficha (límites, térmico, compatibilidad) ya viene de fábrica. |
| `FuenteBase` | Base de fuentes: pines fijos, corriente por Kirchhoff, sufrimiento por corto. Heredá y definí `Tipo` (DC/AC). |
| `CircuitGraph` | El solver. No debería hacer falta tocarlo para agregar componentes. |
| `SimuladorTermico` | El calor. Ídem. |
| `LeyesElectricas` | Las fórmulas con nombre y semántica, para que el código se lea como el apunte. (`FormulasCorrienteAlterna` se archivó en `Deprecated/Core/`: nadie la llamaba.) |

### Game

| Clase | Contrato |
|---|---|
| `Simulacion` | El puente. Único lugar que traduce Godot ↔ Core. |
| `CatalogoComponentes` | LOS DATOS de qué se puede colocar. Acá se agregan componentes. |
| `ComponenteVisual` | Modelo 3D auto-ajustado + bornes + feedback (brillo, humo, quemado). Genérico: no debería hacer falta tocarlo salvo para feedback nuevo. |
| `CableVisual` | El tubo Bézier con comba + su Wire lógico. |
| `BorneVisual` | La esferita clickeable de un pin. |
| `ControladorConstruccion` | La máquina de estados de interacción (colocar/cablear/mover/seleccionar). |
| `CamaraOrbital`, `BarraSuperior`, `PanelInspector`, `Main` | Lo que sus nombres dicen. `Main` arma y cablea todo: leerlo es leer el mapa. |

## Receta: agregar un componente nuevo

Ejemplo: una **bocina** que suena si le llega potencia suficiente.

**1. La lógica, en `Core/Components/Bocina.cs`:**

```csharp
namespace ElectricSim.Core.Components;

public class Bocina : Component
{
    public double Resistencia { get; set; } = 8;        // como un parlante de 8Ω
    public double PotenciaMinima { get; set; } = 0.2;   // mínimo para sonar
    public bool Sonando { get; private set; }

    public Bocina(string name) : base(name)
    {
        MaxPower = 3;                    // su ficha de ruptura
        FraccionCalorDisipado = 0.6;
    }

    public override double GetResistance() => Resistencia;

    protected override void ActualizarEstado(double deltaTime)
    {
        Sonando = Power >= PotenciaMinima;   // "necesita un mínimo para funcionar"
    }

    protected override void Quemar()
    {
        base.Quemar();
        Sonando = false;
    }
}
```

**2. La entrada del catálogo, en `Game/CatalogoComponentes.cs`:**

```csharp
var bocina = new DefinicionComponente
{
    Id = "bocina",
    Nombre = "Bocina",
    Descripcion = "Suena si le llega potencia suficiente.",
    CrearLogica = () => new Bocina("Bocina"),
    CrearPrimitiva = Primitivas.Lampara,   // primitiva prestada hasta tener .glb
    AlturaObjetivo = 0.3f,
    AnclaBorneA = new Vector3(-0.3f, 0.1f, 0f),
    AnclaBorneB = new Vector3(0.3f, 0.1f, 0f),
};
bocina.Stats.Add(new StatDef("Resistencia", "Ω", 1, 100, 1,
    c => ((Bocina)c).Resistencia, (c, v) => ((Bocina)c).Resistencia = v));
bocina.Stats.AddRange(StatsLimites());
lista.Add(bocina);
```

Listo: aparece en la barra, se coloca con fantasma, tiene bornes, ficha en el
inspector, calor y ruptura. Para el feedback visual/sonoro específico
("mientras `Sonando`, reproducir audio"), se agrega un caso en
`ComponenteVisual.ActualizarDesdeLogica()`.

**3. (Recomendado) Un test en `Tests/Program.cs`** que la conecte a una
batería y verifique que suena con potencia y calla sin ella.

### Notas para las anclas de bornes

Son fracciones de la caja del modelo YA ajustado: X y Z entre −0.5 y 0.5
(desde el centro), Y entre 0 (piso) y 1 (techo). Si el borne te queda corrido,
retocás décimas sin importar la escala real del .glb. Si el modelo viene
"acostado" o "de cabeza", usá `RotacionBase` (mirá la batería 9V).

## Receta: agregar una estadística a un componente existente

Una línea en su entrada del catálogo:

```csharp
led.Stats.Add(new StatDef("Corriente de brillo máx.", "A", 0.001, 0.1, 0.001,
    c => ((LED)c).CorrienteBrilloMaximo, (c, v) => ((LED)c).CorrienteBrilloMaximo = v));
```

El inspector la muestra solo.

## Guardar / cargar proyectos (IMPLEMENTADO)

`Game/GestorGuardado.cs` + `Game/EstadoGuardado.cs`. Botones 💾 y 📂 en la
barra superior. Formato `.json`, versión 2:

```json
{
  "version": 2,
  "componentes": [
    { "tipo": "led.real", "pos": [0.5,0,0], "rot": [0,45,0],
      "stats": { "Voltaje umbral": 2.0 }, "estado": {} }
  ],
  "cables": [
    { "compA": 0, "borneA": "A", "compB": 1, "borneB": "B", "fisico": false,
      "comba": -1, "grosor": 0.016, "resistencia": 0.001,
      "limites": [Infinity, Infinity, Infinity, 150] }
  ],
  "extras": { "mundo": { "ticksPorSegundo": 10, "temperaturaAmbiente": 22 } }
}
```

Los bornes se nombran `"A"`, `"B"` y `"X0"`, `"X1"`… (los extra de los
multi-borne: los 22 GPIO del Arduino, la base del transistor). Los cables
referencian al componente por su ÍNDICE en la lista.

### Cómo AGREGAR cosas sin tocar el guardado

El guardador no conoce ningún componente: recorre el catálogo y le pide a
cada cosa que se describa. Tres niveles:

**1. Componente o stat nueva → cero trabajo.** Tipo, posición, rotación y
todas las `StatDef` se guardan y restauran solas.

**2. Estado vivo que no es una stat** (si un interruptor quedó cerrado, qué
`.hex` tiene una placa) → dos lambdas en su entrada del catálogo:

```csharp
miTipo.GuardarEstado = (c, e) => e.Poner("cerrado", ((Switch)c).IsClosed);
miTipo.CargarEstado  = (c, e) => ((Switch)c).IsClosed = e.Booleano("cerrado", true);
```

Para los CABLES es igual, pero los métodos viven en `CableVisual`
(`GuardarEstado` / `CargarEstado`): una propiedad nueva es una línea ahí.

**3. Un subsistema entero nuevo** (notas en el tablero, protoboard,
marcadores de cámara) → se registra UNA vez donde nace y aparece con su
propia sección en `extras`:

```csharp
RegistroGuardado.Registrar("misNotas",
    estado => estado.Poner("texto", _nota.Text),
    estado => _nota.Text = estado.Texto("texto"));
```

(El aporte `"mundo"` de `Main.cs` es el ejemplo vivo de esto.)

### Compatibilidad hacia adelante y hacia atrás

- Los archivos **versión 1** se abren sin cambios (el formato 2 es un
  superconjunto: mismos nombres de campo).
- Las claves que este build **no conoce** se preservan: si abrís un proyecto
  guardado por una versión más nueva, sus datos extra viajan intactos y se
  vuelven a escribir al guardar en vez de perderse (`EstadoPreservado` en los
  visuales, `extras` desconocidos en el gestor).
- Si falta un tipo de componente o una stat, se saltea con un aviso en vez de
  reventar la carga (y el hueco NO desalinea los índices de los cables).

## Diseños propuestos para el roadmap

### El "reloj" (activaciones automáticas)

El `Pulsador` ya resuelve la mitad: sabe cerrarse por un tiempo y soltarse
solo. El reloj es un componente NO eléctrico (o un panel de la UI) con reglas
del estilo "cada 2 s → presionar Pulsador 3". Implementación mínima: clase
`Reloj` en Core con una lista de `(Component objetivo, double periodo, Action
accion)` y un `Avanzar(dt)` llamado desde `Simulacion.EjecutarTick`. La UI
después le pone cara.

### Componentes compuestos

Un compuesto es un sub-circuito guardado (mismo JSON de arriba) + una lista
de qué bornes internos se exponen como bornes del "paquete". Al colocarlo, se
instancian sus componentes internos (invisibles u ocultos en una caja) y los
cables del usuario se conectan a los pines expuestos. El motor no necesita
cambios: para él son componentes comunes.

### Solver de fasores (AC con frecuencia real)

El día que la frecuencia importe (capacitores en AC, transformadores,
motores): la relajación de `CircuitGraph` pasa de `double` a
`System.Numerics.Complex` (voltajes como fasores, impedancias en vez de
resistencias — las fórmulas archivadas están en `Deprecated/Core/`). Es un
cambio de fondo pero localizado: `Pin.Voltage` complejo + `GetResistance()` →
`GetImpedancia(frecuencia)`. Los componentes puramente resistivos ni se
enteran (su impedancia es real).

## Por qué el solver es como es (para no romperlo sin querer)

La relajación iterativa promedia vecinos ponderando por conductancia. Con
resistencias parecidas converge rapidísimo; con un cable de 0.001Ω al lado de
una resistencia de 220Ω, el sistema queda tan mal condicionado que
necesitaría miles de iteraciones. La solución del proyecto fue **sacar a los
cables del sistema**: si la conexión es perfecta, no es una arista — es EL
MISMO NODO (Union-Find). Se verificó empíricamente contra el análisis nodal
exacto: error 0.000% incluso en malla de Wheatstone con cables. El fusible
(0.05Ω) sí es una arista y no molesta: el problema eran los tres órdenes de
magnitud extra de los cables. Historia completa en los comentarios de
`Core/UnionFind.cs`.

Dos reglas si tocás componentes:
- `GetResistance()` jamás devuelve 0 (dividirías por cero en la conductancia).
- Si tu componente cambia de resistencia según su propio voltaje (como el
  LED), NO la cambies dentro del mismo tick: congelá el valor y decidí el
  próximo en `ActualizarEstado`. El LED es el ejemplo de manual: su
  resistencia dinámica converge de a un paso POR TICK hacia el punto donde
  su caída es exactamente el voltaje umbral (modelo de caída constante) —
  cambiarla dentro del tick haría oscilar la relajación (el comentario en
  `Core/Components/LED.cs` cuenta la historia completa del parpadeo que
  esto arregló).

## El catálogo jerárquico (revisión "paleta + sandbox")

Desde esta revisión el catálogo tiene tres niveles:

```
CategoriaComponente ("Fuentes", "Iluminación", ...)
  └── DefinicionTipo      → lo que el componente ES: modelo 3D, anclas de
      │                     bornes, Stats (con textos educativos) y
      │                     Lecturas en vivo propias del tipo.
      └── VarianteComponente → cómo viene DE FÁBRICA: nombre, descripción
                               y la fábrica CrearLogica. "LED" y "LED ∞"
                               son dos variantes del mismo tipo.
```

`DefinicionComponente` sigue existiendo como la **selección concreta**
(tipo + variante) y reenvía los mismos miembros de siempre — por eso
ComponenteVisual, el controlador y el inspector no se enteraron del cambio.

Las `StatDef` ahora declaran su **TipoStat** (Numero, NumeroConInfinito,
Booleano, Opciones), una **Descripcion** en criollo, una **Formula**
opcional y un **HabilitadoSi** para grisarse según el estado del componente
(los Ohms manuales se apagan cuando el modo corriente objetivo maneja la
resistencia). El inspector arma el control que corresponda a cada tipo; los
NumeroConInfinito llevan el check "∞" que reemplazó al viejo "999".

**Los cuatro límites de ruptura aceptan `double.PositiveInfinity` por
separado** (el motor ya comparaba contra infinito sin problemas, y ahora hay
demos que lo vigilan). Regla especial coherente: una fuente con
`MaxCurrent = ∞` tampoco muere por cortocircuito — un corto es, en el fondo,
un evento de sobrecorriente.

## El modo corriente objetivo de la resistencia

`Resistor.ModoCorrienteObjetivo = true` + `CorrienteObjetivo` (Amperes): la
resistencia se auto-ajusta un paso POR TICK con

```
R_siguiente = V_medido / I_objetivo
```

(el mismo esquema de convergencia que el LED — jamás cambia su resistencia
DENTRO del tick). Con fuente ideal converge en un tick; con más serie, en
unos pocos, sin oscilar. Clamps en [0.1Ω, 500kΩ]; con menos de 10µA medidos
no ajusta (es la microfuga de un circuito abierto, no un circuito). La
bandera `ObjetivoAlcanzado` le dice la verdad al inspector.

## La paleta y los modos de bornes

`PanelPaleta` (izquierda) se construye sola recorriendo
`CatalogoComponentes.Tipos`: una tarjeta desplegable IDÉNTICA por tipo
(regla: un solo patrón visual, nada de niveles mezclados — la Categoria
queda en los datos para orden y usos futuros, no como encabezado). Elegir
una variante dispara el mismo `IniciarColocacion` de siempre. La barra superior quedó
solo con los controles globales.

`BorneVisual` responde a un `ModoBornes` global (Siempre / AlInteractuar /
Ocultos). En "Al interactuar", el controlador mantiene la "pista": bornes
visibles para el componente bajo el mouse, el seleccionado, y TODOS durante
un cableado (son los destinos posibles). La **colisión nunca se apaga**: un
borne oculto se puede clickear igual.
