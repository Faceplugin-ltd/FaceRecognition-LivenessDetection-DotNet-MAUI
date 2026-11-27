#!/usr/bin/env python3
"""Fetch iOS frameworks, then build / install / launch the MAUI app (macOS + device).

Prefer this over `dotnet build … -t:Run`: mlaunch often hits MT1044
(`A device must be specified using --devname`) on CoreDevice / wireless phones.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BUNDLE_ID = "com.faceplugin.facerecognition.app"
PROJ = ROOT / "src" / "FaceRecognition.Net.Maui" / "FaceRecognition.Net.Maui.csproj"
FW = PROJ.parent / "Platforms" / "iOS" / "Frameworks"
APP = (
    PROJ.parent
    / "bin"
    / "Debug"
    / "net8.0-ios"
    / "ios-arm64"
    / "FaceRecognition.Net.Maui.app"
)


def die(msg: str) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    raise SystemExit(1)


def need(name: str, hint: str) -> str:
    path = shutil.which(name)
    if not path:
        die(f"{name} is not on PATH. {hint}")
    return path


def run(args: list[str], *, check: bool = True) -> int:
    print("+", " ".join(str(a) for a in args))
    code = subprocess.run(args, cwd=str(ROOT)).returncode
    if check and code:
        raise SystemExit(code)
    return code


def clean() -> None:
    for name in ("bin", "obj"):
        path = PROJ.parent / name
        if path.is_dir():
            print(f"cleaning {path.relative_to(ROOT)}")
            shutil.rmtree(path)


def ios_udid() -> str:
    need("xcrun", "Install Xcode and its command-line tools.")
    raw = subprocess.check_output(
        ["xcrun", "xctrace", "list", "devices"],
        text=True,
        stderr=subprocess.STDOUT,
        errors="replace",
    )
    in_sims = False
    for line in raw.splitlines():
        lower = line.lower()
        if "simulator" in lower and "==" in line:
            in_sims = True
            continue
        if in_sims or "simulator" in lower:
            continue
        if "(" not in line or ")" not in line:
            continue
        if "mac" in lower or "watch" in lower or "apple tv" in lower:
            continue
        if "iphone" not in lower and "ipad" not in lower:
            continue
        udid = line.rsplit("(", 1)[-1].strip().rstrip(")")
        if len(udid.replace("-", "")) >= 16 and udid.replace("-", "").isalnum():
            print(f"iOS device: {line.strip()}")
            return udid
    die("no physical iPhone/iPad. Unlock one, trust this Mac, and retry.")


def main() -> None:
    import fp_releases

    if sys.platform != "darwin":
        die("iOS builds need macOS and Xcode. Use python run_android.py on this machine.")
    dotnet = need("dotnet", "Install the .NET 8 SDK.")
    print(f"{ROOT.name}: ios (maui)")
    fp_releases.ensure_ios_engine(ROOT, FW, with_bridge=False)
    clean()
    udid = ios_udid()
    run([dotnet, "build", str(PROJ), "-f", "net8.0-ios", "-r", "ios-arm64"])
    if not APP.is_dir():
        die(f"app bundle missing after build: {APP}")
    run(
        [
            "xcrun", "devicectl", "device", "install", "app",
            "--device", udid, "--timeout", "300", str(APP),
        ]
    )
    if run(
        [
            "xcrun", "devicectl", "device", "process", "launch",
            "--terminate-existing", "--device", udid, BUNDLE_ID,
        ],
        check=False,
    ):
        print(
            "\nInstall OK, but launch was denied (Trust the developer on the phone):\n"
            "  Settings → General → VPN & Device Management → Trust\n"
            "Then open the app or re-run: python3 run_ios.py\n",
            file=sys.stderr,
        )
        return
    print(f"launched {BUNDLE_ID}")


if __name__ == "__main__":
    main()
