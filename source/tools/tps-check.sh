#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet build NoDwarf.csproj --nologo
# Use the same Debug build as the normal local game; run the two speeds separately.
exec "$GODOT_BIN" --path "$PROJECT_DIR" -- --tps-test "$@"
