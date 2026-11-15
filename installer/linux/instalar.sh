#!/usr/bin/env bash
# ============================================================================
#  Instalador oficial de Simutronik para Linux (x86_64)
#  Proyecto gratuito, de código abierto y estrictamente no comercial.
#  Autor original: Andrés Adolfo Moresco Skupieñ (andresadolfoms@gmail.com)
# ============================================================================

set -euo pipefail

APP_ID="simutronik"
APP_NAME="Simutronik"
APP_VERSION="1.2.0"
APP_BIN="Simutronik.x86_64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

MODO_AUTO=0
MODO_SISTEMA=0
MODO_USUARIO=0
CON_ESCRITORIO=1
CON_VIDEOS=1

uso() {
    cat <<'EOF'
Uso: ./instalar.sh [opciones]

Opciones:
  --yes, --si, --accept-license   Acepta la licencia e instala sin preguntar.
  --user                          Fuerza instalación solo para el usuario actual (~/.local).
  --system                        Instala para todo el sistema (/opt/simutronik, requiere root/sudo).
  --no-desktop                    No crea acceso directo en el Escritorio.
  --no-videos                     No copia los videos de demostración de inicio.
  -h, --help                      Muestra esta ayuda.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --yes|--si|--accept-license|-y)
            MODO_AUTO=1
            ;;
        --system)
            MODO_SISTEMA=1
            ;;
        --user)
            MODO_USUARIO=1
            ;;
        --no-desktop)
            CON_ESCRITORIO=0
            ;;
        --no-videos)
            CON_VIDEOS=0
            ;;
        -h|--help)
            uso
            exit 0
            ;;
        *)
            echo "Opción desconocida: $1" >&2
            uso >&2
            exit 1
            ;;
    esac
    shift
done

if [[ ! -f "$SCRIPT_DIR/$APP_BIN" ]]; then
    echo "Error: no se encontró $APP_BIN en $SCRIPT_DIR." >&2
    exit 1
fi

if [[ ! -d "$SCRIPT_DIR/data_ElectricSim_linuxbsd_x86_64" ]]; then
    echo "Error: falta la carpeta data_ElectricSim_linuxbsd_x86_64 junto al instalador." >&2
    exit 1
fi

if [[ "$MODO_SISTEMA" -eq 1 && "$EUID" -ne 0 ]]; then
    echo "Error: --system requiere ejecutarse con sudo (sudo ./instalar.sh --system)." >&2
    exit 1
fi

if [[ "$MODO_USUARIO" -eq 0 && "$EUID" -eq 0 ]]; then
    MODO_SISTEMA=1
fi

if [[ "$MODO_SISTEMA" -eq 1 ]]; then
    INSTALL_DIR="/opt/simutronik"
    BIN_DIR="/usr/local/bin"
    APPLICATIONS_DIR="/usr/share/applications"
    ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
    SCOPE_LABEL="todo el sistema (/opt/simutronik)"
else
    XDG_DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
    INSTALL_DIR="$HOME/.local/opt/simutronik"
    BIN_DIR="$HOME/.local/bin"
    APPLICATIONS_DIR="$XDG_DATA/applications"
    ICON_DIR="$XDG_DATA/icons/hicolor/256x256/apps"
    SCOPE_LABEL="usuario actual ($INSTALL_DIR)"
fi

echo "========================================================================"
echo "  INSTALADOR DE $APP_NAME $APP_VERSION (Linux x86_64)"
echo "========================================================================"
echo "Destino: $SCOPE_LABEL"
echo ""

if [[ "$MODO_AUTO" -eq 0 ]]; then
    if [[ -f "$SCRIPT_DIR/LICENCIA_USO.txt" ]]; then
        echo "--- Términos y Licencia de Uso de Simutronik ---"
        cat "$SCRIPT_DIR/LICENCIA_USO.txt"
        echo "-------------------------------------------------"
    fi
    if ! read -r -p "¿Aceptás los Términos y Licencia (gratuita y no comercial) y deseás instalar? [S/n]: " RESP; then
        echo ""
        echo "Instalación cancelada."
        exit 1
    fi
    RESP="${RESP:-S}"
    case "$RESP" in
        s|S|si|SI|Si|y|Y|yes|YES)
            ;;
        *)
            echo "Instalación cancelada por el usuario."
            exit 0
            ;;
    esac
