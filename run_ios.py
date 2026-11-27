#!/usr/bin/env python3
"""Fetch iOS frameworks, then build / install / launch the MAUI app (macOS + device).

Prefer this over `dotnet build … -t:Run`: mlaunch often hits MT1044
(`A device must be specified using --devname`) on CoreDevice / wireless phones.
"""
from __future__ import annotations

import plistlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BUNDLE_ID = "com.faceplugin.facerecognition.app"
TEAM_ID = "F5UMQMPPB8"
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
XCODE_PROFILES = (
    Path.home() / "Library" / "Developer" / "Xcode" / "UserData" / "Provisioning Profiles"
)
LEGACY_PROFILES = Path.home() / "Library" / "MobileDevice" / "Provisioning Profiles"


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


def _profile_app_ids(path: Path) -> str:
    try:
        raw = subprocess.check_output(
            ["security", "cms", "-D", "-i", str(path)], stderr=subprocess.DEVNULL
        )
        pl = plistlib.loads(raw)
        return str(pl.get("Entitlements", {}).get("application-identifier") or "")
    except (OSError, subprocess.CalledProcessError, plistlib.InvalidFileException):
        return ""


def sync_profiles() -> None:
    """dotnet/mlaunch still reads ~/Library/MobileDevice; Xcode writes UserData."""
    if not XCODE_PROFILES.is_dir():
        return
    LEGACY_PROFILES.mkdir(parents=True, exist_ok=True)
    for src in XCODE_PROFILES.glob("*.mobileprovision"):
        dest = LEGACY_PROFILES / src.name
        if not dest.exists() or dest.stat().st_mtime < src.stat().st_mtime:
            shutil.copy2(src, dest)


def has_bundle_profile(bundle_id: str) -> bool:
    needle = f".{bundle_id}"
    for folder in (XCODE_PROFILES, LEGACY_PROFILES):
        if not folder.is_dir():
            continue
        for path in folder.glob("*.mobileprovision"):
            app_id = _profile_app_ids(path)
            if app_id.endswith(needle) or app_id.endswith(".*"):
                return True
    return False


