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
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --designation-test > "$PROJECT_DIR/.local/designation.log" 2>&1
grep 'DESIGNATION_OK:' "$PROJECT_DIR/.local/designation.log"
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --level-guide-test > "$PROJECT_DIR/.local/level-guide.log" 2>&1
grep 'LEVEL_GUIDE_OK:' "$PROJECT_DIR/.local/level-guide.log"
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --object-commands-test > "$PROJECT_DIR/.local/object-commands.log" 2>&1
grep 'OBJECT_COMMANDS_OK:' "$PROJECT_DIR/.local/object-commands.log"

# Open trenches use local foreground cuts while mining; visual cuts must not alter dig cells.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --mining-cutaway-test > "$PROJECT_DIR/.local/mining-cutaway.log" 2>&1
grep 'MINING_CUTAWAY_OK:' "$PROJECT_DIR/.local/mining-cutaway.log"

"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --dig-volume-test > "$PROJECT_DIR/.local/dig-volume.log" 2>&1
grep 'DIG_VOLUME_OK:' "$PROJECT_DIR/.local/dig-volume.log"

# Current 2D residents: tool rules, equipment art, directions, pause and movement.
# Legacy rig/mesh assets and their authoring reviews have been removed.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --sprite-integration-test > "$PROJECT_DIR/.local/sprite-integration.log" 2>&1
grep 'SPRITE_INTEGRATION_OK:' "$PROJECT_DIR/.local/sprite-integration.log"

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

# Water warnings: actual notice/dialog actions, underground focus and readback.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --water-safety-ui-test > "$PROJECT_DIR/.local/water-safety-ui.log" 2>&1
grep 'WATER_SAFETY_UI_OK:' "$PROJECT_DIR/.local/water-safety-ui.log"

# Map texture cadence and atlas layer/selection regression.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --map-display-test > "$PROJECT_DIR/.local/map-display.log" 2>&1
grep 'MAP_DISPLAY_OK:' "$PROJECT_DIR/.local/map-display.log"

# Generated river/lake geometry, water minimap and natural cavern visibility.
"$GODOT_BIN" --headless --path "$PROJECT_DIR" -- --geography-preview-test > "$PROJECT_DIR/.local/geography-render.log" 2>&1
grep 'GEOGRAPHY_UI_OK:' "$PROJECT_DIR/.local/geography-render.log"
