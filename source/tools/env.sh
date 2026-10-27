#!/bin/bash
# Source this file from the other launch scripts.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKSPACE_DIR="$(cd "$PROJECT_DIR/.." && pwd)"
export DOTNET_ROOT="${NODWARF_DOTNET_ROOT:-$WORKSPACE_DIR/.tools/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$PROJECT_DIR/.local/dotnet-home"
export NUGET_PACKAGES="$PROJECT_DIR/.local/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
GODOT_BIN="${NODWARF_GODOT_BIN:-$WORKSPACE_DIR/.tools/godot/Godot_mono.app/Contents/MacOS/Godot}"
if [[ ! -x "$GODOT_BIN" ]]; then
    echo "Godot .NET editor not found: $GODOT_BIN" >&2
    exit 1
fi
if ! command -v dotnet >/dev/null; then
    echo "The .NET SDK is missing. See README.md." >&2
    exit 1
fi
mkdir -p "$PROJECT_DIR/.local/godot-packages"
GODOT_PACKAGES="$(dirname "$GODOT_BIN")/../Resources/GodotSharp/Tools/nupkgs"
if [[ -d "$GODOT_PACKAGES" ]]; then
    for package in "$GODOT_PACKAGES/"*.nupkg; do
        target="$PROJECT_DIR/.local/godot-packages/$(basename "$package")"
        if [[ ! -f "$target" ]]; then cp "$package" "$target"; fi
    done
fi
cd "$PROJECT_DIR"
