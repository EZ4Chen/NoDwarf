#!/bin/bash
source "$(dirname "$0")/env.sh"
dotnet build NoDwarf.csproj --nologo "$@"
