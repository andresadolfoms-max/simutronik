# Codigo archivado del Core

Nada de esta carpeta se compila: `ElectricSim.csproj` la excluye con
`<Compile Remove="Deprecated\**" />` y los proyectos `Core` y `Tests` tienen
su raiz en sus propias carpetas, asi que ni la ven.

Cada archivo esta acá por una razon distinta:

| Archivo | Por que | Reemplazo |
|---|---|---|
| `FormulasCorrienteAlterna.cs` | Existia, tenia test propio y **nadie la llamaba nunca**. Reactancia, impedancia y factor de potencia implementados y sin usar. | Ninguno: el objetivo esta congelado (item B6). |
| `FuenteBase.PropiedadesViejas.txt` | `CaidaInternaEstimada` y `VoltajeConCargaEstimado` calculaban una caida "estimada" en paralelo, y su doc afirmaba que el solver trataba la fuente como ideal — falso desde el Fix 4. | `FuenteBase.CaidaInternaReal`, que es la caida que el solver YA aplico. |
| `Inductor.cs` | Era una rampa de resistencia con un `SegundosEstablecimiento` puesto a mano: tardaba 2 s pusieras 10Ω o 10kΩ al lado, disipaba calor "guardando campo" y no daba pico inverso. | `Core/Components/Inductor.cs`, modelo companion con L de verdad (tau = L/R). |
| `Motor.cs` | Hacia `RPM = RPMmax·(P/P_nom)`: la corriente no dependia de la velocidad, no habia par de carga, y con la mitad de volts giraba a un cuarto. | `Core/Components/Motor.cs`, con fuerza contraelectromotriz. |
| `Potenciometro.cs` | Tenia DOS bornes: era un reostato, y encima duplicado con `Reostato.cs`. | `Core/Components/Potenciometro.cs`, de tres bornes (divisor de tension). `Reostato.cs` se queda: es el primo de potencia. |
| `Lampara.cs` | **Es un respaldo, no un reemplazo.** Es la version anterior al Paso 10-3, cuando la resistencia del filamento era fija. La clase sigue viva y evolucionada en `Core/Components/`. | `Core/Components/Lampara.cs`, con `R(T)` y pico de arranque. |

Los bloques de test que probaban estos componentes estan en
`Deprecated/Tests/`, cada uno con su nota. **No se pueden "actualizar"**:
median el comportamiento incorrecto. Los reemplazan los DEMO 67 a 72.