fi

echo "[1/5] Creando directorios e instalando archivos en $INSTALL_DIR..."
rm -rf "$INSTALL_DIR.tmp"
mkdir -p "$INSTALL_DIR.tmp" "$BIN_DIR" "$APPLICATIONS_DIR" "$ICON_DIR"

cp "$SCRIPT_DIR/$APP_BIN" "$INSTALL_DIR.tmp/$APP_BIN"
chmod 755 "$INSTALL_DIR.tmp/$APP_BIN"

cp -a "$SCRIPT_DIR/data_ElectricSim_linuxbsd_x86_64" "$INSTALL_DIR.tmp/"

for extra_dir in Proyectos Sketches; do
    if [[ -d "$SCRIPT_DIR/$extra_dir" ]]; then
        cp -a "$SCRIPT_DIR/$extra_dir" "$INSTALL_DIR.tmp/"
    fi
done

for extra_file in LICENSE.md LICENCIA_USO.txt Terminos_y_Licencia_de_Uso_de_Simutronik.pdf LEEME_LINUX.txt logo_simutronik.png icon.svg; do
    if [[ -f "$SCRIPT_DIR/$extra_file" ]]; then
        cp "$SCRIPT_DIR/$extra_file" "$INSTALL_DIR.tmp/"
    fi
done

rm -rf "$INSTALL_DIR"
mkdir -p "$(dirname "$INSTALL_DIR")"
mv "$INSTALL_DIR.tmp" "$INSTALL_DIR"

echo "[2/5] Creando lanzador de consola ($BIN_DIR/$APP_ID)..."
cat > "$BIN_DIR/$APP_ID" <<EOF
#!/usr/bin/env bash
exec "$INSTALL_DIR/$APP_BIN" "\$@"
EOF
chmod 755 "$BIN_DIR/$APP_ID"

echo "[3/5] Instalando ícono y acceso directo en el menú de aplicaciones..."
if [[ -f "$INSTALL_DIR/logo_simutronik.png" ]]; then
    cp "$INSTALL_DIR/logo_simutronik.png" "$ICON_DIR/$APP_ID.png"
    ICON_VALUE="$ICON_DIR/$APP_ID.png"
elif [[ -f "$INSTALL_DIR/icon.svg" ]]; then
    ICON_VALUE="$INSTALL_DIR/icon.svg"
else
    ICON_VALUE="applications-science"
fi

DESKTOP_FILE="$APPLICATIONS_DIR/$APP_ID.desktop"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Simutronik
GenericName=Simulador de Circuitos 3D
Comment=Simulador interactivo de circuitos eléctricos 3D y Arduino UNO
Exec="$INSTALL_DIR/$APP_BIN" %f
Path=$INSTALL_DIR
Icon=$ICON_VALUE
Terminal=false
Categories=Education;Electronics;Science;Engineering;
Keywords=electronica;circuitos;arduino;simulador;ne555;
StartupNotify=true
EOF
chmod 755 "$DESKTOP_FILE"

if [[ "$CON_ESCRITORIO" -eq 1 && "$MODO_SISTEMA" -eq 0 ]]; then
    DESKTOP_DIR=""
    if command -v xdg-user-dir >/dev/null 2>&1; then
        DESKTOP_DIR="$(xdg-user-dir DESKTOP 2>/dev/null || true)"
    fi
    if [[ -z "$DESKTOP_DIR" ]]; then
        if [[ -d "$HOME/Escritorio" ]]; then
            DESKTOP_DIR="$HOME/Escritorio"
        elif [[ -d "$HOME/Desktop" ]]; then
            DESKTOP_DIR="$HOME/Desktop"
        fi
    fi
    if [[ -n "$DESKTOP_DIR" && -d "$DESKTOP_DIR" ]]; then
        cp "$DESKTOP_FILE" "$DESKTOP_DIR/$APP_ID.desktop"
        chmod 755 "$DESKTOP_DIR/$APP_ID.desktop"
        if command -v gio >/dev/null 2>&1; then
            gio set "$DESKTOP_DIR/$APP_ID.desktop" metadata::trusted true 2>/dev/null || true
        fi
    fi
