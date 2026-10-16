# LUZ Civic Terminal

## Changes in 0.5.0

- Bundles Mod Continuity adapters with LUZ's GitHub updates.
- Adds verified save checkpoints and separate protected timelines for manual saves, quicksaves and autosaves.
- Preserves declared data when a supported feature is suspended and validates restoration when its compatible mod returns.
- Adds interrupted-save recovery, original-checkpoint restoration and selection of retained timelines.
- Can automatically install verified LUZ updates at startup, with preferences in Maintenance.

Protected saves are opt-in and require continuity-compatible mod builds. Initial support is native Windows, Nivalis Nights build 25984046 and BepInEx 6.0.0-be.788. Existing standalone mod profiles can continue without enabling protection. See [Mod Continuity](Continuity/README.md) for setup and coverage.

## Changes retained from 0.4.9

- Checks compiled references and declared native contracts before Apply and Play, including generic inheritance and forwarded types. Unknown dynamic contracts remain visibly unverified.
- Limits compatibility acknowledgments to one attempt against the exact report. Save integrity and ownership failures cannot be overridden.
- Requires recovered save files, companions, receipts, journals and committed checkpoints to agree before admitting a recovered timeline.
- Preserves archived recovery files without repeatedly hashing their payloads during ordinary compatibility checks.
- Retains scanner cancellation, timeouts and bounded inputs without process RAM ceilings.

## Changes retained from 0.4.7

- Filter the Registry by All, Enabled or Disabled. Filters combine with search; clear them before reordering.
- DLL/ASI companions stay together in supported archives. Mixed-layout archives cannot silently omit native binaries.
- Apply identifies unmanaged DLL/ASI files before replacement. Import the complete mod archive or use Import existing installation to adopt them.
- Registry startup failures show the data location, diagnostics export and restoration from a validated existing backup. Damaged registries are preserved.
- Self-update checks the persisted registry before installation.
Installing and updating Nivalis mods has been more confusing than it should be. I made LUZ to put BepInEx setup, mod installation and profiles in one place. Import the mods you already use, add new ones, then apply the setup you want to play with.

Version 0.5.0 provides downloads for Windows x64, with experimental Linux x64, macOS Intel and Apple Silicon downloads.

## What you can do

- Install the recommended BepInEx build from its official server.
- Keep separate profiles with their own enabled mods, order and settings.
- Import ZIPs, individual DLLs or the mods already installed in your game.
- Browse Thunderstore and check installed mods for updates.
- Look up Nexus files with your own API key, or import manual downloads without a key.
- Reorder mods, sort dependencies and review missing requirements or declared conflicts.
- Import complete Object Studio collections through Send to LUZ.
- Restore mod/settings and loader backups after a failed change.
- Diagnose a game update and refresh stale BepInEx bindings before your next launch.
- Check for LUZ updates and install the matching platform release from Maintenance.

## Updated guides in 0.4.6

The bundled guides now cover Nivalis Update #4 and Object Framework Runtime 0.3.8. They explain profile settings, installation, updates and recovery. This release retains the application behaviour introduced in 0.4.5.

## Moving the game in 0.4.5

After moving Nivalis with Steam, use Maintenance > Detect Steam install or Choose game folder. Confirm the old and new locations. LUZ keeps your profiles and connects their restore points to the confirmed installation. Complete any pending recovery before applying again.

Moving the game no longer clears cached settings when the new configuration folder is missing. File-access errors identify the affected path, and Maintenance and diagnostics remain available if an installation check fails.

Updating to 0.4.5 upgrades the profile registry and backs up the previous file. Older LUZ versions cannot open the upgraded registry. Keep using 0.4.5 or newer after updating; do not mix files from different application versions.

## Choose your download

Windows x64 is the main desktop download. Linux x64 requires a graphical glibc desktop with X11/XWayland and xdg-open. macOS downloads require macOS 15 or newer; choose Intel or Apple Silicon to match your Mac.

