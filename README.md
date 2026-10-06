# LUZ Civic Terminal

A mod registry and launcher for Nivalis Nights. Version 0.4.4: Windows x64, with experimental Linux x64, macOS Intel and Apple Silicon downloads.

[Download releases](https://github.com/Hvizeu/LUZ-Civic-Terminal/releases)

## Updating LUZ

LUZ checks its public GitHub release feed on startup, at most once every 24 hours. In **Maintenance > LUZ updates**, you can check immediately or disable startup checks. Updates do not require a GitHub account or key.

When an update is available, choose **Install LUZ**. The terminal downloads the package for your platform, verifies GitHub's SHA-256 digest, checks the application version and platform, installs it for your account, updates the desktop shortcut and restarts LUZ. Profiles and game files remain in place. The current application remains available if the download or installation fails. Portable copies also install updates into the per-user application location; they do not overwrite the portable folder.

Only numbered stable GitHub releases with a matching package and SHA-256 digest are offered. Drafts and prereleases are skipped. A failed check does not block using the launcher. macOS updates apply and verify a local ad-hoc signature; they are not notarized. Native Linux/macOS updating has not been tested.

## HUD Overhaul dependency error

Version 0.4.1 fixes LUZ rejecting valid BepInEx dependency ranges such as `1.03 / >=1.03`. Upgrade LUZ and reopen your existing profile. Matching installed dependencies now pass validation; missing or incompatible dependencies still block Apply. This fixes the launcher check and does not establish HUD Overhaul gameplay compatibility.

## Linked-folder installation error on Bazzite

Version 0.4.3 resolves linked home and application roots before checking installation files. This supports paths reached through aliases such as `/home` to `/var/home`. Links inside the extracted application or managed mod folders remain unsupported. Install the updated Linux package; no game relocation or administrator access should be needed for this fix. The repair passes linked-directory fixtures on Windows; native Bazzite acceptance is pending.

## Undo LUZ's Windows Nexus registration

Use **Maintenance > Browser downloads > Remove LUZ as NXM manager**, beside **Change system-wide NXM handler**. One click removes the registration and shows the result inline; there is no confirmation dialog. If you already deleted LUZ, extract the Windows package and run **Install LUZ.exe**, then click **Remove LUZ from Nexus download handlers**. You do not need to install LUZ again.

This removes LUZ's registration while preserving mods, profiles and other managers. Use **Open Windows Default apps** to choose a replacement. The extracted installer's cleanup also opens Windows Default apps. Search for **NXM** and select your other manager. If that manager is missing, repair its installation or use its option to register Nexus links. Reauthorizing a Nexus account does not register a Windows link handler. Close and reopen Settings if it still displays the removed entry. LUZ does not rewrite Windows' protected default choice or invent a replacement for an unregistered manager.

Windows has one default handler for the `nxm` scheme, shared by all games. Keep your other manager as that default and use **Paste download link** or manual ZIP import for Nivalis. Removing the registration leaves your mods, saves and LUZ profiles in place.

## Problems after a game patch

Open **Maintenance > After a game update > Diagnose game update**. This shows the installed Steam build, binding regeneration settings and relevant lines from the last saved game log. That log may predate the patch.

If generated BepInEx bindings are stale or automatic regeneration was disabled, close the game and choose **Refresh game bindings**. LUZ backs up the generated interop folder and loader configuration, then enables regeneration at your next normal game launch. It leaves installed mods, object packs and saves intact. Custom interop paths require manual review.

If a mod says **requires game build** or **Unsupported GameAssembly**, install a version of that mod compatible with the patched game. Updating LUZ or refreshing bindings does not make incompatible hooks safe. Keep object frameworks and packs installed when loading saves containing their objects. **Unapplied changes** instead means the selected profile differs from the last applied profile; review it before applying.

To report an unresolved failure, use **Maintenance > Export diagnostics** and include the exact error. Review the archive before sharing it.

Read **QUICK START.txt** for installation, BepInEx setup and adding your first mods.

Windows profiles from 0.1.x stay in their existing location.

## Choose your download

| System | Package | Open |
|---|---|---|
| Windows 10/11 x64 | `win-x64.zip` | Extract the entire folder, then run `Install LUZ.exe`. |
| Linux x64 glibc desktop | `linux-x64.zip` | Extract with your archive manager or `unzip`, then run `./"Install LUZ"` from the extracted folder. |
| macOS 15+ Intel | `osx-x64.zip` | Extract, follow the signing step below, then run `Install LUZ.command`. |
| macOS 15+ Apple Silicon | `osx-arm64.zip` | Extract, follow the signing step below, then run `Install LUZ.command`. |

Click **Install and create shortcut**. LUZ installs for your account and adds a desktop shortcut with its icon. After installation, open that shortcut. The standalone installer does not start LUZ or the game automatically. Updates install beside the previous version and redirect the shortcut; your profiles stay in their existing data folder. You can delete the extracted download after a successful install.

If Linux reports Permission denied after extraction, open a terminal in the extracted folder and run `chmod +x "Install LUZ" "LUZ Civic Terminal"`, then run `./"Install LUZ"` again.

On Linux, your desktop may ask you to **Allow Launching** or mark the new shortcut as trusted. On macOS, the desktop shortcut points to the installed app in your Applications folder.

For portable use, open `LUZ Civic Terminal.exe`, `LUZ Civic Terminal`, or `LUZ Civic Terminal.app` directly instead. Keep all accompanying files together. These packages include .NET; no separate .NET installation is needed. Linux needs a graphical desktop with X11 or XWayland, the usual font/fontconfig and graphics libraries, and `xdg-open`. Use a current glibc distribution such as Ubuntu 22.04/24.04; this archive does not target Alpine/musl. It is not a headless-server application. A missing shared-library error needs the corresponding distribution package installed.

**macOS preview:** cross-built archives contain `Prepare macOS.command` beside the app. Run it once to apply a local ad-hoc signature; it does not start the terminal or game. If Finder blocks the helper, run `sh "Prepare macOS.command"` in Terminal from that extracted folder. The app is not notarized. If macOS blocks opening it, use its **Privacy & Security > Open Anyway** flow only for a copy you trust. Do not disable system-wide security settings. Native macOS packaging applies the signature during the build instead.

**The terminal's platform is separate from the game's platform.** Nivalis Nights is a Windows x64 game. Linux requires a working Steam Proton installation; macOS requires a working Wine/CrossOver setup capable of running this game. This terminal does not install those tools or establish game compatibility. Always install the **Windows x64 IL2CPP BepInEx** build for this game, including under Proton/Wine.

For Proton, set the game's Steam Launch Options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`. Merge this with any existing options you need. For Wine/CrossOver, configure `winhttp` as **native, then builtin** in the game's bottle. See the [official BepInEx Wine/Proton instructions](https://docs.bepinex.dev/articles/advanced/proton_wine.html).

Windows and Linux use Steam when **Play Nivalis** is pressed. macOS requires **Maintenance > Game launch** to point to your working compatibility launcher executable or script. Enter one literal argument per line, without shell quotes or expansion. Linux users can also configure a custom launcher, for example their existing Flatpak Steam command. Use an executable inside an `.app` or an executable wrapper script, not the `.app` directory itself. Opening the terminal never launches the game.

## Start here

1. Extract the package for your platform, run its installer, then open the desktop shortcut as described above.
2. Open **Maintenance**. Check the detected game folder, or choose the folder containing `Nivalis Nights.exe`.
3. If you already use mods, close the game and click **Import existing installation**. This copies your current plugins, patchers and settings into the selected profile without changing the game.
4. If you need BepInEx, click **Install recommended BepInEx**. The terminal downloads the Windows x64 IL2CPP build from the official build server and backs up replaced loader files. **Check latest official build** offers the newest published build; newer builds may need compatibility testing.
5. Add mods through **Catalogue**, or use **Registry > Import ZIP / DLL** for files you downloaded yourself.
6. Enable the mods you want. Use **Profile tools > Validate profile**, then **Apply profile**. Read the confirmation: Apply replaces the deployed plugins and patchers with the enabled contents of this profile.
7. Click **Play Nivalis** when you want to start the game through Steam or your configured compatibility launcher. Installing, applying and opening the terminal never launches the game automatically.

For later updates, use **Check updates**, review the proposed versions, download them, then **Apply profile** with the game closed. For a local ZIP, import the newer archive instead. Use **Edit mod > Set source link** with a supported Nexus or Thunderstore package page if you want source update checks.

## Object Studio projects

Object Studio exports a whole collection as one ZIP, including its name, version, icon, page link and Object Framework dependency. Use Object Framework Runtime 0.3.2 with game build 25726588, in the same profile as your object packs. Future game patches may require a newer runtime. Import that ZIP like any other mod. A new build replaces the active version of the same project; disabling it removes its objects together when you apply the profile. Saves containing those objects still require the project.

Studio’s optional **Send to LUZ** command opens this version with the project ZIP or forwards it to an existing LUZ window. Confirm the import into the displayed profile, then apply when ready. Direct Studio deployments are also recognized by **Import existing installation**.

## Profiles and sorting

Use **New** for an empty profile or **Copy** for a copy with the same mods and stored settings. Enable and disable mods independently in each profile. Pin a version to exclude it from updates. Removed mods stay in the library; **Profile tools > Add from library** lets you reuse an older version.

Drag the dotted handle to change deployment priority, or use the **Move** buttons. Lower rows win when two packages contain the same file. **Auto-sort** places declared dependencies first and keeps unrelated mods in their existing relative order.

**Deployment priority is not BepInEx startup order.** BepInEx resolves plugin startup using its own dependency rules. The terminal detects missing dependencies, duplicate plugin IDs, declared incompatibilities and dependency cycles; it cannot guarantee compatibility between arbitrary mods.

Each profile has separate stored configuration. When you apply another profile, the terminal captures settings from the previously applied profile. Edit the currently applied profile's settings in the game's `BepInEx/config` folder. **Open profile configuration** opens the live configuration for the applied profile and stored settings for an inactive profile. Close the game before copying an applied profile; its latest settings will be included.

Profile exports contain the ordered mod list, versions, hashes, enabled flags, pins and notes. They do not contain mod binaries, API keys, settings or save games. To import a profile, first import the exact referenced mod versions into your library. The terminal lists anything missing.

## Thunderstore and Nexus

**Thunderstore:** load the Nivalis Nights catalogue, search, view package icons and open source pages. Install downloads the selected package and its declared dependencies. BepInEx package dependencies are handled through the dedicated loader setup, avoiding an accidental downgrade to the older loader packaged on Thunderstore.

**Nexus:** paste your personal API key into Maintenance, then use **Find Nexus mod** with a mod page, mod number or `nxm://` download link. Saved keys use Windows account encryption, macOS Keychain, or the Linux desktop Secret Service. Linux persistent storage needs `secret-tool` (commonly packaged as `libsecret-tools`) and an unlocked desktop keyring. **Use for session** works without persistent storage on every platform. Keys are never saved as plain text or passed as command-line arguments; they are sent only to the Nexus API. The file list includes main and optional files; choose the variant you intend to use. Update checks match the installed file name; renamed variants require manual review.

Direct Nexus downloads depend on your account's API permissions. Standard accounts need the signed link generated by the website's **Mod Manager Download** button. You can always download a ZIP in your browser and import it. LUZ does not log into your account or bypass Nexus download restrictions. Nexus dependencies are not automatically resolved.

If Nexus only shows **Manual Download**, use it and import the downloaded ZIP in LUZ. Registering NXM does not add a button to the website. Your personal key is available from [Nexus API key settings](https://www.nexusmods.com/settings/api-keys); generating it is optional for manual ZIP imports.

If **Apply profile** is unavailable, read the sidebar's blocking issue and **Mod details > Profile issues**. Update or enable the named dependency in the active profile, close the game, check the game location, and resolve any interrupted-deployment recovery prompt. Manually updating a game DLL does not update the version stored in your LUZ profile.

### Browser downloads (NXM links)

**Warning:** Choosing LUZ as the default NXM manager routes Nexus links for every game to LUZ instead of your current manager. LUZ only supports Nivalis Nights. If you want to keep your current manager, do not register LUZ: use manual ZIP import or paste Nivalis links instead.

1. Close an older LUZ window and open this version. Your existing profiles are retained.
2. Add your Nexus API key under **Maintenance > Nexus connection**. Use a key belonging to the same account you use on the website.
3. Under **Maintenance > Browser downloads**, read the warning, then click **Change system-wide NXM handler**. On Windows, choose LUZ for **NXM** in the Default apps settings that open. Linux/macOS select LUZ after the confirmation. Keep the application at its installed location. On Windows, the installer and updater refresh an existing LUZ registration. Register again after moving a portable copy; Linux/macOS users should also register again after an update.
4. Click **Mod Manager Download** for a Nivalis Nights file on Nexus, and allow your browser to open LUZ. An existing LUZ window receives the link; otherwise the terminal opens.
5. Confirm the exact file and destination profile. The file is downloaded and imported. Click **Apply profile** separately when you want to change the game.

The OS association applies to all Nexus links. LUZ supports **Nivalis Nights files only**, not other games or Nexus Collections. If you use Vortex for other games, keep Vortex as the default and use **Paste download link** in LUZ instead. You can reselect Vortex in its settings or the operating system's default apps. LUZ does not overwrite another manager's registration on Windows or modify the protected Windows UserChoice setting.

Links wait in memory while another action is running. Repeated clicks for the same file update its pending token. If a key is missing or a download fails, fix the issue and use **Retry queued links**; use **Clear queued links** to discard them. Closing LUZ clears pending links. Expired links require another click on Nexus. Link tokens are not stored in profiles or displayed in status text.

Supported downloads are BepInEx ZIPs and individual DLLs. RAR/7z archives and FOMOD installers require a different installation route. No download automatically applies a profile or launches the game. Browser associations and authenticated downloads still need native user testing; automated checks use local fixtures and a separate-process handoff.

Mods execute code when the game starts. Only install packages you trust. The terminal reads plugin metadata without executing plugin code during import.

## Backups and recovery

Every Apply creates a restore point for plugins, patchers and configuration. **Maintenance > Restore profile backup** restores one and preserves the current files as another restore point. Loader backups have a separate restore action.

If Apply and Play show **Recovery needed**, open **Maintenance > Recovery required** at the top of the page. Use the recovery action shown there: **Recover profile deployment** restores mod files and settings; **Recover BepInEx installation** restores loader files. If both appear, complete both. LUZ selects the matching backup automatically and reports any remaining blocker after each operation.

If the recorded backup is missing or unreadable, use **Export recovery diagnostics** for support. Reinstalling LUZ does not remove recovery records from your separate profile library.

Save games are not switched or backed up. Removing furniture or other content mods can affect saves that use their objects. Keep your own save backups.

Library, profile and backup locations:

- Windows: `%LOCALAPPDATA%\LUZCivicTerminal` (unchanged from 0.1.x).
- Linux: `$XDG_DATA_HOME/LUZCivicTerminal`, or `~/.local/share/LUZCivicTerminal` when that variable is unset or relative.
- macOS: `~/Library/Application Support/LUZCivicTerminal`.

Keep this data folder when updating. Use **Maintenance > LUZ updates**, or close LUZ and run the new release's installer. Do not mix runtime files from different releases. Backups are retained until you remove them yourself; large mod sets can use substantial disk space. Saved keys remain in the platform's credential store; they are not part of profile exports.

**Export diagnostics** includes the terminal's latest error, selected mod versions and the last 2,000 BepInEx log lines. Known local paths and credential fields are redacted. Review the archive before sharing because individual mods can write arbitrary data into their logs.

## Preview status

Automated tests use temporary game folders and Windows UI rendering without opening a desktop window. Checks cover archive paths, platform paths, literal launch arguments, Steam discovery, dependency rules, profiles, deployment, backups and loader replacement. Cross-compilation and archive checks do not prove native execution. Linux/macOS desktop behavior, native keyrings and compatibility-layer gameplay remain unverified until tested on those systems. Authenticated Nexus downloads and physical drag/drop also require native acceptance testing.

Before relying on a new platform build:

1. Open the terminal with the game closed. Expected: no game launch; the folder picker, scrolling and search work, including paths containing spaces.
2. Create a test profile, import a trusted ZIP, reorder using its handle and the Move buttons, then reopen the terminal. Expected: order and enabled states persist.
3. With a separate game-file backup, install the loader if needed, Apply the profile, then Restore its backup. Expected: original plugin/config files return. Stop if files are missing or a recovery journal remains.
4. Optionally save a Nexus key, restart and retrieve a mod listing, then remove the key. Expected: persistent storage works, or a useful keyring error offers session-only use.
5. Only when ready to start the game, press Play. Expected: your chosen Steam/compatibility route starts Nivalis with the applied profile. Confirm the mods inside the game; a successful launch request alone is not proof.

Supported packages: ZIP archives and individual BepInEx DLLs using `BepInEx/plugins`, `BepInEx/patchers`, `BepInEx/config`, equivalent folder roots, or ordinary plugin files. Game-file replacements, mod installers, links inside managed mod folders and other mod frameworks are not supported. User-selected Steam/game root links are resolved before validation. Windows-style archive separators are normalized, and shared-file precedence treats destination case consistently with the Windows game. Ambiguous case aliases are rejected.

This is an unofficial tool. Nivalis Nights, BepInEx, Nexus Mods and Thunderstore are separate projects/services. Required runtime license notices are included in `Notices`.
