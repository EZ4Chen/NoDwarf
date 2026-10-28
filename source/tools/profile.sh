#!/bin/bash
profile_caller_dir="$PWD"
source "$(dirname "$0")/env.sh"
export NODWARF_GODOT_BIN="$GODOT_BIN"
cd "$profile_caller_dir"
exec python3 "$PROJECT_DIR/tools/perf/profile.py" "$@"
