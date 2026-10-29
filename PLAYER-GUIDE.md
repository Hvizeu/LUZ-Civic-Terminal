# Player guide

This guide matches LUZ 0.8.7. Start with [QUICK START.txt](QUICK%20START.txt) for installation. Mod authors should use the separate [CorpSec guide](CORPSEC-GUIDE.md).

## 1. Connect your game and existing mods

1. Close Nivalis and open **Civic Terminal > Maintenance**.
2. Check **Game location**. Use **Detect Steam install** or **Choose game folder** to select the folder containing `Nivalis Nights.exe`.
3. Select the profile you want to use. If the game already has mods, choose **Import existing installation** before the first Apply.
4. Confirm the import. Expected: installed plugins, patchers and configuration are copied into LUZ; the game files remain in place.

LUZ adopts the installation currently deployed in the game. It does not import another manager's inactive profiles.

If you move Nivalis with Steam later, select the new location in Maintenance and confirm that it is the same installation. LUZ keeps profiles and reconnects their restore points. Finish any reported recovery before applying again.

## 2. Install BepInEx

1. In Maintenance, choose **Install or update latest BepInEx**.
2. Review the latest official Windows x64 IL2CPP build LUZ found and confirm. LUZ downloads that build from the official server and backs up replaced loader files. A failed lookup stops the operation instead of installing an older fallback.
3. On a first-time setup, launch the game yourself once, wait for loader initialization, then close it before applying mods.

The recommended loader is always the latest completed official Windows x64 IL2CPP build. A failed check does not fall back to an older version. Use the BepInEx controls for loader ZIPs rather than importing them as mods. **Restore loader backup** reverses a loader installation using its matching backup.

## 3. Import and apply a mod

1. Download the complete Nivalis mod ZIP and read its requirements.
2. Choose **Registry > Import ZIP / DLL**, select the ZIP and confirm the destination profile.
3. Import its required dependencies. Enable the mods you want.
4. Review **Profile issues** and use **Auto-sort** if needed.
5. With the game closed, choose **Apply profile** and review the change.
6. Expected: Registry shows the chosen versions and the profile as applied to the game.
7. Choose **Play Nivalis** when ready.

Import adds a package to LUZ's library/profile. Apply replaces deployed plugins and patchers with the selected profile's enabled contents. A DLL alone can be missing assets or support files from its original ZIP.

LUZ 0.8.7 reads Nivalis Expanded's hard dependency from its compiled plugin metadata. The Update #6 universal package is valid when exactly one enabled plugin has GUID `hvizeu.nivalis.unofficialpatch` and version `>=0.4.12 <0.5.0`. A missing, disabled, out-of-range or duplicate Unofficial Patch remains a profile error. Storefront package dependencies used by unrelated mods retain their normal package checks.

Lower rows in the deployment order win shared-file conflicts. Drag the dotted handles or use **Move up** and **Move down**. Auto-sort follows declared dependencies; BepInEx controls plugin startup order.

## 4. Organize profiles

- **New** creates an empty profile. **Copy** keeps the selected profile's mods and settings.
- **Active profile** changes the selection. Apply makes its contents live in the game.
- Registry search combines with the **All**, **Enabled** and **Disabled** filters. Clear filters before reordering.
- Use the selected mod's edit menu to remove it from the profile. It remains available under **Profile tools > Add from library**.
- **Open profile configuration** opens live settings for the applied profile and stored settings for an inactive one. LUZ captures current settings when switching profiles.
- Profile exports contain the ordered mod list and version references. They do not contain mod payloads, settings, API keys or saves.

Close the game before copying an applied profile. Removing a content mod does not remove its objects safely from every save. Use its author's instructions or supported Mod Continuity preservation.

## 5. Download and update mods

**Catalogue** browses Thunderstore and imports packages with their declared dependencies. **Check updates** finds available versions; review and import them, then Apply with the game closed. **Pin this version** excludes a mod from update checks.

Manual Nexus downloads need no API key: download the ZIP in your browser and import it. For Nexus lookup and permitted direct downloads, use **Maintenance > Nexus connection** with your own personal key. Account permissions still apply. Install Nexus dependencies separately.

Saved keys use the operating system's credential store. **Use for session** avoids persistent storage. Linux persistent storage needs `secret-tool` and an unlocked desktop keyring.

### Nexus browser links

When a page offers **Mod Manager Download**, LUZ can receive its NXM link. Registering LUZ changes the system-wide Nexus handler for all games, while LUZ supports only Nivalis Nights.

