# Changelog

## 0.7.6

- Added saved-data recovery support for Culinary Inspiration and restaurant reviews.
- Updated compatibility checks for Nivalis Expanded and Unofficial Patch.

## 0.7.5

- Repair imports whose package directory survived an interrupted import or registry restore. Validate a fresh copy before publication and preserve the original directory under import-recovery.
- Keep disabled and pinned settings intact, and restore the original directory if registration fails. Includes the disabled-import Apply fix from 0.7.4.

## 0.7.4

- Fix Apply after importing an existing installation when imported mods are disabled. Exact imported DLL and ASI bytes are recognized as managed, allowing the disabled files to be removed into the normal restore point.
- Preserve disabled settings on reimport and continue blocking unknown binaries or imported binaries changed on disk.

## 0.7.3

- Validate Nivalis Expanded against the compiled Unofficial Patch plugin dependency `hvizeu.nivalis.unofficialpatch >=0.4.7 <0.5.0` for Nexus and local imports.
- Accept compatible 0.4.x patch versions while keeping missing, disabled, out-of-range and duplicate plugin identities blocking. Unrelated package dependencies remain strict.

## 0.7.2

- Let author-declared stateless and ordinary companion-file support omit compiled contracts when it claims no native recovery implementation.
- Keep compiled contracts mandatory for native and embedded recovery behavior, and compare declared contract hashes with the exact packaged DLL during validation and protected-session admission.
- Refresh the support-contact action as soon as project details are saved.
- Include complete player and CorpSec step-by-step guides in every desktop package, covering first setup, repeat releases, normal/recovery exports and automation.
- Keep public-checkout validation independent of local mod source folders; provide an explicit additional check for the real Object Framework editions.

## 0.7.1

- Keep the CorpSec **My mods** list inside a bounded, vertically scrollable rail and sort saved projects by name.
- Add **Remove from My mods** to Project details with a confirmation that identifies exactly which private data is removed and which author files remain untouched.
- Keep removed projects hidden during routine build scans. Explicitly adding their builds folder restores them.

## 0.7.0

- Add one bounded static analyzer shared by CorpSec desktop, CLI and generated build integration.
- Preserve project decisions and their provenance while refreshing detected DLL identity, assets, declarations, maintained integrations and persistence evidence.
- Remember explicit standalone/recovery edition selection for plugins that share a GUID.
- Replace raw recovery declarations with guided persistence, capability and adapter editors.
- Separate package, recovery declaration and in-game recovery status; allow ordinary packages to omit incomplete recovery support.
- Reject untouched recovery adapter templates and LUZ-dependent binaries in ordinary packages.
- Add JSON CLI results, unresolved exit codes and serialized authoring writes.
- Validate maintained Object Framework support and independent stateless, companion-file and custom-adapter examples through finished archive reimport.

## 0.6.2

- Accept watched project folders containing linked documentation or analysis directories by excluding those links from discovery.
- Keep linked payload assets rejected during generation and packaging.

## 0.6.1

- Replace CorpSec's single-package opening flow with a persistent **My mods** workspace.
- Scan parent build folders for several mods, group repeated outputs by plugin identity and select the newest version without losing author settings.
- Remember watched build folders, the selected mod and each project's release output so later releases do not require reopening a ZIP.
- Refresh, regenerate and validate from the newest watched build as part of the normal package action.
- Add a guided owned-file editor and keep custom adapters and implementation fallbacks under an advanced section.

## 0.6.0

- Add Civic Terminal and CorpSec Terminal workspace tabs while preserving the active player profile.
- Add reusable author projects for final mod folders, ZIPs or DLLs, plus static plugin, dependency, asset and compiled-contract detection.
- Generate versioned `LUZ/mod-support.json` declarations, exact plugin/payload/adapter hashes, companion ownership rules, capability implementations, a recovery-adapter template and an optional standalone LUZ detector.
- Replace protected-session third-party allowlists with frozen package-local descriptors while retaining maintained suite compatibility and old timeline readability.
- Run author adapters through preflight, activation, staged save transformation, suspension, restoration acknowledgement and failure cleanup without retrying a started transaction.
- Build atomic Nexus/manual and Thunderstore ZIPs, checksums and deployment trees, then re-import and plan every finished archive in a disposable LUZ library.
- Add the same inspect, generate, validate, package and build-integration workflow to the `--corpsec` command line.

