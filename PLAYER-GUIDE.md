# Player guide

This guide matches LUZ 0.7.2. Start with [QUICK START.txt](QUICK%20START.txt) for installation. Mod authors should use the separate [CorpSec guide](CORPSEC-GUIDE.md).

## 1. Connect your game and existing mods

1. Close Nivalis and open **Civic Terminal > Maintenance**.
2. Check **Game location**. Use **Detect Steam install** or **Choose game folder** to select the folder containing `Nivalis Nights.exe`.
3. Select the profile you want to use. If the game already has mods, choose **Import existing installation** before the first Apply.
4. Confirm the import. Expected: installed plugins, patchers and configuration are copied into LUZ; the game files remain in place.

LUZ adopts the installation currently deployed in the game. It does not import another manager's inactive profiles.

If you move Nivalis with Steam later, select the new location in Maintenance and confirm that it is the same installation. LUZ keeps profiles and reconnects their restore points. Finish any reported recovery before applying again.

## 2. Install BepInEx

1. In Maintenance, choose **Install recommended BepInEx**.
2. Review and confirm. LUZ downloads BepInEx 6.0.0-be.788, Windows x64 IL2CPP, from its official server and backs up replaced loader files.
3. On a first-time setup, launch the game yourself once, wait for loader initialization, then close it before applying mods.

**Check latest official build** is a separate upstream choice. The recommended loader remains the default. Use the BepInEx controls for loader ZIPs rather than importing them as mods. **Restore loader backup** reverses a loader installation using its matching backup.

## 3. Import and apply a mod

1. Download the complete Nivalis mod ZIP and read its requirements.
2. Choose **Registry > Import ZIP / DLL**, select the ZIP and confirm the destination profile.
3. Import its required dependencies. Enable the mods you want.
4. Review **Profile issues** and use **Auto-sort** if needed.
5. With the game closed, choose **Apply profile** and review the change.
6. Expected: Registry shows the chosen versions and the profile as applied to the game.
7. Choose **Play Nivalis** when ready.

Import adds a package to LUZ's library/profile. Apply replaces deployed plugins and patchers with the selected profile's enabled contents. A DLL alone can be missing assets or support files from its original ZIP.

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

**Prepare saved-data recovery** is the specific recovery route for supported retired worker records. It requires matching Unofficial Patch and Object Framework components and an explicit game-side recovery action after preparation. It creates a separate recovered timeline. Use **Restore interrupted saved-data recovery** for an interrupted operation from that route. Neither button is a general repair for arbitrary save corruption.

## 9. After a game update

1. Open **Maintenance > After a game update > Diagnose game update**.
2. Read the detected build and loader errors. The saved log may describe an earlier launch.
3. If generated bindings are stale, close the game and choose **Refresh game bindings**.
4. LUZ backs up those files and enables regeneration at your next normal launch.

A mod that rejects the new game build needs a compatible release. Binding regeneration does not qualify unknown native behavior or bypass the mod's guard.

## 10. Update LUZ

Use **Maintenance > LUZ updates > Check LUZ updates**. Startup checks run at most once every 24 hours. Automatic startup installation is a separate preference.

LUZ checks the platform download's SHA-256 and version before installing it to a per-user version folder and updating the shortcut. Profiles, library contents and CorpSec settings remain in the separate data folder. Updating LUZ does not launch Nivalis.

LUZ 0.4.0 and newer can use the updater. Users on 0.3.x need one manual installation. For manual updates, close LUZ, extract the whole new package and run its installer. Do not mix executable files from different releases or downgrade a registry to an older unsupported reader.

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
