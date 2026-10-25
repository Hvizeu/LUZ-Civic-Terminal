# LUZ for Nivalis Nights

Windows integration for Vortex 2.9.0+ and LUZ Civic Terminal 0.8.5+.

1. Install this extension ZIP in Vortex and restart Vortex when requested.
2. Manage Nivalis Nights. The extension finds Steam and an installed LUZ, or asks Vortex to download the compatible Windows Main File from the LUZ Nexus page.
3. Accept the one-time connection review. If missing, LUZ prepares the latest official Windows x64 IL2CPP BepInEx build from builds.bepinex.dev.
4. Install or update mods in Vortex, then press Play. LUZ synchronizes the selected profile, applies when its normal safeguards permit, and stays open. Review the first Apply in LUZ.

Vortex owns downloads, Nexus login and installer choices. LUZ owns profiles, enabled states, order, pins, compatibility, configuration and deployment. Installed Vortex packages remain available even when disabled in Vortex. Removing a Vortex package stops its update feed and keeps its LUZ copy. Vortex Play never launches Nivalis.

## Migrating from another Nivalis extension

Use Purge Mods with the previous extension still active first. Disable that extension, install/enable LUZ for Nivalis Nights, then restart Vortex and manage the game. Only one game-support extension may own Nivalis. Existing staged packages and LUZ profiles are retained. Loader bundles, standalone tools and unsupported packages require explicit review or exclusion in LUZ Maintenance; they are not gameplay plugins.

The extension has independent identity game-nivalisnights-luz. It preserves Nexus game identity nivalisnights (10401) and Steam 1488490. It does not require a LUZ Nexus API key and does not change NXM associations. A normal LUZ archive needs this extension already active to receive automatic Vortex setup.

Package recognition was informed by Sebastian Maute's Nivalis Nights extension 0.3.4. Required upstream license and attribution are retained. Our extension maintains its own setup, snapshots, profile integration and deployment ownership.
