# ReSqueezed

An unofficial Windows x64 launcher and compatibility runtime for the original Xbox retail release of Juiced.

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
