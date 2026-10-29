# LUZ Civic Terminal

LUZ manages Nivalis Nights mods, profiles, BepInEx setup and protected save timelines. Its **CorpSec Terminal** workspace helps mod authors turn their build folders into release packages with optional LUZ recovery support.

**Version 0.8.9.** On Windows, the LUZ Nexus page's **Mod Manager Download** sends the package to Vortex; the LUZ for Nivalis Nights extension installs, pairs and registers it automatically. Manual platform ZIPs remain available from [GitHub Releases](https://github.com/Hvizeu/LUZ-Civic-Terminal/releases/latest). Linux x64, macOS Intel and Apple Silicon builds are experimental.

## Start here

| What you want to do | Guide |
| --- | --- |
| Install LUZ and your first mods | [Quick start](QUICK%20START.txt) |
| Manage profiles, downloads, updates and backups | [Player guide](PLAYER-GUIDE.md) |
| Add your mod, generate support and build a ZIP | [CorpSec step-by-step guide](CORPSEC-GUIDE.md) |
| Use protected saves, restore features or recover a timeline | [Mod Continuity](Continuity/README.md) |
| Build LUZ or automate author releases | [Building and command line](BUILDING.md) |
| Read what changed | [Release notes](RELEASE-NOTES.md) and [changelog](CHANGELOG.md) |

All four desktop ZIPs include these guides. LUZ includes .NET; users do not need to install it separately.

## Civic Terminal for players

- **Profiles and settings:** import mod ZIPs, DLLs or an existing installation; enable, disable, search, filter and reorder mods; copy profiles and keep their settings.
- **Dependency checks:** inspect declared requirements and conflicts, sort dependencies and preview the files that Apply will deploy. LUZ 0.8.7 reads Nivalis Expanded's compiled requirement `hvizeu.nivalis.unofficialpatch >=0.4.12 <0.5.0` for Nexus and local imports; missing, disabled, out-of-range or duplicate copies remain blocking.
- **Downloads and updates:** browse Thunderstore, check mod updates, pin versions and import manual Nexus downloads. Nexus lookup and permitted direct downloads use your own API key.
- **One-click Nexus/Vortex setup on Windows:** manage Nivalis Nights in Vortex once, then choose **Mod Manager Download** on the LUZ Nexus page. The LUZ extension recognizes the versioned LUZ package marker, installs LUZ as an isolated tool, pairs it and makes it primary automatically. After that, update a mod and press Vortex Play; LUZ synchronizes, applies when safe, and stays open without launching Nivalis.
- **BepInEx setup:** install or update to the latest official Windows x64 IL2CPP build, with matching loader backups and restore.
- **Compatibility guidance:** check compiled references before Apply and Play. Warnings explain the affected mod, the uncertainty and the next action. An accepted warning is remembered only for the same inspected action and setup.
- **Recovery and diagnostics:** restore profile or loader backups, recover interrupted operations, diagnose game-update errors and refresh stale generated bindings.
- **Object Studio collections:** receive a project through Studio's **Send to LUZ** workflow, then apply it with the required Object Framework Runtime.
- **LUZ updates:** download and verify platform releases from GitHub. Update validation follows the destination release's Continuity requirements and checks additional declared files. Startup checks and automatic installation have separate preferences. See the [manual recovery steps](PLAYER-GUIDE.md#10-update-luz) if an older updater rejects a complete package.

**Import** adds a package to LUZ. **Apply profile** installs the selected profile's enabled files. **Play Nivalis** starts the game. Import your existing installation before the first Apply if you want to keep those mods.

## CorpSec Terminal for mod authors

Choose **Add mods folder** once and select the folder where your builds are written. CorpSec remembers your mods, the build edition to follow, author details, support decisions and output folder. Later releases reuse those settings.

The four sections follow the release process:

1. **Project:** review the detected plugin and current build; enter author details and a support contact.
2. **LUZ Support:** analyze the build, review existing declarations and answer the remaining questions about saved data.
3. **Validation:** check package contents and recovery declarations. Expand a result for details and required actions.
4. **Package:** choose normal or recovery support, review the file list and build a Nexus/manual ZIP, Thunderstore ZIP or both.

CorpSec inspects DLL metadata, assets, existing support files and optional source without executing mod code or build scripts. It preserves author decisions during rescans. Existing maintained integrations can retain their recovery identity; independent mods can supply their own declarations without a central approval or catalogue edit.

Generated support lives in `LUZ/mod-support.json` beside the mod DLL and binds its declarations to the final files' hashes. The original DLL is unchanged. Ordinary file rules cover supported per-slot save/configuration companions. Custom recovery behavior still needs an implementation supplied by the author.

**Package checks passed**, **Recovery declaration checks passed** and **In-game recovery has not been tested** describe different results. Generation does not certify gameplay. A normal package can be exported while recovery is incomplete, provided the selected binary does not directly require LUZ.

The [CorpSec guide](CORPSEC-GUIDE.md) includes the complete first release, subsequent releases, removal from My mods, common errors, custom adapters and build automation.

## Protected saves

**Mod Continuity** is optional. On a supported setup, LUZ creates a verified original checkpoint and a separate protected working timeline. Manual saves, quicksaves and autosaves use that timeline. Declared companion files travel with it.

Supported features can preserve dormant data while unavailable and validate restoration when a compatible implementation returns. Original checkpoints and retained timelines remain available. Interrupted saves retain recovery records instead of silently continuing.

Protected sessions require **native Windows**, a catalogued Nivalis Nights build (**26295359** or retained recovery identity **25984046**), **BepInEx 6.0.0-be.788**, and compatible mod declarations or maintained integrations. LUZ checks the exact native binary, metadata and loader identities. It cannot make an arbitrary mod or unknown game build safe by generating a descriptor. Wine/Proton protected sessions are unsupported.

See [Mod Continuity](Continuity/README.md) before enabling protection or removing a mod used by your save.

## Platform and download requirements

| Download | Desktop requirements |
| --- | --- |
| `win-x64` | Windows x64 |
| `linux-x64` | Graphical glibc Linux x64, X11/XWayland and `xdg-open` |
| `osx-x64` | macOS 15 or newer, Intel processor |
| `osx-arm64` | macOS 15 or newer, Apple Silicon |

Extract the complete ZIP outside the game. Run **Install LUZ.exe** on Windows, **Install LUZ** on Linux or the supplied preparation and installation helpers on macOS. Portable use is available by opening **LUZ Civic Terminal** directly from the extracted folder. macOS packages are not notarized.

Nivalis remains a Windows game on every host. Linux needs an existing Steam Proton setup; macOS needs an existing Wine/CrossOver setup. LUZ does not install those tools. Use Windows x64 IL2CPP BepInEx for Nivalis on every host. See the [player guide](PLAYER-GUIDE.md) for launcher and loader settings.

## Help and support

For a problem report, include the LUZ version, operating system, exact steps and full error. **Maintenance > Export diagnostics** creates a support ZIP; review it before sharing.

Supported imports are BepInEx ZIPs and individual DLLs. RAR/7z, FOMOD installers, Nexus Collections, arbitrary game-file replacements and other mod frameworks are outside this release's scope.

Made by Hvizeu. LUZ is an independent, unofficial project. Required third-party licences and notices are included in the download.

[Support my work on Ko-fi](https://ko-fi.com/henriquevizeu).
