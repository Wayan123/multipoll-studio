#!/usr/bin/env bash
# Build MultiPollStudio.exe (.NET Framework 4.8, WinForms) from WSL with the Roslyn csc shipped by Visual Studio.
# Usage: ./build.sh [Release|Debug]   -> dist/MultiPollStudio.exe
#        ./build.sh uitest            -> dist/MultiPollStudioUiTest.exe (GUI smoke test, see tests/UiSmoke.cs)
set -euo pipefail
cd "$(dirname "$0")"
CONFIG="${1:-Release}"
CSC="${CSC:-}"
if [[ -z "$CSC" ]]; then
  for c in "/mnt/c/Program Files/Microsoft Visual Studio/"*/*/MSBuild/Current/Bin/Roslyn/csc.exe; do CSC="$c"; done
fi
[[ -x "$CSC" ]] || { echo "csc.exe not found; set CSC=/path/to/csc.exe" >&2; exit 1; }
mkdir -p dist
OUT=MultiPollStudio.exe
mapfile -t SRC < <(find src -name '*.cs' | sort)
if [[ "$CONFIG" == uitest ]]; then
  OUT=MultiPollStudioUiTest.exe
  mapfile -t SRC < <( (find src -name '*.cs' ! -name Program.cs ! -name SelfTest.cs; find tests -name '*.cs') | sort)
fi
WIN_SRC=(); for f in "${SRC[@]}"; do WIN_SRC+=("$(wslpath -w "$f")"); done
FLAGS=(-nologo -codepage:65001 -langversion:7.3 -target:winexe -platform:anycpu -nowarn:1591
       -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:System.Windows.Forms.DataVisualization.dll
       -win32manifest:"$(wslpath -w app.manifest)" -win32icon:"$(wslpath -w openpoll.ico)")
if [[ "$CONFIG" == Debug ]]; then FLAGS+=(-debug+ -define:DEBUG); else FLAGS+=(-optimize+ -debug-); fi
# built-in device profiles are embedded (minified) as resources "profile.<name>.json"
rm -rf obj/profiles && mkdir -p obj/profiles
for p in profiles/*.json; do
  python3 -c 'import json,sys; json.dump(json.load(open(sys.argv[1], encoding="utf-8")), open(sys.argv[2], "w", encoding="utf-8"), separators=(",", ":"), ensure_ascii=False)' "$p" "obj/$p"
  FLAGS+=("-resource:$(wslpath -w "obj/$p"),profile.$(basename "$p")")
done
"$CSC" "${FLAGS[@]}" -out:"$(wslpath -w dist)\\$OUT" "${WIN_SRC[@]}" | tr -d '\r'
ls -l "dist/$OUT"