def ensure_provisioning_profile(udid: str) -> None:
    """Make sure a development profile for BUNDLE_ID exists (required by net8.0-ios)."""
    sync_profiles()
    if has_bundle_profile(BUNDLE_ID):
        return
    print(f"no local provisioning profile for {BUNDLE_ID}; refreshing via Xcode…")
    need("xcodebuild", "Install Xcode.")
    with tempfile.TemporaryDirectory(prefix="fp-maui-prov-") as tmp:
        tmp_path = Path(tmp)
        app_dir = tmp_path / "ProvApp"
        app_dir.mkdir()
        (app_dir / "main.m").write_text("int main(){return 0;}\n", encoding="utf-8")
        (app_dir / "Info.plist").write_text(
            f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>{BUNDLE_ID}</string>
  <key>CFBundleName</key><string>ProvApp</string>
  <key>CFBundleExecutable</key><string>ProvApp</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
</dict></plist>
""",
            encoding="utf-8",
        )
        proj = tmp_path / "ProvApp.xcodeproj"
        proj.mkdir()
        (proj / "project.pbxproj").write_text(
            f"""// !$*UTF8*$!
{{
	archiveVersion = 1;
	classes = {{}};
	objectVersion = 56;
	objects = {{
		A1 /* main.m */ = {{isa = PBXFileReference; lastKnownFileType = sourcecode.c.objc; path = main.m; sourceTree = "<group>"; }};
		A2 /* Info.plist */ = {{isa = PBXFileReference; lastKnownFileType = text.plist.xml; path = Info.plist; sourceTree = "<group>"; }};
		A3 /* ProvApp.app */ = {{isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = ProvApp.app; sourceTree = BUILT_PRODUCTS_DIR; }};
		B1 /* main.m in Sources */ = {{isa = PBXBuildFile; fileRef = A1 /* main.m */; }};
		C1 /* Sources */ = {{isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = ( B1 /* main.m in Sources */, ); runOnlyForDeploymentPostprocessing = 0; }};
		C2 /* Frameworks */ = {{isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; }};
		C3 /* Resources */ = {{isa = PBXResourcesBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; }};
		G1 /* Products */ = {{isa = PBXGroup; children = ( A3 /* ProvApp.app */, ); name = Products; sourceTree = "<group>"; }};
		G2 /* ProvApp */ = {{isa = PBXGroup; children = ( A1 /* main.m */, A2 /* Info.plist */, ); path = ProvApp; sourceTree = "<group>"; }};
		G0 = {{isa = PBXGroup; children = ( G2 /* ProvApp */, G1 /* Products */, ); sourceTree = "<group>"; }};
		D1 /* Debug */ = {{isa = XCBuildConfiguration; buildSettings = {{ CODE_SIGN_STYLE = Automatic; DEVELOPMENT_TEAM = {TEAM_ID}; INFOPLIST_FILE = ProvApp/Info.plist; PRODUCT_BUNDLE_IDENTIFIER = {BUNDLE_ID}; PRODUCT_NAME = "$(TARGET_NAME)"; SDKROOT = iphoneos; TARGETED_DEVICE_FAMILY = "1,2"; }}; name = Debug; }};
		D2 /* Release */ = {{isa = XCBuildConfiguration; buildSettings = {{ CODE_SIGN_STYLE = Automatic; DEVELOPMENT_TEAM = {TEAM_ID}; INFOPLIST_FILE = ProvApp/Info.plist; PRODUCT_BUNDLE_IDENTIFIER = {BUNDLE_ID}; PRODUCT_NAME = "$(TARGET_NAME)"; SDKROOT = iphoneos; TARGETED_DEVICE_FAMILY = "1,2"; }}; name = Release; }};
		P1 /* Debug */ = {{isa = XCBuildConfiguration; buildSettings = {{}}; name = Debug; }};
		P2 /* Release */ = {{isa = XCBuildConfiguration; buildSettings = {{}}; name = Release; }};
		L1 /* ProjectConfigs */ = {{isa = XCConfigurationList; buildConfigurations = ( P1 /* Debug */, P2 /* Release */, ); defaultConfigurationIsPublic = NO; defaultConfigurationName = Release; }};
		L2 /* TargetConfigs */ = {{isa = XCConfigurationList; buildConfigurations = ( D1 /* Debug */, D2 /* Release */, ); defaultConfigurationIsPublic = NO; defaultConfigurationName = Release; }};
		T1 /* ProvApp */ = {{isa = PBXNativeTarget; buildConfigurationList = L2 /* TargetConfigs */; buildPhases = ( C1 /* Sources */, C2 /* Frameworks */, C3 /* Resources */, ); buildRules = (); dependencies = (); name = ProvApp; productName = ProvApp; productReference = A3 /* ProvApp.app */; productType = "com.apple.product-type.application"; }};
		PR /* Project */ = {{isa = PBXProject; attributes = {{ LastUpgradeCheck = 1500; }}; buildConfigurationList = L1 /* ProjectConfigs */; compatibilityVersion = "Xcode 14.0"; developmentRegion = en; hasScannedForEncodings = 0; knownRegions = ( en, Base, ); mainGroup = G0; productRefGroup = G1 /* Products */; projectDirPath = ""; projectRoot = ""; targets = ( T1 /* ProvApp */, ); }};
	}};
	rootObject = PR /* Project */;
}}
""",
            encoding="utf-8",
        )
        code = subprocess.run(
            [
                "xcodebuild",
                "-project",
                str(proj),
                "-scheme",
                "ProvApp",
                "-configuration",
                "Debug",
                "-destination",
                f"id={udid}",
                "-allowProvisioningUpdates",
                "-allowProvisioningDeviceRegistration",
                f"DEVELOPMENT_TEAM={TEAM_ID}",
                "CODE_SIGN_STYLE=Automatic",
                "build",
            ],
            cwd=str(tmp_path),
        ).returncode
        if code:
            die(
                f"could not create a provisioning profile for {BUNDLE_ID}.\n"
                "In Xcode: Settings → Accounts → your Apple ID → Download Manual Profiles,\n"
                "or open any app with this bundle id once with Automatic signing."
            )
    sync_profiles()
    if not has_bundle_profile(BUNDLE_ID):
        die(f"still no provisioning profile for {BUNDLE_ID} after Xcode refresh")


def main() -> None:
    import fp_releases

    if sys.platform != "darwin":
        die("iOS builds need macOS and Xcode. Use python run_android.py on this machine.")
    dotnet = need("dotnet", "Install the .NET 8 SDK.")
    print(f"{ROOT.name}: ios (maui)")
    fp_releases.ensure_ios_engine(ROOT, FW, with_bridge=False)
    clean()
    udid = ios_udid()
    ensure_provisioning_profile(udid)
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
