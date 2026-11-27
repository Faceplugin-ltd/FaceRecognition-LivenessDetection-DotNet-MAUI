#!/usr/bin/env python3
"""Fetch iOS frameworks if missing, then build the .NET MAUI iOS app (macOS only).

Mirrors the README manual steps: fetch missing native binaries, then the
standard dotnet commands for this repo.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
IS_WIN = os.name == "nt"


def die(msg: str) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    raise SystemExit(1)


def which(name: str) -> str | None:
    hits = [shutil.which(name)]
    if IS_WIN:
        hits.extend([shutil.which(name + ".cmd"), shutil.which(name + ".exe")])
    return next((h for h in hits if h), None)


def need(name: str, hint: str) -> str:
    path = which(name)
    if not path:
        die(f"{name} is not on PATH. {hint}")
    return path


def run(args: list[str], cwd: Path | None = None) -> None:
    print("+", " ".join(str(a) for a in args))
    r = subprocess.run(args, cwd=str(cwd or ROOT))
    if r.returncode:
        raise SystemExit(r.returncode)


PROJ = ROOT / "src" / "FaceRecognition.Net.Maui" / "FaceRecognition.Net.Maui.csproj"
FW = PROJ.parent / "Platforms" / "iOS" / "Frameworks"


def main() -> None:
    import fp_releases

    if sys.platform != "darwin":
        die("iOS builds need macOS and Xcode. Use python run_android.py on this machine.")
    dotnet = need("dotnet", "Install the .NET 8 SDK.")
    print(f"{ROOT.name}: ios (maui)")
    fp_releases.ensure_ios_engine(ROOT, FW, with_bridge=False)
    run([dotnet, "build", str(PROJ), "-f", "net8.0-ios", "-r", "ios-arm64"])


if __name__ == "__main__":
    main()
