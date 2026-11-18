#!/usr/bin/env bash
# Builds release exports into build/: StationX-linux-x86_64.tar.gz and StationX-windows-x86_64.zip.
# Needs the Godot 4.7.2 .NET export templates in ~/.local/share/godot/export_templates/4.7.2.stable.mono
# (Editor → Manage Export Templates, or unzip the official .tpz there). Set GODOT to use a different binary.
set -euo pipefail
cd "$(dirname "$0")"
godot="${GODOT:-godot}"
tpl="$HOME/.local/share/godot/export_templates/4.7.2.stable.mono"
if [ ! -d "$tpl" ]; then
  echo "Export templates missing: $tpl" >&2
  exit 1
fi
rm -rf build/linux build/windows build/both build/*.zip build/*.tar.gz
mkdir -p build/linux build/windows && touch build/.gdignore
"$godot" --headless --path . --export-release "Linux" build/linux/StationX.x86_64
"$godot" --headless --path . --export-release "Windows" build/windows/StationX.exe
(
  cd build
  tar czf StationX-linux-x86_64.tar.gz -C linux .
  (cd windows && zip -qr ../StationX-windows-x86_64.zip .)
)
ls -lh build/*.tar.gz build/*.zip