If another manager handles your other games, keep it as the default and use **Paste download link** or manual ZIP import in LUZ. Pasting a link does not change the handler. LUZ cannot add a missing Mod Manager Download button to a Nexus page.

On Windows, **Maintenance > Browser downloads > Remove LUZ as NXM manager** removes LUZ's registration while preserving mods and profiles. Choose a replacement through **Open Windows Default apps**, or use the replacement manager's registration procedure. The extracted installer also provides registration removal.

### Automatic Vortex integration on Windows

The **LUZ for Nivalis Nights** extension requires Vortex 2.9.0 or newer. It owns the existing `nivalisnights` game identity and includes the LUZ handoff; there is no competing bridge extension. Vortex must have this support before it can recognize the LUZ archive as an application instead of an ordinary mod.

1. Choose **Manage** for Nivalis Nights so Vortex obtains the LUZ extension and finds Steam.
2. On the LUZ Nexus page, choose **Mod Manager Download**, then install the downloaded file in Vortex.
3. Expected: Vortex recognizes the official LUZ package, installs it in its isolated tool area, pairs it to the detected game and makes it primary. No LUZ folder selection or Maintenance connection is required.
4. Manage Nexus mod downloads and updates in Vortex.
5. Press Play in Vortex. Expected: Vortex publishes a complete exchange snapshot, starts or activates LUZ with that request, and does not launch Nivalis.
6. Read the LUZ result. **Applied** means the captured profile passed compatibility checks and the normal deployment transaction. **NeedsAttention** means LUZ synchronized what it could but needs profile, compatibility or package review. **RecoveryRequired** retains the existing recovery journal.

If the older extension left a direct Vortex deployment in the game folder, use **Purge Mods** once when prompted and choose Manage again. This is the only migration-only step; existing package staging is retained.

The extension sends every installed Vortex package, including packages disabled in a Vortex profile. LUZ's switches control gameplay. New supported packages enter the selected LUZ profile enabled. Existing order, disabled states, pins and notes stay intact. A pinned update is reported but not selected. Removing or disabling a package in Vortex leaves its imported LUZ package and profile entry in place; LUZ marks the update source unavailable.

LUZ does not auto-apply a first deployment, a profile with unrelated pending edits, a new compatibility warning, a running game or an outstanding recovery. Review those cases in LUZ. Repeating an unchanged snapshot does not create another profile entry, deployment or restore point. **Disconnect** stops future synchronization and keeps imported packages and profiles.

## 6. Read a compatibility warning

Apply checks the proposed profile files; Play checks the installed files. LUZ inspects compiled references and available declarations without executing the mod.

| Result | What to do |
| --- | --- |
| Checked contracts passed | The inspected requirements match. Continue with your normal gameplay checks. |
| Unverified | Read the affected mod and missing information. Use its support contact, ask the author for compatible LUZ support, or decide whether to continue with that exact warning. |
| Incompatible | Install a mod or dependency release for your game build. Read the stated mismatch before considering an offered override. |
| Recovery or integrity failure | Use the matching Maintenance recovery action. This cannot be overridden as an ordinary compatibility warning. |

An explicit warning decision is remembered for the same action and inspected setup. Changes to relevant game, loader, mod, profile or configuration files require a fresh decision. An accepted warning does not certify the mod or bypass protected-save admission.

## 7. Receive an Object Studio collection

1. In Object Studio, open your project and choose **Send to LUZ**.
2. Confirm the displayed destination profile.
3. Install the matching Object Framework Runtime and enable the required collection.
4. Apply with the game closed. Expected: the collection appears in the applied profile.

Rebuilding the same Studio project updates that collection. Use Framework and Studio versions compatible with your game build; the Studio SDK is a separate authoring application, not a ZIP to install as a gameplay mod. Saves containing custom objects still need their supporting packs.

## 8. Recover from an interrupted change

Each Apply creates a mod/settings restore point. Use **Maintenance > Restore profile backup** for those files. Loader installations have a separate **Restore loader backup**.

If Apply or Play reports **Recovery required**, read the action at the top of Maintenance:

1. Use **Recover profile deployment** for an interrupted profile change.
2. Use **Recover BepInEx installation** for an interrupted loader change.
3. If both appear, complete both using their matching backups.
4. If recovery cannot use its backup, choose **Export recovery diagnostics** and retain the failed files for support.

Profile backups cover mods and settings. Optional [Mod Continuity](Continuity/README.md) separately checkpoints saves and declared companions. These are different recovery operations.

