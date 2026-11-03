"""Inspect and launch a real export without adding test code to the game."""
from pathlib import Path
import os
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
LOGS = ROOT / "source/.local/checks"


def checked_run(name, command, cwd, markers=(), env=None):
    LOGS.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(list(map(str, command)), cwd=cwd, env=env,
                            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
    output = result.stdout + result.stderr
    log = LOGS / (name + ".log")
    log.write_text(output, encoding="utf-8")
    if result.returncode or any(m not in output for m in markers) or re.search(r"ERROR:|\b\w*Exception\b", output):
        raise RuntimeError(f"{name} failed; see {log}")
    print("PASS " + name, flush=True)


def verify_export(godot, output, executable):
    output, executable, godot = Path(output).resolve(), Path(executable).resolve(), Path(godot).resolve()
    pack, = output.rglob("*.pck")
    checked_run("package-assemblies", ["dotnet", "run", "--project", ROOT / "tools/checks/package/PackageChecks.csproj",
                                        "--", output], ROOT, ["PACKAGE_ASSEMBLY_OK: NoDwarf.dll", "PACKAGE_ASSEMBLY_OK: NoDwarf.Core.dll"])
    with tempfile.TemporaryDirectory(prefix="nodwarf-export-") as folder:
        work = Path(folder)
        # Isolated user settings/logs; no source checkout or previous save is required.
        env = os.environ.copy()
        env.update(APPDATA=str(work / "appdata"), XDG_DATA_HOME=str(work / "data"))
        (work / "project.godot").write_text("config_version=5\n", encoding="utf-8")
        checked_run("package-resources", [godot, "--headless", "--path", work, "--script", ROOT / "tools/ci/verify_pack.gd", "--", pack],
                    work, ["PACKAGE_RESOURCES_OK:"], env)
        # Official release templates disable command-line scene overrides. Exercise
        # their real main scene; entering a camp is covered by development checks
        # and the native-window release acceptance, without a shipped test hook.
        checked_run("export-startup", [executable, "--headless", "--quit-after", "120",
                    "--log-file", LOGS / "export-startup-engine.log"], work, ["Godot Engine"], env)
    print("EXPORT_OK: clean assemblies/resources and standalone main-menu launch", flush=True)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot")
    parser.add_argument("output")
    parser.add_argument("executable")
    args = parser.parse_args()
    verify_export(args.godot, args.output, args.executable)
