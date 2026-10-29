#!/bin/bash
source "$(dirname "$0")/env.sh"
exec "$GODOT_BIN" --path "$PROJECT_DIR" --resolution 1440x1000 res://Scenes/Characters/FlatPawnDemo.tscn "$@"
