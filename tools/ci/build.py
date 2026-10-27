"""Install verified Godot .NET tools, export, smoke-test and package one desktop target."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "source"
LOCAL = PROJECT / ".local" / "ci"
DIST = ROOT / "dist"
CONFIG = json.loads(Path(__file__).with_name("godot-downloads.json").read_text())
TARGETS = {
    "windows": ("Windows x64", "windows-x64", "NoDwarf.exe"),
    "macos": ("macOS Apple Silicon", "macos-arm64", "NoDwarf.app"),
}


def run(args, cwd=PROJECT, timeout=600):
    print("+", " ".join(map(str, args)), flush=True)
    subprocess.run(list(map(str, args)), cwd=cwd, check=True, timeout=timeout)


def sha256(path):
    with path.open("rb") as stream:
        digest = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def install(target):
    LOCAL.mkdir(parents=True, exist_ok=True)
    version = CONFIG["version"]
    if f'Godot.NET.Sdk/{version}' not in (PROJECT / "NoDwarf.csproj").read_text():
        raise RuntimeError("Update godot-downloads.json to match NoDwarf.csproj")
    for key in (target, "templates"):
        asset = CONFIG[key]
        archive = LOCAL / asset["name"]
        if not archive.exists() or sha256(archive) != asset["sha256"]:
            url = f"https://github.com/godotengine/godot-builds/releases/download/{version}-stable/{asset['name']}"
            run(["curl", "--fail", "--location", "--retry", "3", "--max-time", "900",
                 "--output", archive, url], timeout=960)
        if sha256(archive) != asset["sha256"]:
            raise RuntimeError(f"Checksum mismatch: {asset['name']}")
        destination = LOCAL / key
        destination.mkdir(exist_ok=True)
        if key == "macos":
            run(["ditto", "-x", "-k", archive, destination])
        else:
            with zipfile.ZipFile(archive) as z:
                z.extractall(destination)

    # Custom template paths keep this installer entirely inside the workspace.
    templates = LOCAL / "templates" / "templates"
    if target == "macos":
        godot = LOCAL / "macos" / "Godot_mono.app" / "Contents" / "MacOS" / "Godot"
        packages = godot.parent.parent / "Resources/GodotSharp/Tools/nupkgs"
        template = templates / "macos.zip"
    else:
        godot = next((LOCAL / "windows").rglob("*_mono_win64_console.exe"))
        packages = godot.parent / "GodotSharp/Tools/nupkgs"
        template = templates / "windows_release_x86_64.exe"
    if not template.is_file():
        raise RuntimeError(f"Missing .NET export template: {template}")
    feed = PROJECT / ".local/godot-packages"
    feed.mkdir(parents=True, exist_ok=True)
    nupkgs = list(packages.glob("*.nupkg"))
    if not nupkgs:
        raise RuntimeError(f"Missing bundled NuGet packages: {packages}")
    for package in nupkgs:
        shutil.copy2(package, feed)
    return godot, template


def checked_game(executable, argument, marker):
    log = LOCAL / f"{argument.removeprefix('--')}.log"
    with log.open("w", encoding="utf-8") as output:
        result = subprocess.run([str(executable), "--headless", "--", argument],
                                cwd=executable.parent, stdout=output,
                                stderr=subprocess.STDOUT, timeout=180)
    text = log.read_text(encoding="utf-8", errors="replace")
    print(text, flush=True)
    if result.returncode or marker not in text or re.search(r"ERROR:|Exception", text):
        raise RuntimeError(f"Exported game failed {argument}; see {log}")


def build(target):
    if (target == "macos") != (sys.platform == "darwin"):
        raise RuntimeError("Run each target on its native GitHub runner")
    preset, platform, filename = TARGETS[target]
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    version = f"dev-{commit[:7]}"
    if os.environ.get("GITHUB_REF_TYPE") == "tag":
        version = os.environ["GITHUB_REF_NAME"]
        if not re.fullmatch(r"v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
            raise RuntimeError("Use a version tag such as v0.1.0 or v0.1.0-alpha.1")
    godot, template = install(target)
    os.environ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    os.environ["DOTNET_NOLOGO"] = "1"
    run([godot, "--version"])
    run(["dotnet", "build", "NoDwarf.csproj", "--configuration", "Debug", "--nologo"])
    run([godot, "--headless", "--path", PROJECT, "--editor", "--import", "--quit"])

    output = PROJECT / "builds" / platform
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    presets = PROJECT / "export_presets.cfg"
    original = presets.read_text(encoding="utf-8")
    index = 1 if target == "macos" else 0
    section = f"[preset.{index}.options]"
    configured = original.replace(section, section + '\ncustom_template/release=' + json.dumps(template.as_posix()))
    numeric_version = re.sub(r"^v", "", version).split("-")[0] if version.startswith("v") else "0.0.0"
    configured = configured.replace('application/short_version="0.1.0"', f'application/short_version="{numeric_version}"')
    configured = configured.replace('application/version="0.1.0"', f'application/version="{numeric_version}"')
    try:
        presets.write_text(configured, encoding="utf-8")
        run([godot, "--headless", "--path", PROJECT, "--export-release", preset, output / filename])
    finally:
        presets.write_text(original, encoding="utf-8")

    executable = output / filename
    if target == "macos":
        executable = output / filename / "Contents/MacOS/NoDwarf"
        run(["codesign", "--verify", "--deep", "--strict", output / filename])
    if not executable.is_file():
        raise RuntimeError("Export did not produce a runnable game")
    # Exercise the actual self-contained export, including assets and bundled .NET.
    checked_game(executable, "--smoke-test", "SMOKE_OK:")
    checked_game(executable, "--interaction-test", "INTERACTION_OK:")
    shutil.copy2(ROOT / "docs/PLAYER-README.txt", output / "README.txt")
    shutil.copy2(PROJECT / "Assets/Textures/PureBDcraft/terms of use.txt", output / "PureBDcraft-terms.txt")
    DIST.mkdir(exist_ok=True)
    archive = DIST / f"NoDwarf-{version}-{platform}.zip"
    if archive.exists():
        archive.unlink()
    if target == "macos":
        run(["ditto", "-c", "-k", "--sequesterRsrc", output, archive])
    else:
        shutil.make_archive(str(archive.with_suffix("")), "zip", output)
    manifest = {"platform": platform, "commit": commit, "version": version,
                "file": archive.name, "sha256": sha256(archive)}
    (DIST / f"{platform}.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=TARGETS)
    args = parser.parse_args()
    build(args.target)
