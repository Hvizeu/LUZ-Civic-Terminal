# Nivalis Nights Support for Vortex

Manage **Nivalis Nights** in Vortex. The extension finds the Steam installation, creates an isolated deployment area, and recognizes a LUZ Civic Terminal package installed from its Nivalis Nights Nexus page. It connects LUZ and makes it the primary tool automatically.

Routine use is two steps:

1. Install or update a Nivalis Nights mod in Vortex.
2. Press **Play** in Vortex.

Vortex exports the completed installer-selected packages. LUZ synchronizes them into the currently selected profile, applies that profile when its normal safeguards allow it, and stays open at its launcher. Vortex Play never launches Nivalis.

LUZ owns enabled state, ordering, pins, compatibility, deployment, recovery, and game launch. Vortex enable switches do not control the LUZ profile; every installed package remains available to LUZ, including packages disabled in Vortex.

## Existing Vortex users

Version 0.5.0 replaces the previous direct-deployment behavior of the canonical `game-nivalisnights` extension. If Vortex still has old links in the game folder, use **Purge Mods** once and manage the game again. The extension does not silently remove an existing deployment.

Requires Windows, Vortex 2.9.0 or later, and LUZ 0.8.2 or later. Choose **Manage** for Nivalis Nights once, then use **Mod Manager Download** on the [LUZ Nexus page](https://www.nexusmods.com/nivalisnights/mods/81). Vortex installs the marked Windows package, pairs it and registers it as the primary tool. If LUZ is still missing during setup, the extension asks Vortex to download that page's newest compatible Windows Main File using the signed-in Nexus account; LUZ needs no API key.
