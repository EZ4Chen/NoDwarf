#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet build NoDwarf.csproj --nologo
"$GODOT_BIN" --headless --path "$PROJECT_DIR" --editor --import --quit
exec "$GODOT_BIN" --path "$PROJECT_DIR" "$@"
