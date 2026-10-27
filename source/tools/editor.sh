#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet build NoDwarf.csproj --nologo
exec "$GODOT_BIN" --path "$PROJECT_DIR" --editor "$@"
