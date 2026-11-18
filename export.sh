#!/usr/bin/env bash
# Builds release exports into build/:
#   StationX-linux-x86_64.tar.gz, StationX-windows-x86_64.zip, and StationX-linux-windows.zip
#   (both platforms in one folder: they share StationX.pck, each has its own data_ folder).
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
mkdir -p build/linux build/windows build/both && touch build/.gdignore
"$godot" --headless --path . --export-release "Linux" build/linux/StationX.x86_64
"$godot" --headless --path . --export-release "Windows" build/windows/StationX.exe
cmp -s build/linux/StationX.pck build/windows/StationX.pck || { echo "Linux and Windows .pck differ; can't combine" >&2; exit 1; }
cp -a build/linux/. build/windows/. build/both/
cp README.md build/both/README.md
(
  cd build
  tar czf StationX-linux-x86_64.tar.gz -C linux .
  (cd windows && zip -qr ../StationX-windows-x86_64.zip .)
  (cd both && zip -qr ../StationX-linux-windows.zip .)
)
ls -lh build/*.tar.gz build/*.zip
