#!/bin/bash
source "$(dirname "$0")/env.sh"
# One offline development-check manifest is shared by macOS and Windows.
dotnet run --project "$WORKSPACE_DIR/tools/checks/runner/CheckRunner.csproj" -- --root "$WORKSPACE_DIR" --godot "$GODOT_BIN" "$@"