fi

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

echo "[4/5] Configurando videos de demostración (si están incluidos)..."
if [[ "$CON_VIDEOS" -eq 1 && -d "$SCRIPT_DIR/videos_inicio" ]]; then
    if [[ "$MODO_SISTEMA" -eq 1 && -n "${SUDO_USER:-}" ]]; then
        TARGET_HOME="$(getent passwd "$SUDO_USER" | cut -d: -f6 || echo "$HOME")"
        VIDEO_DEST="$TARGET_HOME/.local/share/godot/app_userdata/ElectricSim/inicio_videos"
        mkdir -p "$VIDEO_DEST"
        for f in "$SCRIPT_DIR/videos_inicio/"*.ogv; do
            [[ -e "$f" ]] || continue
            base="$(basename "$f")"
            [[ -e "$VIDEO_DEST/$base" ]] || cp "$f" "$VIDEO_DEST/$base"
        done
        chown -R "$SUDO_USER:$SUDO_USER" "$TARGET_HOME/.local/share/godot" 2>/dev/null || true
    elif [[ "$MODO_SISTEMA" -eq 0 ]]; then
        VIDEO_DEST="${XDG_DATA_HOME:-$HOME/.local/share}/godot/app_userdata/ElectricSim/inicio_videos"
        mkdir -p "$VIDEO_DEST"
        for f in "$SCRIPT_DIR/videos_inicio/"*.ogv; do
            [[ -e "$f" ]] || continue
            base="$(basename "$f")"
            [[ -e "$VIDEO_DEST/$base" ]] || cp "$f" "$VIDEO_DEST/$base"
        done
    fi
fi

echo "[5/5] Generando script de desinstalación ($INSTALL_DIR/desinstalar.sh)..."
cat > "$INSTALL_DIR/desinstalar.sh" <<EOF
#!/usr/bin/env bash
set -euo pipefail
if [[ ! -w "$INSTALL_DIR" ]]; then
    echo "Sin permisos de escritura en $INSTALL_DIR. Ejecutá: sudo \"\$0\"" >&2
    exit 1
fi
echo "Desinstalando Simutronik..."
rm -rf "$INSTALL_DIR"
rm -f "$BIN_DIR/$APP_ID"
rm -f "$DESKTOP_FILE"
rm -f "$ICON_DIR/$APP_ID.png"
if [[ -n "\${HOME:-}" ]]; then
    rm -f "\$HOME/Desktop/$APP_ID.desktop" "\$HOME/Escritorio/$APP_ID.desktop" 2>/dev/null || true
fi
echo "Simutronik fue desinstalado correctamente."
EOF
chmod 755 "$INSTALL_DIR/desinstalar.sh"

echo ""
echo "========================================================================"
echo "  INSTALACION COMPLETADA"
echo "========================================================================"
echo "  Aplicacion:     $INSTALL_DIR/$APP_BIN"
echo "  Proyectos:      $INSTALL_DIR/Proyectos"
echo "  Sketches:       $INSTALL_DIR/Sketches"
echo "  Desinstalar:    $INSTALL_DIR/desinstalar.sh"
echo "  Ejecutar:       $APP_ID"
echo ""

if ! command -v ffmpeg >/dev/null 2>&1; then
    echo "Nota opcional (grabador de video / MP4 -> OGV):"
    echo "  'ffmpeg' no está instalado en este sistema. Podés instalarlo cuando quieras:"
    echo "    Ubuntu / Debian / Mint: sudo apt install ffmpeg"
    echo "    Fedora:                 sudo dnf install ffmpeg"
    echo "    Arch / Manjaro:         sudo pacman -S ffmpeg"
    echo ""
fi

if [[ "$MODO_SISTEMA" -eq 0 && ":$PATH:" != *":$BIN_DIR:"* ]]; then
    echo "Tip: agregá $BIN_DIR a tu PATH si querés lanzar 'simutronik' desde cualquier terminal,"
    echo "o simplemente abrí 'Simutronik' desde el menú de aplicaciones."
fi
