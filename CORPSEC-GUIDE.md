# CorpSec Terminal: your first mod package

This guide follows the LUZ 0.7.7 interface from a built mod to a finished ZIP. You do not need to edit JSON, contact LUZ's maintainer or arrange its support folder by hand.

CorpSec prepares support declarations and packages. It cannot infer every mod's saved-data rules or write custom recovery behavior for you. The steps below explain which choices are yours and what each result means.

## Before you start

Compile your mod and keep its final DLL, required assets, companion libraries, licences and credits in the build/release folder you intend to ship. Source alone is not a built mod.

Use a stable folder that receives later builds. You can select a parent project folder containing several builds; selecting a final output folder makes the choice narrower. Keep exported ZIPs in a separate output folder so they do not become additional build candidates.

CorpSec settings and analysis records are stored in LUZ's private author workspace. Switching between Civic Terminal and CorpSec preserves the selected player profile. Authoring does not Apply a profile or launch the game.

## 1. Add your build folder

1. Open **CorpSec Terminal** at the top of LUZ.
2. Click **Add mods folder** on the left.
3. Select the folder containing your built mod DLLs or release ZIPs.
4. Choose your mod in **My mods**.
5. Expected: **Project** shows its name, plugin identity, version, **Watched folder** and **Current build**.

The My mods list scrolls and sorts projects by name. One selected folder can contain several mods.

Use **Add one ZIP or DLL** for a one-off input. A watched folder is the repeat-release route: you do not need to open a new ZIP for every version.

Scans skip linked subfolders and development records. A project containing a linked documentation or analysis directory is allowed. Links inside the actual package payload are rejected; put real required assets in the selected build.

## 2. Review Project

1. Check **Current build**. It must identify the version and output you intend to distribute.
2. If **Build edition to follow** appears, choose the standalone or recovery edition deliberately. A shared plugin GUID does not make them interchangeable.
3. Enter **Display name**, **Author**, **Description**, **Website** and **Support contact**. Use a working mod page, issue tracker or contact that tells players where to report a problem.
4. Optionally choose **Choose source folder** to let static analysis inspect recognizable file-writing code.
5. Switch to **LUZ Support** to save your entries.

**Check for a new build** refreshes this project. **Scan for new builds** refreshes saved watched folders. Your author choices are retained. If a build is unavailable or its identity conflicts with saved support, resolve the displayed action before generating recovery support.

## 3. Analyze the build

1. In **LUZ Support**, click **Analyze this build**.
2. Read **Detected information and next actions**.
3. Review imported declarations and any suggested saved files.
4. Answer **Where does this mod keep persistent state?** using the table below.

CorpSec reads metadata, assets, existing support and optional source without running the mod or its build scripts. A possible file-writing call is evidence to review, not proof that the mod owns that file. No detected writes does not prove that a mod has no persistent state.

| Choice | Use it when |
| --- | --- |
| **No extra persistent state** | The mod has no owned data that must survive a protected save or restoration. Disposable diagnostics alone do not need recovery. |
| **Separate save or configuration files** | The mod owns supported per-slot files outside the native game save. Declare them in the next step. |
| **State stored inside the game save** | Recovery depends on native saved records. Keep a valid detected integration or supply the required implementation and compiled contracts. |
| **Custom recovery adapter** | Preserving or transforming state needs mod-specific code. Follow the custom-adapter section below. |
| **I don't know yet** | Ownership or recovery behavior remains unclear. Recovery stays incomplete; a normal package can still be built from a standalone binary. |

Do not choose no extra state merely to clear an action. Shared configuration and game-embedded data cannot safely be turned into invented per-slot files.

## 4. Review owned files, if your mod has them

Skip this step for a mod with no extra persistent state.

1. Select a file suggestion and click **Review selected file suggestion**, or click **Add owned file**.
2. Set **Location** to `saves` for the game's save directory or `config` for `BepInEx/config`.
3. Enter the real relative path. Use `{slot}` where the save-slot identifier belongs.
4. Choose **Slot naming** to match your mod's existing naming rule: `raw`, `lower`, `upper`, `sha256` or `sha256-upper`.
5. Choose **Validation**. `opaque` preserves file bytes; `json-revision` expects the supported revision-aware JSON format.
6. Select **This file is required to restore the mod** only when restoration cannot proceed without it.
7. Click **Save file rule**. Expected: the rule appears under **Owned save and configuration files**.

