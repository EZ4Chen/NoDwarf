#!/bin/bash
source "$(dirname "$0")/env.sh"
exec "$GODOT_BIN" --path "$PROJECT_DIR" res://Scenes/Characters/WhitebodyDemo.tscn "$@"
