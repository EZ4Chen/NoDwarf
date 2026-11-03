param(
    [string]$Godot = $env:NODWARF_GODOT_BIN,
    [ValidateSet("editor", "run", "build", "check")][string]$Mode = "editor",
    [ValidateSet("quick", "regression", "performance")][string]$CheckGroup = "regression",
    [string]$Only
)
$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent
$workspaceDir = Split-Path $projectDir -Parent
if (-not $Godot) {
    $version = (Get-Content (Join-Path $workspaceDir "tools/ci/godot-downloads.json") -Raw | ConvertFrom-Json).version
    $Godot = Join-Path $workspaceDir ".tools/godot/Godot_v$version-stable_mono_win64/Godot_v$version-stable_mono_win64_console.exe"
}
$godotPath = (Resolve-Path $Godot).Path
$dotnetRoot = $env:NODWARF_DOTNET_ROOT
if (-not $dotnetRoot -and (Test-Path (Join-Path $workspaceDir ".tools/dotnet/dotnet.exe"))) {
    $dotnetRoot = Join-Path $workspaceDir ".tools/dotnet"
}
if ($dotnetRoot) {
    $env:DOTNET_ROOT = (Resolve-Path $dotnetRoot).Path
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK is missing. See README.md." }
$feed = Join-Path $projectDir ".local/godot-packages"
$packages = Join-Path (Split-Path $godotPath -Parent) "GodotSharp/Tools/nupkgs"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item (Join-Path $packages "*.nupkg") $feed -Force
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_HOME = Join-Path $projectDir ".local/dotnet-home"
$env:NUGET_PACKAGES = Join-Path $projectDir ".local/nuget-packages"
Push-Location $projectDir
try {
    if ($Mode -eq "check") {
        $checkArgs = @("--root", $workspaceDir, "--godot", $godotPath, "--group", $CheckGroup)
        if ($Only) { $checkArgs += @("--only", $Only) }
        dotnet run --project (Join-Path $workspaceDir "tools/checks/runner/CheckRunner.csproj") -- @checkArgs
    } else {
        dotnet build NoDwarf.csproj --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
        if ($Mode -eq "editor") { & $godotPath --path $projectDir --editor }
        elseif ($Mode -eq "run") {
            & $godotPath --headless --path $projectDir --editor --import --quit
            if ($LASTEXITCODE -ne 0) { throw "Resource import failed" }
            & $godotPath --path $projectDir
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "Development command failed ($Mode), exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}
