# Mod Continuity

This reference matches LUZ 0.7.2. For ordinary profiles and backups, see the [player guide](../PLAYER-GUIDE.md). For author setup, see the [CorpSec walkthrough](../CORPSEC-GUIDE.md).

LUZ includes the compatibility runtime, API and supported-build catalog in its desktop release. GitHub releases update them with the launcher. No separate mod-site download is needed.

The initial adapter targets Nivalis Nights build 25984046 and BepInEx 6.0.0-be.788 on native Windows. LUZ verifies the actual native binary, metadata, loader and installed payloads. Unsupported builds retain their checkpoint and cannot start a protected session.

## Player workflow

1. In Maintenance, choose **Enable protected saves** and select the original native save.
2. Install the compatible recovery editions or validated author-supported packages and **Apply profile** with the game closed. Their existing plugin and saved-object identities remain unchanged.
3. Press Play. LUZ captures and verifies a checkpoint outside the game and cloud save trees, then prepares a separate protected timeline.
4. Use normal manual saves, quicksaves and autosaves. The runtime routes them to the protected timeline. Load another historical timeline by selecting it in LUZ.
5. If a supported feature becomes unavailable, choose **Suspend supported feature**, select it and Apply. LUZ disables supported gameplay modules and hard dependants in this profile; Object Framework's catalogue and native serialization remain required for saves containing its objects.
6. After installing a compatible module, use **Restore feature**, Apply and load the protected timeline. LUZ reinstates the recorded dependency set. Restored modules must acknowledge their loaded state before saving resumes.

The original checkpoint is retained. External or cloud replacements require investigation rather than overwriting either copy. A failed save preserves its first error and journal; Maintenance can restore the exact native and companion write set when ownership remains verifiable.

Use **Check capabilities** to read which owners/features are available and why. If an interrupted protected save is reported, close the game and choose **Recover interrupted save**. Expected: LUZ restores the unfinished native-save and companion publication together, retains the original checkpoint and reports externally changed files rather than overwriting them.

Always launch a protected session through **Play** in LUZ. Selecting a save or generating a support descriptor alone does not activate protection. A failed protected operation stops; it cannot silently continue with unprotected saving.

**Restore original checkpoint** creates another protected timeline at the original historical point. It preserves the current timeline and any externally changed original files. This also works when the live original has been replaced, because the native data and companions come from the verified immutable checkpoint.

**Resume protected timeline** selects retained progress after a rollback. Play rechecks its game, adapter and mod identities before loading; a changed compatible payload creates a new revision instead of overwriting the historical timeline.

Protected sessions disable in-game save deletion. Checkpoint files are never automatically pruned. Global account/settings files are included in the checkpoint; normal native settings remain shared by the game. Timeline routing applies to native slot saves and their declared companions.

## Package-local mod support

Third-party authors can use **CorpSec Terminal** to generate `LUZ/mod-support.json` beside their plugin DLL. Descriptor schema 1 and recovery API 1 remain supported. The descriptor binds the plugin, other payload files, declared compiled contracts, optional recovery adapters and supported capabilities to their exact identities. It declares companion files under the save or BepInEx configuration roots, their slot mapping, ownership and validation rule. The generated optional-runtime helper uses reflection. Generation leaves the plugin DLL unchanged; whether that binary works without LUZ depends on its own dependencies.

Stateless support and ordinary companion-file support do not need an unrelated native capability. A selected native or embedded recovery implementation still requires its compiled contracts. Existing schema-1 metadata and schema-2 embedded contracts retain their respective identity rules.

Protected admission reads validated package-local descriptors and freezes their full contents, provenance, hashes and selected implementations into the timeline. Updating or removing a package cannot erase the recovery rule used by an existing checkpoint. Maintained suite integrations use the same frozen contract while old schema-1 timelines retain their legacy companion rules.

Adapter selection completes before the protected operation begins. A missing preferred adapter can select a declared complete fallback; selection never changes after native or companion effects start. Author adapters implement `Preflight`, `Activate`, `PrepareSave`, `CompleteSave`, `Suspend`, `Restore` and `CleanupFailure`. Transforming operations receive only declared companion copies in a transaction staging folder. LUZ validates those copies before publication and retains the first failure and original checkpoint when cleanup is incomplete.

Author provenance is shown honestly. A generated descriptor does not qualify unknown native members, prove core save behavior or confer “LUZ verified” status. Static generation and package checks must be followed by the mod author's disposable-save gameplay checks.

## Compatibility coverage

The versioned service currently owns activation, input-binding validation, clock advancement, inventory recovery inserts, object vendor registration and protected save routing. The migrated suite still has direct game references and raw native contracts. Those remain tied to their exact supported build. The coverage tool reports them explicitly.

An adapter update can change a mediated operation while its consumer remains unchanged. It cannot make an undeclared direct native access portable. Native inventory transactions retain their existing owners. Calls are never retried through a fallback after a mutation starts.

For a new game build with unchanged residual native contracts, the catalog can include a reviewed consumer grant. It binds the exact DLL hash, original native/metadata identities, target build and qualification digest. Both LUZ and the runtime require that grant. Another payload or an unqualified native layout is rejected; missing compiled members still fail validation.

Dormancy supports Better Time, City Life's versioned companion and Object Framework custom behaviors. Native object records remain under the game's serializer; dormant behaviors preserve the framework's object identities and values. Retired worker recovery, unknown required companions, missing serialization support and unsupported ownership block continuation.

The desktop's suspension/restoration picker currently exposes those maintained features. Third-party descriptors and custom adapters use the recovery lifecycle; author support does not automatically add an arbitrary feature to that picker. Greenhouse progression through Better Time's Wait is a separate mod integration and gameplay check. Generating Greenhouse support files does not establish correct farming, workers, wages or sales.

Returning data is validated by the owning module. Restoration retains newer native progress and does not inject an old native save over the working timeline. Previous dormant captures remain independently preserved when a feature is suspended again.

## Building and distribution

Run `python tools/build_continuity.py` before building or packaging the desktop application. It obtains the pinned official loader as compiler input, verifies its identity, and produces only LUZ's runtime, API, catalog, maintained descriptor templates and payload hashes. Game binaries are not required or distributed.

`python tools/package.py --rid all` includes that payload in every desktop package. The GitHub workflow builds matching version tags and publishes them as stable GitHub releases; manual and pull-request builds are also available. The launcher updater requires the platform ZIP and GitHub SHA-256 digest; pushing source alone does not update installed applications.

Source integration in the maintained mods uses `-p:LuzContinuity=true`. Default builds retain their standalone behavior. Migration output directories are separate from existing public and owner outputs. Migration identities are Better Time 2.3.0, Unofficial Patch 0.5.0, City Life 1.2.0, Object Framework 0.4.0 and Furniture Lab 0.2.0.

Protected game sessions initially require native Windows. Wine/Proton protected sessions are unsupported. Existing game launch and mod-management features remain available on the other launcher platforms.
