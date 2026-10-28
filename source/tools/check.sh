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

# Mouse-driven resident groups and area designation, including UI isolation.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --box-selection-test > "$PROJECT_DIR/.local/box-selection.log" 2>&1
grep 'BOX_SELECTION_OK:' "$PROJECT_DIR/.local/box-selection.log"

# New tool recipes, scoped work bonuses and both rigs' weapon attachments/sweeps.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --tool-equipment-test > "$PROJECT_DIR/.local/tool-equipment.log" 2>&1
grep 'TOOL_MODELS_OK:' "$PROJECT_DIR/.local/tool-equipment.log"

# This suite deliberately emits CONSOLE_EXPECTED_ errors to exercise log capture.
# Its own assertions and process exit code distinguish those from test failures.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --console-test > "$PROJECT_DIR/.local/console.log" 2>&1
grep 'CONSOLE_OK:' "$PROJECT_DIR/.local/console.log"

# Cache invalidation, differential building updates and large-room picking.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --interior-performance-test --interior-label=regression --interior-checks > "$PROJECT_DIR/.local/interior-check.log" 2>&1
grep 'INTERIOR_OPTIMIZATION_OK:' "$PROJECT_DIR/.local/interior-check.log"

# Normal gameplay uses asynchronous occlusion; exercise publication/cancellation too.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --interior-thread-test --thread-label=regression --thread-checks > "$PROJECT_DIR/.local/interior-thread-check.log" 2>&1
grep 'INTERIOR_ASYNC_CHECKS_OK:' "$PROJECT_DIR/.local/interior-thread-check.log"
