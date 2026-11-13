# Instalador y Distribución de Simutronik (Windows 64 bits)

Esta carpeta contiene todo lo necesario para **generar el instalador oficial de Windows (`Instalar_Simutronik.exe`)** y distribuir Simutronik en cualquier PC con Windows 10 u 11 (64 bits), sin exigir que el usuario final instale Godot ni .NET.

## Descargar el instalador ya compilado

Los archivos binarios listos para instalar (`Instalar_Simutronik.exe` y `Simutronik_Portable_win64.zip`) superan el límite por archivo de Git (100 MB), por lo que se publican en la pestaña **Releases** del repositorio en GitHub:

1. Entrá a [**Releases**](https://github.com/andresadolfoms-max/simutronik/releases) en este repositorio.
2. Descargá `Instalar_Simutronik.exe` (asistente de instalación en español, recomendado) **o** `Simutronik_Portable_win64.zip` (carpeta autocontenida sin instalación).
3. Ver instrucciones detalladas en [`LEEME_INSTALACION.txt`](LEEME_INSTALACION.txt) — incluido también dentro de los paquetes distribuibles.

## Cómo compilar el instalador desde el código fuente

Requisitos:
- **Godot 4.7 (.NET)** con *Export Templates* de Windows x64 instalados.
- **Inno Setup 6** (`ISCC.exe`).
- **(Opcional)** `ffmpeg.exe` en `ffmpeg/ffmpeg.exe` para que el instalador y la versión portable incluyan el grabador de videos MP4.

Pasos resumidos:
1. Exportar el proyecto desde Godot (`export_presets.cfg`, preset `Windows Desktop`) hacia `c:\Dev\Instalador_Simutronik\Simutronik_Portable\Simutronik.exe`.
2. Copiar `Proyectos\`, `Sketches\`, `ffmpeg.exe` (si está disponible), `LICENSE.md`, `installer\LICENCIA_USO.txt` y `docs\Terminos_y_Licencia_de_Uso_de_Simutronik.pdf` dentro de `Simutronik_Portable\`.
3. Compilar `installer/Simutronik.iss` con Inno Setup (`ISCC.exe installer\Simutronik.iss`) para generar `Instalar_Simutronik.exe`.

> Sujeto a los [Términos de Uso, Licencia y Manifiesto de Simutronik](../LICENSE.md) — distribución 100 % gratuita y no comercial.
