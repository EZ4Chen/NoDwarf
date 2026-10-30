#!/bin/bash
source "$(dirname "$0")/env.sh"
# One offline manifest is shared by macOS, Windows and exported-package checks.
dotnet run --project "$WORKSPACE_DIR/tools/checks/runner/CheckRunner.csproj" -- --root "$WORKSPACE_DIR" --godot "$GODOT_BIN" "$@"
