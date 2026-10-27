#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet run --project "$WORKSPACE_DIR/benchmarks/NavigationBench.csproj" -c Release -- "$@"
