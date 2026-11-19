#!/usr/bin/env bash
# Builds release exports into build/: StationX-linux-x86_64.tar.gz and StationX-windows-x86_64.zip, and for each a
# small update: <name>-patch.zip holds only the game's own files (StationX.pck and data_*/StationX.*), and
# <name>-base.txt lists the SHA-256 of every other file (engine, .NET runtime). The in-game updater fetches the patch
# when the player's copy has exactly those other files, and the full download otherwise.
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
rm -rf build/linux build/windows build/both build/*.zip build/*.tar.gz build/*.txt
mkdir -p build/linux build/windows && touch build/.gdignore
"$godot" --headless --path . --export-release "Linux" build/linux/StationX.x86_64
"$godot" --headless --path . --export-release "Windows" build/windows/StationX.exe
# The music stays loose beside the game (assets/music has a .gdignore, so it isn't packed): it is part of the base,
# not the patch, and an update doesn't download it again.
for d in build/linux build/windows; do mkdir -p "$d/music" && cp assets/music/*.ogg "$d/music/"; done
# Game files (the patch) vs. everything else (the base the patch needs). Paths are ./relative, sorted bytewise.
patch_files() { find . -type f \( -name StationX.pck -o -path './data_*/StationX.*' \) | LC_ALL=C sort; }
base_files() { find . -type f ! -name StationX.pck ! -path './data_*/StationX.*' | LC_ALL=C sort; }
(
  cd build
  tar czf StationX-linux-x86_64.tar.gz -C linux .
  (cd windows && zip -qr ../StationX-windows-x86_64.zip .)
  for p in linux:StationX-linux-x86_64 windows:StationX-windows-x86_64; do
    dir=${p%%:*} name=${p#*:}
    (cd "$dir" && patch_files | zip -q -@ "../$name-patch.zip" && base_files | tr '\n' '\0' | xargs -0 sha256sum) > "$name-base.txt"
  done
)
ls -lh build/*.tar.gz build/*.zip build/*.txt
