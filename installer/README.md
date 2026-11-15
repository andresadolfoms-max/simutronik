# Instaladores y Distribución de Simutronik (Windows y Linux x64)

Esta carpeta contiene todo lo necesario para generar los paquetes oficiales de **Windows 64 bits** (`Instalar_Simutronik.exe` + portable `.zip`) y **Linux 64 bits** (`Instalar_Simutronik_Linux_x64.run` + portable `.tar.gz`), sin exigir que el usuario final instale Godot ni .NET.

## Descargar los instaladores ya compilados

Los ejecutables compilados (`> 100 MB`) se alojan en la pestaña [**Releases**](https://github.com/andresadolfoms-max/simutronik/releases) del repositorio en GitHub:

### Windows (10 / 11 x64)
- **`Instalar_Simutronik.exe`** *(recomendado)*: asistente de instalación en español con accesos directos.
- **`Simutronik_Portable_win64.zip`**: carpeta autocontenida sin instalación.
- Guía detallada: [`LEEME_INSTALACION.txt`](LEEME_INSTALACION.txt).

### Linux (Ubuntu, Debian, Mint, Fedora, Arch, etc. — x86_64)
- **`Instalar_Simutronik_Linux_x64.run`** *(recomendado)*: instalador autoextraíble en un solo archivo (`chmod +x Instalar_Simutronik_Linux_x64.run && ./Instalar_Simutronik_Linux_x64.run`). Instala en `~/.local/opt/simutronik` sin requerir `root` (o en `/opt/simutronik` con `sudo`), agrega entrada al menú de aplicaciones, ícono, comando `simutronik` y script `desinstalar.sh`.
- **`Simutronik_Linux_x64.tar.gz`**: paquete portable + script [`linux/instalar.sh`](linux/instalar.sh). Descomprimí y ejecutá `./Simutronik.x86_64` o `./instalar.sh`.
- Guía detallada: [`linux/LEEME_LINUX.txt`](linux/LEEME_LINUX.txt).

## Cómo compilar los instaladores desde el código fuente

Requisitos:
- **Godot 4.7 (.NET)** con *Export Templates* de Windows x64 y Linux x64 instalados.
- **Inno Setup 6** (`ISCC.exe`) para generar `Instalar_Simutronik.exe`.
- *(Opcional en Windows)* `ffmpeg.exe` en `ffmpeg/ffmpeg.exe` para incluir el grabador MP4 (en Linux Simutronik detecta automáticamente `ffmpeg` en el `PATH` del sistema o junto al binario).

Pasos resumidos:
1. Exportar desde Godot usando `export_presets.cfg`:
   - Preset `Windows Desktop` → `Simutronik.exe` + compilar `installer/Simutronik.iss` con Inno Setup (`ISCC.exe installer\Simutronik.iss`).
   - Preset `Linux` → `Simutronik.x86_64` + incluir `data_ElectricSim_linuxbsd_x86_64/`, `Proyectos/`, `Sketches/`, licencias, íconos y [`linux/instalar.sh`](linux/instalar.sh) en `.tar.gz` / `.run` con permisos `0755` y fin de línea LF.

> Sujeto a los [Términos de Uso, Licencia y Manifiesto de Simutronik](../LICENSE.md) — distribución 100 % gratuita y no comercial.