## 0.5.1

- Replace raw compatibility scanner output with plain guidance that explains what LUZ knows, what it cannot verify, and what each button does.
- Remember an explicit compatibility decision for the exact inspected action and setup; ask again after a relevant game, loader, profile, mod, or configuration change.
- Name the affected package and give users a concrete support route for mods that lack maintained LUZ compatibility metadata.
- Treat Cancel as a normal return with a clear result instead of showing a second failure dialog.

## 0.5.0

- Add bundled Mod Continuity adapters and opt-in protected save timelines for supported game and mod builds.
- Preserve checkpoints, unavailable feature data and interrupted-save recovery records; restore features or resume retained timelines through Maintenance.
- Add automatic installation of verified GitHub launcher updates at startup, with separate check and installation preferences.

## 0.4.9

- Validate schema 2 compiled contracts, generic inheritance and forwarded type identities before deployment. Existing schema 1 metadata remains supported.
- Treat incomplete or inconsistent recovered save publication as a blocking integrity failure. Retain restartable restoration checkpoints and verify referenced generations.
- Exclude archived recovery payloads from routine compatibility hashing without excluding them from deployment preservation.
- Retain worker cancellation, timeout and bounded inputs without process RAM ceilings.

## 0.4.8

- Inspect compiled mod contracts and binding provenance before Apply and Play.
- Require an explicit, single-attempt acknowledgment for incompatible or unverified contracts; preserve non-overrideable safety failures.
- Prepare verified save generations for recovery by compatible game-side components, and retain checkpoints for interrupted recovery.
- Include shared recovery sources in the repository so a clean checkout builds independently.

## 0.4.7

- Add All, Enabled and Disabled registry filters.
- Preserve DLL/ASI companions and reject ambiguous archive layouts.
- Detect unmanaged binaries before Apply, and offer registry backup recovery when startup cannot read the library.
- Verify registry compatibility during self-update while preserving profiles, configuration and recovery records.

## 0.4.6

- Refresh the bundled installation and update guides for Nivalis Update #4 and Object Framework Runtime 0.3.8.
- Explain live versus stored profile settings, reuse of library mods, and current recovery and platform instructions.

## 0.4.5

- Reconnect a moved Nivalis installation while keeping profiles and restore points.
- Preserve cached settings when the new installation has no configuration folder yet.
- Show file-access errors with the affected path and keep Maintenance and diagnostics available.
- Back up and upgrade the profile registry. After updating, keep using LUZ 0.4.5 or newer; older versions cannot open the upgraded registry.

## 0.4.4

- Put Remove LUZ as NXM manager beside registration in Windows Maintenance, with the result shown immediately.
- Explain that registering LUZ changes the Nexus-link handler for every game. Manual ZIP import and pasted Nivalis links remain available without changing the handler.
- Preserve other managers' registrations, mods and profiles when removing LUZ's registration. Choose a replacement in Windows Default apps.

## 0.4.3

- Fix installation through linked home/application folders, including Bazzite-style home paths.
- Add Nexus-handler removal to Windows Maintenance and the extracted installer, including after the old LUZ folder was deleted.

## 0.4.2

- Give interrupted profile changes and BepInEx installations their own recovery actions at the top of Maintenance.
- Select the matching restore point and explain any remaining blocker.
- Offer recovery diagnostics when the required backup is missing or unreadable.

## 0.4.1

- Fix valid dependency ranges being rejected as version numbers, including the HUD Overhaul error. Existing profiles work without reimporting mods; missing dependencies still block Apply.

## 0.4.0

- Check GitHub for LUZ updates automatically or on demand, then verify and install the matching platform download while keeping profiles.
- Add game-update diagnostics and a backed-up binding refresh for the next normal game launch.
- Refresh an existing Windows Nexus-handler registration during installation.

## 0.3.2

- Initial public preview with profiles, ZIP/DLL and existing-installation imports, mod ordering, dependency checks and deployment backups.
- Recommended BepInEx setup, Thunderstore browsing, Nexus lookup/downloads and mod update checks.
- Object Studio collection imports, diagnostics and experimental Linux/macOS downloads alongside Windows x64.
