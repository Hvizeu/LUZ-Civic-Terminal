# LUZ for Nivalis Nights

Windows integration for Vortex 2.9.0+ and LUZ Civic Terminal 0.8.5+.

1. Download the extension ZIP from [its Nexus Files page](https://www.nexusmods.com/site/mods/2496?tab=files). In Vortex, open **Home > Extensions**, drop the ZIP onto that page, enable it, and restart Vortex when requested.
2. Manage Nivalis Nights. The extension finds Steam and an installed LUZ, or asks Vortex to download the compatible Windows Main File from the LUZ Nexus page.
3. Accept the one-time connection review. If missing, LUZ prepares the latest official Windows x64 IL2CPP BepInEx build from builds.bepinex.dev.
4. Install or update mods in Vortex, then press Play. LUZ synchronizes the selected profile, applies when its normal safeguards permit, and stays open. Review the first Apply in LUZ.

Vortex owns downloads, Nexus login and installer choices. LUZ owns profiles, enabled states, order, pins, compatibility, configuration and deployment. Installed Vortex packages remain available even when disabled in Vortex. Removing a Vortex package stops its update feed and keeps its LUZ copy. Vortex Play never launches Nivalis.

## Migrating from another Nivalis extension

Use Purge Mods with the previous extension still active first. Disable that extension, install/enable LUZ for Nivalis Nights, then restart Vortex and manage the game. Only one game-support extension may own Nivalis. Existing staged packages and LUZ profiles are retained. Loader bundles, standalone tools and unsupported packages require explicit review or exclusion in LUZ Maintenance; they are not gameplay plugins.

The extension has independent identity game-nivalisnights-luz. It preserves Nexus game identity nivalisnights (10401) and Steam 1488490. It does not require a LUZ Nexus API key and does not change NXM associations. A normal LUZ archive needs this extension already active to receive automatic Vortex setup.

Package recognition was informed by Sebastian Maute's Nivalis Nights extension 0.3.4. Required upstream license and attribution are retained. Our extension maintains its own setup, snapshots, profile integration and deployment ownership.

Version 0.6.2 keeps registration separate from runtime API calls. Game scanning only discovers existing LUZ installations; downloads start during Manage/setup. Setup registers the handoff tool immediately, including on a first install, and creates every exchange destination. Play preserves the original error when synchronization is cancelled.

Version 0.6.3 revalidates the paired game installation when Vortex reopens, before consulting Steam discovery. This prevents a startup `Game entry not found` error while Steam's inventory is still unavailable. Missing installations still require rediscovery; invalid connection metadata stops setup. The existing connection and LUZ profile are retained. Update the extension ZIP once; LUZ 0.8.7 needs no application update for this repair.

Version 0.6.4 routes a saved Vortex shortcut for the paired `Nivalis Nights.exe` through LUZ synchronization. Earlier versions could let that shortcut start the game directly. Shortcuts for other installations and tools keep their existing behavior. An incomplete migration still stops with an error; the extension never purges files automatically.

## BepInEx opens but LUZ does not

BepInEx belongs to game startup. LUZ is a separate desktop application. Launching Nivalis through Steam or its executable does not open LUZ.

If this happens after pressing Play in Vortex, update this extension to 0.6.4 through **Home > Extensions**, enable it and restart Vortex. Check that **LUZ Civic Terminal** is the primary tool for Nivalis and that only one Nivalis game-support extension is enabled. If Vortex still uses a previous extension's direct deployment, follow the migration steps above before managing the game again.

If it still fails, include the extension, Vortex and LUZ versions, the exact button or shortcut used, a screenshot of Nivalis's Vortex tools, and the relevant Vortex error/log. If LUZ opens, **Maintenance > Export diagnostics** provides its side of the report. BepInEx output alone cannot identify why LUZ did not open.

**Browse Extensions** is a catalog search, separate from the Extensions page where ZIPs are installed. Vortex lists one game extension per game, so this independently installed extension may be absent from Browse. Use the direct Files page above; a missing catalog entry does not diagnose whether the installed extension registered successfully.

Developer checks: `npm ci --legacy-peer-deps --ignore-scripts`, then `npm test`. On a Windows machine with Vortex 2.9.0, set `LUZ_VORTEX_ASAR` to its `resources/app.asar` and run `npm run test:host`. These checks read the installed host's API guard, discovery, deployment-path and start-hook implementation into disposable fixtures. They do not open Vortex, execute LUZ or establish native UI acceptance.