**Prepare saved-data recovery** handles supported retired-worker records and a validated Unofficial Patch manager-companion revision mismatch. It always creates a separate recovered timeline and requires an explicit game-side recovery action after preparation. Retired-worker/table recovery requires matching Unofficial Patch and Object Framework components. Manager-only repair requires Unofficial Patch 0.4.15, preserves native progress, and resets patch-owned manager/accounting, Inspiration and restaurant-review records. Use **Restore interrupted saved-data recovery** for an interrupted operation from that route. Neither button is a general repair for arbitrary save corruption.

## 9. After a game update

1. Open **Maintenance > After a game update > Diagnose game update**.
2. Read the detected build and loader errors. The saved log may describe an earlier launch.
3. If generated bindings are stale, close the game and choose **Refresh game bindings**.
4. LUZ backs up those files and enables regeneration at your next normal launch.

A mod that rejects the new game build needs a compatible release. Binding regeneration does not qualify unknown native behavior or bypass the mod's guard.

## 10. Update LUZ

Use **Maintenance > LUZ updates > Check LUZ updates**. Startup checks run at most once every 24 hours. Automatic startup installation is a separate preference.

LUZ checks the platform download's SHA-256 and version before installing it to a per-user version folder and updating the shortcut. Profiles, library contents and CorpSec settings remain in the separate data folder. Updating LUZ does not launch Nivalis.

LUZ 0.4.0 and newer can use the updater. Users on 0.3.x need one manual installation. Older Continuity-aware versions may reject a complete newer package with **Continuity bundle is incomplete** because their validator expects an older file set. Updating the new release cannot change that already-installed validator.

If that happens:

1. Note the LUZ version shown in the application and your operating system. Close LUZ normally.
2. Download the complete matching platform ZIP from the [official GitHub release](https://github.com/Hvizeu/LUZ-Civic-Terminal/releases/latest), and extract it into a fresh folder outside Nivalis and outside the old LUZ application folder.
3. Run **Install LUZ.exe** on Windows, **Install LUZ** on Linux, or the supplied preparation and install helpers on macOS. The installer creates a separate version folder and updates the shortcut. Portable users can instead open **LUZ Civic Terminal** from the complete newly extracted folder.
4. Open the new version and check the displayed version, selected profile, enabled mods and pending recovery actions before applying changes. The library and profiles should be retained from the separate data folder listed below.

Keep your LUZ data folder, save files, configurations and recovery records. There is no need to uninstall LUZ or delete its library to complete this update. Do not mix executable files from different releases or downgrade the library to an older reader. If the new version still reports missing or mismatched files, retain the exact error and export diagnostics; do not clear recovery records.

## Linux and macOS

Linux needs a graphical glibc x64 desktop with X11/XWayland and `xdg-open`. Alpine/musl is unsupported. macOS needs version 15 or newer and the matching processor download. Cross-built Mac packages include **Prepare macOS.command** for a local signature; they are not notarized.

Nivalis remains a Windows game. Linux needs a working Steam Proton setup; macOS needs a working Wine/CrossOver setup. Use Windows x64 IL2CPP BepInEx on both. See the [BepInEx Proton/Wine instructions](https://docs.bepinex.dev/articles/advanced/proton_wine.html).

For Proton, merge this into the game's Steam Launch Options:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

For Wine/CrossOver, set `winhttp` to native, then builtin in the game's bottle. Under **Maintenance > Game launch**, select your working launcher executable or script and enter one literal argument per line. Linux also supports an existing custom launcher such as Flatpak Steam.

Protected save sessions currently require native Windows; ordinary mod management remains available on the other desktop platforms.

## Data locations and support

| Platform | LUZ data folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%/LUZCivicTerminal` |
| Linux | `$XDG_DATA_HOME/LUZCivicTerminal`, or `~/.local/share/LUZCivicTerminal` |
| macOS | `~/Library/Application Support/LUZCivicTerminal` |

Keep this folder when updating or uninstalling if you want to retain profiles, packages, author projects and recovery history. Your last applied game files can remain installed when you stop using LUZ.

For support, record the operating system, LUZ version, steps and exact error. Choose **Maintenance > Export diagnostics** immediately after the problem and review the ZIP before sharing; mod logs can contain personal paths or other data.

## Import recovery

If a previous import or registry restore left an unregistered package folder, retry the original import or Maintenance > Import existing installation. LUZ rebuilds the package from the selected input and keeps the original folder under import-recovery in its data directory. Invalid imports do not replace existing data. This operation preserves profile enable and pin choices; applying the profile is still a separate action.
