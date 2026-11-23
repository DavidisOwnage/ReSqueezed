"""Build a source-only ReSqueezed launcher distribution for Windows x64."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from zipfile import ZipFile, ZIP_DEFLATED


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=repo / "dist" / "ReSqueezed Launcher")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        parser.error("Choose an empty output directory so local installs cannot enter the release.")
    output.mkdir(parents=True, exist_ok=True)
    launcher = repo / "launcher"
    csc = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    exe = output / "ReSqueezed Launcher.exe"
    subprocess.run([
        str(csc), "/nologo", "/target:winexe", "/platform:x64", "/optimize+",
        "/reference:System.Windows.Forms.dll", "/reference:System.Drawing.dll",
        "/reference:System.IO.Compression.dll", "/reference:System.IO.Compression.FileSystem.dll",
        "/reference:System.Runtime.Serialization.dll",
        f"/win32icon:{launcher / 'assets/recomp.ico'}",
        f"/resource:{launcher / 'assets/recomp.ico'},GameIcon",
        f"/resource:{launcher / 'assets/city-backdrop.png'},CityBackdrop",
        f"/resource:{launcher / 'assets/resqueezed.png'},ReSqueezedLogo",
        f"/resource:{launcher / 'dependencies.json'},Dependencies",
        f"/out:{exe}",
        *map(str, sorted(launcher.glob("*.cs"))),
    ], check=True)
    for directory in ("source", "launcher"):
        shutil.copytree(repo / directory, output / directory,
                        ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
    for name in ("README.md", ".gitignore", "THIRD-PARTY-NOTICES.txt"):
        shutil.copy2(repo / name, output / name)
    forbidden = {".xbe", ".iso", ".xiso", ".bik", ".dat", ".xbx", ".zbg", ".zlh", ".xbt", ".txb", ".wav", ".wma"}
    inventory = []
    for path in sorted(output.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(output).as_posix()
        if path.suffix.lower() in forbidden or "/src/recomp/gen/" in "/" + relative:
            raise RuntimeError(f"Release contains a local game artifact: {relative}")
        if path.suffix.lower() in (".exe", ".dll", ".lib", ".pyd") and path != exe:
            raise RuntimeError(f"Release contains an unexpected binary: {relative}")
        if path.suffix.lower() in (".png", ".ico") and relative not in (
            "launcher/assets/city-backdrop.png", "launcher/assets/resqueezed.png", "launcher/assets/recomp.ico",
            "source/ReSqueezed/assets/recomp.ico"):
            raise RuntimeError(f"Release contains unreviewed artwork: {relative}")
        inventory.append({"path": relative, "bytes": path.stat().st_size,
                          "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    (output / "PUBLICATION.json").write_text(json.dumps({
        "description": "Launcher and build sources; game translation is generated locally from the selected disc.",
        "excluded": ["retail assets", "disc images", "translated game code", "game executables",
                     "retail branding", "screenshots", "saves", "downloaded dependencies"],
        "files": inventory,
    }, indent=2) + "\n", encoding="utf-8")
    archive = output.with_suffix(".zip")
    temporary = archive.with_suffix(".zip.tmp")
    with ZipFile(temporary, "w", ZIP_DEFLATED, compresslevel=6) as zip_file:
        for path in sorted(output.rglob("*")):
            if path.is_file():
                zip_file.write(path, Path(output.name) / path.relative_to(output))
    temporary.replace(archive)
    print(f"Built {archive} with {len(inventory)} reviewed files.")


if __name__ == "__main__":
    main()