Extract the whole platform ZIP outside the game. It includes .NET; no separate .NET installation is needed. On Windows, run Install LUZ.exe and choose Install and create shortcut, then open the shortcut. On Linux, run ./"Install LUZ"; if necessary, make Install LUZ and LUZ Civic Terminal executable first. On macOS, run Prepare macOS.command, then Install LUZ.command. The macOS application uses a local signature and is not notarized. Follow the included QUICK START for platform details.

For portable use, open LUZ Civic Terminal directly from the extracted folder. Keep its accompanying files together.

## First setup

1. Close Nivalis and open Maintenance. Check the detected game folder, or choose the folder containing Nivalis Nights.exe.
2. Already using mods? Select the intended profile and use Import existing installation before the first Apply. This copies the current plugins, patchers and settings into LUZ.
3. Use Install recommended BepInEx if needed. LUZ downloads the Windows x64 IL2CPP loader and backs up replaced loader files. After first-time setup, start the game yourself once, let initialization finish, then close it.
4. Import mod ZIPs through Registry > Import ZIP / DLL, or choose packages in Catalogue. Enable the desired mods and their dependencies.
5. Review Profile issues and use Auto-sort if needed. With the game closed, Apply profile installs its enabled contents. Importing alone does not install a mod.
6. Choose Play Nivalis when you want to start the game. Opening LUZ, importing a package and applying a profile do not start the game.

Apply replaces deployed mods with the selected profile's contents. Import your existing installation first if you want to keep it. LUZ reads the mods deployed in the game, rather than another manager's inactive profiles.

## Profiles and updates

New creates an empty profile. Copy includes the current mods and settings. Removing a mod from a profile keeps it in the library; use Profile tools > Add from library to reuse it. Drag the handles or use Move up/down to change order; lower rows win shared-file conflicts. Auto-sort follows declared dependencies. These checks identify declared problems, but cannot establish compatibility between arbitrary mods.

Open profile configuration opens live settings for the applied profile and stored settings for an inactive one. LUZ captures the latest settings when switching profiles. Close the game before copying an applied profile.

Check updates finds available mod versions. Review and import them, then Apply with the game closed. Pin this version excludes a mod from update checks. Profile exports contain the ordered mod list and version references; they do not contain mod files, settings, API keys or saves.

Maintenance > LUZ updates checks the public GitHub releases. Startup checks run at most once every 24 hours and can be disabled. LUZ 0.5.0 can automatically install verified updates on startup; disable that preference to choose installation manually. Updates install for your account and redirect the shortcut while retaining profiles. A failed update leaves the current application available. Users on an older release can use Check LUZ updates and install 0.5.0 once to enable the new startup installation option.

## Compatibility checks

Profile checks inspect compiled mod contracts without executing inspected assemblies. Apply checks the effective profile files; Play checks installed files. Results distinguish checked contracts passed, incompatible and unverified. Binding provenance must match recognized loader inputs; timestamps or version labels alone do not establish compatibility.

An explicit acknowledgment permits one attempt against the exact report, and changes to relevant files invalidate it. File-safety, ambiguous ownership, interrupted operations and save-integrity failures remain blocking. Static inspection cannot guarantee gameplay compatibility or repair a third-party mod.

## Nexus and Thunderstore

On Windows, Remove LUZ as NXM manager is beside Change system-wide NXM handler. Removal preserves your profiles, mods and other managers. Choose your preferred replacement in Windows Default apps. The extracted installer also offers registration removal without reinstalling LUZ.

Catalogue downloads Thunderstore packages with their declared dependencies. BepInEx setup uses the dedicated loader controls. LUZ also imports manual Nexus ZIP downloads without an API key.

For Nexus lookup and direct downloads, add your own personal key under Maintenance > Nexus connection. Downloads follow your account's permissions. Saved keys use the operating system's credential storage; Use for session is available. Linux persistent storage needs secret-tool and an unlocked desktop keyring. Nexus dependencies must be installed separately.

