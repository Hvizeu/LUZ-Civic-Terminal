# LUZ Civic Terminal 0.7.2

This release adds CorpSec Terminal for mod authors alongside the existing Civic Terminal player workspace. It includes the authoring and compatibility improvements made since the public 0.5.0 release.

## CorpSec Terminal

- Add a build folder once and keep your mods in a saved, scrollable My mods list. Follow the intended standalone or recovery edition and retain author choices across releases.
- Analyze DLLs, assets, existing support declarations and optional source without executing mod code or build scripts.
- Review detected saved-data requirements, declare owned files and configure capabilities/adapters through guided controls.
- Generate exact-file LUZ support beside the plugin without rewriting its DLL. Independent authors can supply support without central approval or catalogue changes.
- Build Nexus/manual and Thunderstore ZIPs, review their file list and receive checksums. Finished archives are checked through LUZ's importer and deployment planner.
- Export a normal package while recovery is incomplete, using a standalone binary. Recovery exports require complete supported declarations and implementations.
- Reuse the same workflow through the CLI, JSON results and generated build integration.
- Remove a saved author project without deleting its source, builds or exported packages.

## Compatibility and recovery

- Compatibility warnings identify the affected mod and next action. Accepted warnings are remembered for the same action and inspected setup; relevant changes require a new decision.
- Package-local declarations describe save ownership, adapter identities and preferred/fallback implementations. Resolved support is retained with protected timelines.
- Stateless and ordinary companion-file support no longer demand unrelated native contracts. Native or embedded recovery implementations still require their compiled contracts.
- Saving a support contact refreshes its required action when project details are saved.
- Package checks, recovery declaration checks and in-game recovery status remain separate.

## Guides included in every download

QUICK START.txt covers installation and first use. PLAYER-GUIDE.md covers profiles, downloads, updates and recovery. CORPSEC-GUIDE.md walks through the first package, subsequent releases, owned files, normal/recovery choices, custom adapters and automation. Continuity/README.md explains protected saves and restoration. BUILDING.md documents the CLI and source build.

## Installation

Download the ZIP for Windows x64, Linux x64, macOS Intel or macOS Apple Silicon. Extract the whole archive outside the game and use its installer. .NET is included. Existing users on LUZ 0.4.0 or newer can use Maintenance > LUZ updates; 0.3.x needs a manual installation. Profiles and author settings remain in LUZ's separate data folder.

Windows is the main desktop build. Linux/macOS builds remain experimental; macOS packages are not notarized. Protected game sessions require native Windows, supported Nivalis Nights build 25984046, BepInEx 6.0.0-be.788 and compatible mod support. Ordinary mod management can be used without protected saves.

[Source and guides](https://github.com/Hvizeu/LUZ-Civic-Terminal) | [Support my work](https://ko-fi.com/henriquevizeu)