For example, a mod that already writes `MyMod/<slot>.json` beneath `BepInEx/config` can declare `config`, `MyMod/{slot}.json` and `raw`. This is an example naming scheme, not a filename to copy into every mod.

Check suggested paths carefully, including the slot placeholder. Do not claim another mod's file, use an absolute path, or use `..` to reach outside the selected root. A shared file without safe slot ownership needs a supported integration or custom staged transformation. An unresolved rule remains a required action.

Existing validated declarations can supply fields beyond this simple editor. Preserve their schema bounds and semantics; an unfamiliar format needs investigation rather than selecting a validation label by guesswork.

## 5. Generate support files

1. Resolve the required actions in **LUZ Support**.
2. Leave **Advanced recovery implementations** alone unless your mod needs an implementation or fallback that is not already detected.
3. Scroll down and click **Generate support files**.
4. Expected: LUZ opens **Validation** and reports **Package checks passed** and **Recovery declaration checks passed**.

The generated package tree contains a `LUZ` folder beside your plugin:

```text
BepInEx/
  plugins/
    your-mod/
      YourMod.dll
      required assets and companion libraries
      LUZ/
        mod-support.json
        adapters/                 only when you supply adapter DLLs
```

CorpSec binds the support record to the exact final plugin, payload and adapter files. It does not rewrite your DLL. Generate again after changing any shipped file. An old descriptor can provide settings to review; its old hashes do not qualify a new build.

## 6. Read Validation

| Result | Meaning |
| --- | --- |
| **Package checks passed** | The generated tree passes package checks. Finished ZIPs also go through LUZ's real importer and deployment planner in disposable directories. |
| **Recovery declaration checks passed** | Required declarations, identities and selected implementations satisfy static validation. |
| **In-game recovery has not been tested** | Packaging did not run the mod through a save/reload or restoration. This remains visible after successful generation. |

Expand results to read details. **Validate current generated files** checks the generated tree again. Changing the mod's build requires regeneration.

For **Normal package**, recovery is omitted and recovery declaration success is not required. Do not describe that export as providing recovery support.

## 7. Build the Nexus/manual ZIP

1. Open **Package**.
2. Select **Package with LUZ recovery support**, or **Normal package** if you intend to distribute the standalone mod without recovery.
3. Click **Choose output folder** and select your release folder. LUZ remembers it.
4. Review **Package file list**. It should contain the selected mod, required assets and the intended support files.
5. Click **Build Nexus ZIP**.
6. Expected: the status bar says **Built** followed by the archive name. The output folder contains that ZIP and a matching `.sha256` file.

Each build refreshes the selected edition, regenerates the requested package tree, validates it and writes the archive. A successful build does not upload it or install it into your player profile.

A normal package omits incomplete LUZ support and generated adapters. A binary with a direct LUZ dependency cannot be exported as if it worked without LUZ; select its standalone edition instead.

CorpSec filters saves, personal configuration, credentials, logs, debug symbols and development files. It rejects game binaries, loader bundles and duplicate LUZ runtimes. Required assets, licences, credits and intentional distributable configuration defaults belong in your final build input. Review the resulting list before distributing.

## 8. Build the Thunderstore ZIP

1. Stay in **Package** and review **Thunderstore package name**.
2. For each required plugin dependency, enter its real store mapping, one per line:

   ```text
   plugin GUID | Author-Package-Version
   ```

3. Use the exact plugin GUID and the published package identifier/version. If there are no required plugin mappings, leave the field blank.
4. Click **Build Thunderstore ZIP** or **Build both**.
5. Expected: a Built message names the archives and each has a checksum beside it.

LUZ generates the Thunderstore metadata and package structure from your project. If a dependency cannot be mapped, it reports the missing mapping rather than inventing one. The Nexus and Thunderstore presets use the same selected binaries. Review store metadata, artwork, licences and requirements before uploading.

## 9. Repeat for the next release

1. Build the new DLL and assets into the watched folder.
2. Select the saved project in My mods.
3. Use **Check for a new build** to review its version and edition, or build from Package to refresh as part of packaging.
4. Review any newly unresolved actions.
5. Build the requested ZIPs.

Your support contact, ownership decisions, selected edition and output folder stay saved. You do not need to reopen the previous ZIP or rebuild the project setup.