When a Nexus page offers Mod Manager Download, LUZ can receive its NXM link. Registering LUZ changes the system-wide handler for Nexus links across all games, while LUZ supports only Nivalis Nights. If you use another manager for other games, keep it as the default and use Paste download link or manual ZIP import here. Pasting a link does not change the handler.

## Object Studio collections

In Studio, choose Send to LUZ to build and send the open project. Confirm the displayed profile, install the matching Object Framework Runtime and Apply with the game closed. Use Runtime and Studio SDK versions compatible with your installed game build. Rebuilding updates the same project; disabling it removes the collection on the next Apply. Saves containing custom objects still require their packs.

## After a game update

Use Maintenance > After a game update > Diagnose game update. It shows the installed build and relevant errors from the saved loader log, which may belong to an older launch.

If generated bindings are stale, close the game and choose Refresh game bindings. LUZ backs them up and enables regeneration at your next normal launch. A mod reporting an unsupported game build still needs a compatible mod release; refreshing bindings does not bypass its requirement.

## Backups and recovery

Each Apply creates a mod/settings restore point. Maintenance > Restore profile backup restores one; BepInEx has a separate Restore loader backup action. If Apply or Play says Recovery needed, use the matching action at the top of Maintenance. Export recovery diagnostics is available when a backup is missing or unreadable.

Profile restore points contain mods and settings. Mod Continuity separately protects saves after it is enabled. Maintenance > Prepare saved-data recovery provides the existing recovery route for supported retired worker records: it captures a verified source save and matching companions while the game is closed. That recovery requires compatible Unofficial Patch and Object Framework components, a separate game launch and the explicit recovery action; it creates a separate recovered timeline. Unsupported companions or uncertain writes block completion. Profiles and the library stay in the platform's LUZCivicTerminal data folder when updating. Windows uses %LOCALAPPDATA%/LUZCivicTerminal; Linux uses $XDG_DATA_HOME/LUZCivicTerminal or ~/.local/share/LUZCivicTerminal; macOS uses ~/Library/Application Support/LUZCivicTerminal. Keep that folder when updating.

## Linux and macOS game setup

Nivalis is a Windows game. Linux requires working Steam Proton; macOS requires a working Wine/CrossOver setup. LUZ does not install those tools. Use Windows x64 IL2CPP BepInEx for the game on every host.

See the [official BepInEx Proton/Wine instructions](https://docs.bepinex.dev/articles/advanced/proton_wine.html). For Proton, merge WINEDLLOVERRIDES="winhttp=n,b" %command% into the game's Steam Launch Options. For Wine/CrossOver, set winhttp to native, then builtin in the game's bottle. On macOS, configure Maintenance > Game launch with your working launcher executable or script. Enter one literal argument per line. Linux also supports custom launchers such as an existing Flatpak Steam setup.

## Help and limits

Supported imports are BepInEx ZIPs and individual DLLs. RAR/7z, FOMOD installers, arbitrary game-file replacements, Nexus Collections and other mod frameworks are outside this release's scope.

For a problem report, include the operating system, LUZ version, exact steps and error. Maintenance > Export diagnostics creates a support ZIP; review it before sharing. QUICK START and README contain the detailed walkthrough and data locations.

Made by Hvizeu. LUZ is an independent, unofficial project. Required third-party licenses and notices are included in the download.

[Support my work on Ko-fi](https://ko-fi.com/henriquevizeu).

## 0.4.9

Compatibility inspection supports embedded contract schema 2 and existing schema 1 packages, including transitive generic inheritance and assembly-qualified signatures. Unknown requirements remain Unverified. Overrides permit one attempt against the exact inspected deployment and cannot bypass save integrity or cleanup failures. Recovery inspection checks durable publication and referenced native generations without repeatedly hashing archived payloads. Interrupted publication remains blocked until verified recovery. Scanner cancellation, timeout and bounded inputs remain enforced; no process RAM ceiling is imposed.
