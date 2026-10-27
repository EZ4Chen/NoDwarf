#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet build NoDwarf.csproj --nologo
"$GODOT_BIN" --headless --path "$PROJECT_DIR" --editor --import --quit
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --smoke-test 2>&1 | tee "$PROJECT_DIR/.local/smoke.log"
grep -q 'SMOKE_OK:' "$PROJECT_DIR/.local/smoke.log"
if grep -q 'ERROR:\|Exception' "$PROJECT_DIR/.local/smoke.log"; then
    echo "Smoke test reported errors." >&2
    exit 1
fi

"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --interaction-test 2>&1 | tee "$PROJECT_DIR/.local/interaction.log"
grep -q 'INTERACTION_OK:' "$PROJECT_DIR/.local/interaction.log"
if grep -q 'ERROR:\|Exception' "$PROJECT_DIR/.local/interaction.log"; then
    echo "Mouse interaction test reported errors." >&2
    exit 1
fi
