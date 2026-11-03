#!/bin/bash
bash "$(dirname "$0")/source/tools/run.sh" "$@"
status=$?
if [ "$status" -ne 0 ]; then
    read -r -p "Launch failed. Press Enter to close." _
fi
exit "$status"
