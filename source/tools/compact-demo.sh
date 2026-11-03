#!/bin/bash
source "$(dirname "$0")/env.sh"
exec "$GODOT_BIN" --path "$PROJECT_DIR" res://Development/Scenes/Characters/CompactDwarfDemo.tscn "$@"
