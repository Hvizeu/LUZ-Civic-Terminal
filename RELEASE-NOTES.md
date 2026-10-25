# LUZ Civic Terminal 0.8.5

LUZ Maintenance can now prepare a protected repair copy for the Unofficial Patch failure where a valid manager companion belongs to an older native save revision. This can happen when the native save is overwritten while the patch is absent.

The preview explicitly identifies the separately named destination and the patch-owned data that will reset: manager policies/accounting, Inspiration records and restaurant-review history. The native game progress and original native/companion files remain preserved. Missing, malformed, unsupported, contradictory or interrupted companions are still rejected rather than guessed around.

Install Unofficial Patch 0.4.15 with this LUZ build. With the game closed, choose **Maintenance > Prepare saved-data recovery**, select the affected save and confirm the preview. Start the game normally, then use **Settings > Mods > Recover prepared save** from the idle main menu. The mod loads the captured native progress with fresh patch-owned records and publishes a verified separate recovered slot. LUZ captures stale manager backups as evidence, uses an exact matching backup instead of reset repair, and refuses malformed backups.

LUZ can be installed directly from its Nivalis Nights Nexus page with **Mod Manager Download**. Once the **LUZ for Nivalis Nights** extension is active, Vortex recognizes the official Windows package, installs it as an isolated tool, connects it to the correct Steam installation, and makes LUZ the primary tool without a Maintenance setup flow.

The extension still finds an existing installation or asks Vortex to obtain the current Windows Main File from Nexus when LUZ is missing. Vortex uses its signed-in Nexus account; no LUZ API key is required. The user-selected and automatic-download paths converge on the same idempotent pairing process.

Routine use is simply: install or update a mod in Vortex, then press **Play**. Vortex exports the completed installer-selected packages; LUZ synchronizes the selected profile, applies it through the existing safeguards when safe, and stays open at its launcher. Vortex Play never launches Nivalis.

New supported packages enter the selected LUZ profile enabled. Existing order, disabled state, pins, and notes remain intact. Removing a package from Vortex leaves its imported LUZ copy in place and marks the update source unavailable.

Users migrating from the earlier direct-deployment extension may need to use **Purge Mods** once before managing the game again. This migration is explicit so neither tool silently removes an existing installation.

The integration requires Windows, Vortex 2.9.0 or newer, Nivalis Nights managed through the LUZ extension, and LUZ 0.8.5 or newer. Linux and macOS retain their existing manual and profile workflows.

## Integrated Vortex support

Install the separate LUZ for Nivalis Nights extension once. Setup finds or obtains LUZ and offers one connection review, including the latest official Windows x64 IL2CPP loader when missing. Corrected package preparation preserves wrapped configuration and plugin companions. Vortex Play remains usable while LUZ stays open. Updates retain LUZ pins and disabled selections; unsupported tools and legacy loader bundles identify the required review.