To remove an entry, open **Project**, scroll to **Remove from My mods** and confirm. LUZ removes its private author settings and generated workspace. Source, builds and exported packages remain. Routine scans keep that project hidden; **Add mods folder** restores discovery when you choose to add it again.

## Optional build automation

1. Finish the guided setup and choose the package mode.
2. In LUZ Support, choose **Generate build integration**.
3. Follow the reported path to `CorpSec.targets` and the separate SDK example files.
4. Add an MSBuild import for that targets file to your source project. The generated target runs support generation after Build; it does not build release ZIPs.
5. Use the `--corpsec package` command in your release script when you also want ZIP creation. See [BUILDING.md](BUILDING.md) for commands and exit codes.

Keep these source helpers and private settings outside public packages. The targets file records the current LUZ executable and package mode. Regenerate it after moving/updating the executable or changing that mode, or supply `LuzCorpSecExecutable` in your build.

## Custom recovery implementations

Use a custom adapter when ordinary file rules cannot preserve the mod correctly. The generated SDK includes a self-contained adapter project, lifecycle instructions and an optional-runtime helper. Its methods deliberately report incomplete implementation until you replace them.

1. Generate the build integration and copy the SDK example into your source workspace.
2. Implement preflight, activation, save preparation/completion, suspension, restoration acknowledgement and failure cleanup for your mod.
3. Build the adapter using the generated instructions.
4. In **LUZ Support > Advanced recovery implementations**, add its DLL and public type, then declare the capabilities and preferred/fallback implementations it provides.
5. Generate support from the final plugin and adapter builds, validate and package.

Adapter code runs only inside the game runtime. File transformations operate on declared staged copies. LUZ chooses a complete implementation before effects begin; it does not retry a partly completed transaction through a fallback.

When LUZ is absent, optional integration can retain ordinary play. When a protected session is active and its integration fails, the affected operation must stop. Never turn that failure into an unprotected save.

## Existing integrations: Object Framework

For an Object Framework recovery build, CorpSec can reuse the maintained owner `framework` and capabilities `module`, `catalogue` and `behaviours` when identity and compiled integration requirements match. Retain both `NivalisFurnishings.dll` and `NivalisFurnishings.Core.dll`, plus required assets.

Select the recovery edition explicitly. Select the standalone edition separately for a normal package. Do not replace the maintained owner with a new name; restoration acknowledgements use that established identity.

## Common actions and how to resolve them

| Message or problem | Next step |
| --- | --- |
| Enter a support contact | Fill in Project > Support contact and switch sections to save. |
| Tell LUZ where this mod keeps persistent state | Choose the correct storage answer; use I don't know yet if unresolved. |
| Compile current Nivalis contracts into the final DLL | The selected native/embedded implementation requires a compiled contract. Integrate it in source and rebuild. Stateless support does not need an unrelated native capability. |
| Declare this save file | Review the evidence, confirm ownership and enter its actual slot mapping. |
| Rebuild support files for this DLL | Regenerate after the final build; do not edit hashes to conceal changed contents. |
| Missing adapter or incomplete implementation | Build and register the adapter, or select a complete supported implementation before exporting recovery support. |
| No mod found | Select a folder containing a built BepInEx plugin. Verify the DLL is present and not only a source project or linked payload. |
| Recovery check failed | Expand the result and fix its stated identity, ownership or implementation problem. Normal export remains available for a standalone edition. |

## Check the mod in the game

Use disposable save copies. Record your mod version, game/loader build, support files and outcomes.

1. Test the standalone edition without LUZ. Expected: ordinary features and saves work without requiring the optional integration.
2. Import the recovery package into a test profile and enable protected saves on a disposable source save. Apply, then explicitly launch through LUZ.
3. Save and reload. Expected: native progress and declared mod data agree, with no duplicated inventory, rewards or other effects.
4. For supported suspension/restoration, exercise the declared feature and its fallback. Expected: preserved state returns only after its owning implementation validates restoration.
5. Exercise your adapter's failure paths. Expected: original checkpoints and first errors remain, and failed protected operations do not continue as unprotected saves.

A stateless example, such as Behavior Inspector with only disposable diagnostic output, needs no invented companion files or custom adapter. Passing that example's package checks does not prove recovery for a stateful mod such as Greenhouse.
