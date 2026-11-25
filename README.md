# ReSqueezed

<a href="https://recomplauncher.com/game/resqueezed"><picture><source media="(prefers-color-scheme: dark)" srcset="https://recomplauncher.com/assets/press/launcher/badge-light.svg"><img alt="Get it on RecompLauncher" src="https://recomplauncher.com/assets/press/launcher/badge-dark.svg" height="48"></picture></a>

An unofficial Windows x64 launcher and compatibility runtime for the original Xbox retail release of Juiced. Big thanks to sp00nznet for XboxRecomp!

## Play with RecompLauncher

1. Install ReSqueezed from the catalog and choose your Juiced Xbox USA / Europe retail ISO or extracted disc folder in **Easy Setup**.
2. Run setup to download the verified build tools and build ReSqueezed locally. The game can play from the files it kept after setup.
3. Use **Play** to start the game directly. RecompLauncher exposes aspect ratio, window mode, opening movies, saves and logs.

Easy Setup checks for default.xbe. ReSqueezed then verifies the executable against the hashes below. The optional whole-disc verification badge uses the size and CRC32 of the [Redump USA / Europe ISO](https://data.spludlow.co.uk/redump/redump/Microsoft%20-%20Xbox/Juiced%20%28USA%2C%20Europe%29%20%28En%2CFr%2CDe%2CEs%2CIt%29). Rebuilt XISOs can still pass the executable check with a different disc CRC.

## Build your game

1. Extract the complete release ZIP and run **ReSqueezed Launcher.exe**.
2. Open **Build**, select your own Juiced Xbox ISO or extracted disc folder, and choose an installation folder. It defaults to the launcher's folder.
3. Choose **Build**. First-time setup downloads approximately 244 MiB of tools from their official publishers and verifies each archive's pinned SHA-256. Later builds reuse the installed tools.
4. Choose **Play**. Settings supports 16:9 widescreen and 4:3, with the existing performance improvements. Saves supports career backups and exports. Mods supports Faster startup.

The launcher accepts the shared USA / Europe retail executable, including English, French, German, Spanish, and Italian. It validates the executable directly inside the ISO before downloading tools or extracting files. Extracted folders receive the same checks. Rebuilt XISOs can have different whole-disc checksums, so validation uses the contained executable.

| Item | Expected value |
| --- | --- |
| Title ID | 545100EF |
| Executable size | 3,518,464 bytes |
| Executable MD5 | 3F4399A5947E98B9681DDF4F08241A56 |
| Executable SHA-256 | 90912DE89F5A105D15568D38A0B5901962EC4DF9D41D99B6551A99FE947462A2 |

The MD5 matches the shared retail release listed by [MobCat's Xbox title database](https://mobcat.zip/XboxIDs/). The local build also enforces SHA-256. Other revisions require their own compatibility verification.

A successful build installs the extracted game, ReSqueezed.exe, Play ReSqueezed.exe, SDL, and a build log. Compiled game code is generated locally from the selected executable. Compatibility patches preserve the tested menu, display, graphics, performance, and controller fixes. Xbox controllers use XInput. DualShock 4 and DualSense use SDL directly and display the corresponding button symbols.

## Update an existing install

In RecompLauncher, install the update and choose **Build Now** when prompted. Setup rebuilds from the retained disc files. Your existing saves, backups, Faster startup setting and logs stay in the installation folder.

For a manual update, extract the new ZIP into the same folder as your current **ReSqueezed Launcher.exe**, replacing the release files when prompted. Open the launcher and choose **Update**. Close the game before updating. The launcher replaces the game executable only after the build succeeds.

Version 0.2a restores short crew-driver callouts and Respect dialogue and fixes the Workshop Dynamometer data display.

## Downloads

launcher/dependencies.json pins the URLs, sizes, and SHA-256 hashes for Python, Capstone, LLVM-MinGW UCRT, CMake, Ninja, extract-xiso, and SDL. Downloads use HTTPS. Files are installed through temporary directories after checksum verification. Failed downloads can be retried by choosing Build. The first build requires an internet connection.

Game content comes from the user's selected disc. Supply a complete extraction containing default.xbe, audio, cars, movies, tracks, and the root resource archives.

## Publication

Use this package as the repository root. PUBLICATION.json records the release inventory. The included source trees contain launcher code, the patched open-source runtime, the recompiler, address and ABI metadata, and compatibility patch instructions. The launcher uses the supplied ReSqueezed logo, an RS icon, and the generated city backdrop.

Local game data, generated game translations, compiled game executables, retail artwork, texture dumps, videos, screenshots, and saves are excluded from the release. The .gitignore covers local builds and downloaded tools. After playing or building in this folder, create releases with launcher/package.py so only the reviewed source inputs enter the ZIP.

To build the launcher itself on Windows, use Python 3.10 or newer and run:

    python launcher/package.py

This compiles the launcher using the Windows .NET Framework C# compiler and writes a clean package under dist. The release includes the compiled launcher, so players can start without installing Python or build tools themselves.

For an unattended game build:

    "ReSqueezed Launcher.exe" --build "path-to-disc.iso" "path-to-install"

Third-party attribution and licenses accompany the source trees. See THIRD-PARTY-NOTICES.txt.

### Legal Disclaimer

ReSqueezed is an independent, unofficial recompilation project intended to enable owners of the original *Juiced* game to run it on modern systems.

**No original game assets are included or distributed with this project.** Users must provide their own legally obtained game files.

ReSqueezed is not affiliated with, endorsed by, or sponsored by the original game's developers, publishers, or respective rights holders.

The project's original source code is distributed under the MIT License. This license does not grant rights to the original game or its intellectual property.
