param(
    [Parameter(Mandatory=$true)][string]$Godot,
    [ValidateSet("editor", "run", "check")][string]$Mode = "editor"
)
$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent
$godotPath = (Resolve-Path $Godot).Path
$feed = Join-Path $projectDir ".local/godot-packages"
$packages = Join-Path (Split-Path $godotPath -Parent) "GodotSharp/Tools/nupkgs"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item (Join-Path $packages "*.nupkg") $feed -Force
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_HOME = Join-Path $projectDir ".local/dotnet-home"
$env:NUGET_PACKAGES = Join-Path $projectDir ".local/nuget-packages"
Push-Location $projectDir
try {
    dotnet build NoDwarf.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    if ($Mode -eq "check") {
        & $godotPath --headless --path $projectDir --editor --import --quit
        if ($LASTEXITCODE -ne 0) { throw "Import failed" }
        & $godotPath --headless --path $projectDir -- --smoke-test
        if ($LASTEXITCODE -ne 0) { throw "Simulation checks failed" }
        & $godotPath --headless --path $projectDir -- --interaction-test
    } elseif ($Mode -eq "editor") {
        & $godotPath --path $projectDir --editor
    } else {
        & $godotPath --path $projectDir
    }
    if ($LASTEXITCODE -ne 0) { throw "Godot exited with an error" }
} finally {
    Pop-Location
}
