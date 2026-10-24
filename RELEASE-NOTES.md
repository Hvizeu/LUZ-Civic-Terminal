# LUZ Civic Terminal 0.8.2

LUZ can now be installed directly from its Nivalis Nights Nexus page with **Mod Manager Download**. Once the canonical **Nivalis Nights Support** extension is active, Vortex recognizes the official Windows package, installs it as an isolated tool, connects it to the correct Steam installation, and makes LUZ the primary tool without a Maintenance setup flow.

The extension still finds an existing installation or asks Vortex to obtain the current Windows Main File from Nexus when LUZ is missing. Vortex uses its signed-in Nexus account; no LUZ API key is required. The user-selected and automatic-download paths converge on the same idempotent pairing process.

Routine use is simply: install or update a mod in Vortex, then press **Play**. Vortex exports the completed installer-selected packages; LUZ synchronizes the selected profile, applies it through the existing safeguards when safe, and stays open at its launcher. Vortex Play never launches Nivalis.

New supported packages enter the selected LUZ profile enabled. Existing order, disabled state, pins, and notes remain intact. Removing a package from Vortex leaves its imported LUZ copy in place and marks the update source unavailable.

Users migrating from the earlier direct-deployment extension may need to use **Purge Mods** once before managing the game again. This migration is explicit so neither tool silently removes an existing installation.

The integration requires Windows, Vortex 2.9.0 or newer, Nivalis Nights managed through the canonical extension, and LUZ 0.8.2 or newer. Linux and macOS retain their existing manual and profile workflows.
