#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: assemble-linux.sh <version> <publish-dir> [output-dir]}"
PUBLISH_DIR="${2:?Usage: assemble-linux.sh <version> <publish-dir> [output-dir]}"
OUT_DIR="${3:-artifacts}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STAGE="$OUT_DIR/TS-DJ-${VERSION}-linux-x64"
ZIP="$OUT_DIR/TS-DJ-${VERSION}-linux-x64.zip"

rm -rf "$STAGE" "$ZIP"
mkdir -p "$STAGE" "$OUT_DIR"

cp -a "$PUBLISH_DIR"/. "$STAGE/"

cat > "$STAGE/ts-dj" <<'EOF'
#!/usr/bin/env sh
ROOT="$(cd "$(dirname "$0")" && pwd)"
exec "$ROOT/TS-DJ.App" "$@"
EOF
chmod +x "$STAGE/ts-dj"
chmod +x "$STAGE/TS-DJ.App" 2>/dev/null || true

mkdir -p "$STAGE/icons"
for size in 32 48 128 256; do
  cp "$ROOT/packaging/linux/icons/ts-dj-${size}.png" "$STAGE/icons/ts-dj-${size}.png"
done

cat > "$STAGE/ts-dj.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TS-DJ
GenericName=TeamSpeak DJ Client
Comment=Stream music into a TeamSpeak channel
Exec=./ts-dj
Path=
Icon=ts-dj
Terminal=false
Categories=AudioVideo;
StartupNotify=true
EOF

cat > "$STAGE/INSTALL-linux.txt" <<'EOF'
TS-DJ — Linux x64

Requirements:
  - .NET 8 runtime (dotnet --list-runtimes)
  - libopus0 (e.g. sudo apt install libopus0)

Run from this folder:
  ./ts-dj

Menu integration (optional):
  1. Copy ts-dj.desktop to ~/.local/share/applications/
  2. Edit Exec= and Path= to the absolute path of this folder
     Example:
       Exec=/home/you/TS-DJ-0.2.0-linux-x64/ts-dj
       Path=/home/you/TS-DJ-0.2.0-linux-x64
  3. Copy icons/ts-dj-*.png into ~/.local/share/icons/hicolor/SIZExSIZE/apps/ts-dj.png
  4. Run: update-desktop-database ~/.local/share/applications

From source tree, packaging/linux/install-desktop.sh performs a full user-local install.
EOF

(
  cd "$OUT_DIR"
  zip -r "$(basename "$ZIP")" "$(basename "$STAGE")"
)

echo "Created $ZIP"
