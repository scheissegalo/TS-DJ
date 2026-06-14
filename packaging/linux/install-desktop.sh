#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
APP_DIR="$PREFIX/share/ts-dj"
BIN_DIR="$PREFIX/bin"
DESKTOP_DIR="$PREFIX/share/applications"
ICON_DIR="$PREFIX/share/icons/hicolor"
WRAPPER="$BIN_DIR/ts-dj"

echo "Publishing TS-DJ to $APP_DIR ..."
dotnet publish "$ROOT/TS-DJ.App/TS-DJ.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained false \
  -o "$APP_DIR"

mkdir -p "$BIN_DIR" "$DESKTOP_DIR"

# Wrapper uses the real publish path so .NET finds deps next to the apphost.
# A symlink in ~/.local/bin breaks menu launches (probe dir becomes ~/.local/bin).
cat > "$WRAPPER" <<EOF
#!/usr/bin/env sh
exec "$APP_DIR/TS-DJ.App" "\$@"
EOF
chmod +x "$WRAPPER"

for size in 32 48 128 256; do
  target="$ICON_DIR/${size}x${size}/apps"
  mkdir -p "$target"
  cp "$ROOT/packaging/linux/icons/ts-dj-${size}.png" "$target/ts-dj.png"
done

# Use absolute paths — GUI sessions often omit ~/.local/bin from PATH.
cat > "$DESKTOP_DIR/ts-dj.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TS-DJ
GenericName=TeamSpeak DJ Client
Comment=Stream music into a TeamSpeak channel
Exec=$APP_DIR/TS-DJ.App
Path=$APP_DIR
Icon=ts-dj
Terminal=false
Categories=AudioVideo;
StartupNotify=true
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$DESKTOP_DIR" || true
fi

echo
echo "TS-DJ installed."
echo "  Binary:  $APP_DIR/TS-DJ.App"
echo "  CLI:     $WRAPPER"
echo "  Menu:    search for \"TS-DJ\" in your application launcher"
echo
echo "Dependencies:"
echo "  - .NET 8 runtime (dotnet --list-runtimes)"
echo "  - libopus0 (e.g. sudo apt install libopus0)"
