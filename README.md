# Simutronik

Simulador electrónico 3D interactivo y educativo desarrollado en **Godot 4.7 (.NET)** y **C# (.NET 8)** para diseñar circuitos en tiempo real, experimentar con leyes eléctricas reales, programar microcontroladores **Arduino UNO (ATmega328P)** y observar comportamiento térmico, fallas físicas y protecciones sin riesgo de hardware.

---

## Instalación rápida (Windows 64 bits)

Si solo querés usar Simutronik en cualquier PC con Windows 10 u 11 (x64) **sin instalar Godot ni el SDK de .NET**:

1. Abrí la sección [**Releases**](https://github.com/andresadolfoms-max/simutronik/releases) de este repositorio.
2. Descargá una de las dos opciones:
   - **`Instalar_Simutronik.exe`** *(recomendado)*: asistente de instalación en español con accesos directos, proyectos listos, sketches Arduino y licencia incluida.
   - **`Simutronik_Portable_win64.zip`**: carpeta autocontenida; descomprimí y ejecutá `Simutronik.exe`.
3. Consultá [installer/README.md](installer/README.md) o [installer/LEEME_INSTALACION.txt](installer/LEEME_INSTALACION.txt) para ver la guía completa.

> Los ejecutables compilados (`> 100 MB`) se alojan en **GitHub Releases** para mantener limpio el código fuente del repositorio Git.

---

## Qué incluye este repositorio

- **`Core/`**: Motor físico y eléctrico puro en C# (`net8.0`, desacoplado de Godot):
  - Análisis nodal DC y fasorial AC (`CircuitGraph.cs`, `SolverAlterna.cs`), fuentes con resistencia interna y límite de corriente (`FuenteBase.cs`, `Battery.cs`, `FuenteAC.cs`), detección de cortocircuitos y simulación térmica con propagación entre vecinos (`SimuladorTermico.cs`).
  - Emulación ciclo a ciclo del microcontrolador **ATmega328P** para **Arduino UNO** (`Core/Arduino/`: `Atmega328P.cs`, `AvrTimer.cs`, `IntelHexParser.cs`).
  - Componentes en `Core/Components/`: resistencias, potenciómetros, capacitores (no polarizado y electrolítico), inductores, diodos, LEDs, lámparas incandescentes, motores DC con fuerza contraelectromotriz y giro visual, transistores NPN/PNP, temporizador **NE555**, disyuntor rearmable, fusible, interruptores, pulsadores, transformador AC→DC, inversor DC→AC, caja con pantalla y cables resistivos.
- **`Game/` y `scenes/`**: Capa visual 3D en Godot, cámara orbital, paleta de componentes, tendido de cables, pantalla de inicio con demos en video y **Panel Inspector** (`Game/UI/PanelInspector.cs`) con configuración eléctrica simplificada (voltaje/amperaje mínimo y máximo, **watts mínimos calculados automáticamente** y watts máximos antes de quemarse; exceptuando Arduino UNO).
- **`Proyectos/`**: 17 circuitos `.json` listos para cargar desde la aplicación, incluyendo casos funcionales, comparativas AC/DC, fallas destructivas, un proyecto complejo con Arduino UNO y su código fuente `.ino` + firmware `.hex` (`Proyectos/17_estacion_control_arduino/`).
- **`Sketches/`**: Colección de programas `.c` y binarios `.hex` listos para cargar en placas Arduino UNO dentro del simulador.
- **`installer/`**: Script de Inno Setup 6 (`Simutronik.iss`), licencia en texto plano (`LICENCIA_USO.txt`) y guía de empaquetado para generar `Instalar_Simutronik.exe`.
- **`docs/`**: Documentación técnica (`ARQUITECTURA.md`, `PLAN.md`) y copia del documento oficial [`Terminos_y_Licencia_de_Uso_de_Simutronik.pdf`](docs/Terminos_y_Licencia_de_Uso_de_Simutronik.pdf).
- **`Tests/`**: Suite de pruebas unitarias y de integración en consola (82 bloques de demostración/verificación).

---

## Compilar y ejecutar desde el código fuente

### Requisitos
- **SDK de .NET 8** (para compilar el motor, las pruebas y los scripts C#).
- **Godot 4.7 (.NET / Mono)** con soporte C# (para abrir o exportar la aplicación completa).
- *(Opcional)* **Inno Setup 6** para generar `Instalar_Simutronik.exe` usando `installer/Simutronik.iss`.

### Comandos útiles

Compilar la librería y el proyecto principal:
```powershell
dotnet build ElectricSim.csproj -c Release
```

Compilar y ejecutar las pruebas automáticas del motor eléctrico/Arduino:
```powershell
dotnet run --project Tests/Tests.csproj -c Release
```

Abrir en el editor gráfico:
1. Abrí **Godot 4.7 (.NET)** e importá `project.godot`.
2. Ejecutá la escena principal (`scenes/Main.tscn`) o exportá con el preset `"Windows Desktop"` definido en `export_presets.cfg`.

---

## Términos de Uso y Licencia (Código Abierto, Gratuito y No Comercial)

**Simutronik** es un proyecto gratuito, abierto a toda la comunidad para estudiar, usar, modificar y crear extensiones.  
Está sujeto estrictamente a los [**Términos de Uso, Licencia y Manifiesto de Simutronik**](LICENSE.md) (también disponible en formato PDF en [`docs/Terminos_y_Licencia_de_Uso_de_Simutronik.pdf`](docs/Terminos_y_Licencia_de_Uso_de_Simutronik.pdf)):

- **Gratuidad absoluta y prohibición de lucro:** Queda terminantemente prohibido vender copias, extensiones, versiones derivadas (*"Pro"* / pagas) o utilizar Simutronik con fines comerciales o de beneficio económico.
- **Compartir igual:** Cualquier modificación, *fork* o extensión distribuida públicamente debe mantenerse 100 % gratuita, de código abierto y con atribución al autor original.

**Autor original:** Andrés Adolfo Moresco Skupieñ — `andresadolfoms@gmail.com`  
**Repositorio oficial:** `https://github.com/andresadolfoms-max/simutronik`
