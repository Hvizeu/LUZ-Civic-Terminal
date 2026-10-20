# Building LUZ

Requires .NET SDK 10 and Python 3.11 or newer. Game binaries and game sources are not needed to build or test the launcher.

```sh
python tools/build_continuity.py
dotnet build Desktop/Desktop.csproj -c Release
dotnet run --project Tests/Tests.csproj -c Release
python tools/package.py --rid win-x64
```

CorpSec Terminal's UI and headless commands share `Core/CorpSec.cs`. A typical automated author release is:

```sh
"LUZ Civic Terminal" --corpsec --json scan <builds-folder>
"LUZ Civic Terminal" --corpsec --json inspect <final-mod-folder> <source-project>
"LUZ Civic Terminal" --corpsec --json analyze <saved-project.json>
"LUZ Civic Terminal" --corpsec --json generate <saved-project.json> recovery
"LUZ Civic Terminal" --corpsec --json validate <saved-project.json> recovery
"LUZ Civic Terminal" --corpsec --json package <saved-project.json> <output-folder> all recovery
```

Use the installed executable's actual path, with `.exe` on Windows. In PowerShell, prefix a quoted executable path with `&`. Replace the angle-bracket placeholders with your paths. `scan` and `inspect` return each saved project's `ProjectFile`; pass that path to later commands. The desktop provides the review/editing steps before automation. See [the CorpSec guide](CORPSEC-GUIDE.md).

The `integration` command writes a reusable `CorpSec.targets` file, buildable recovery-adapter template, lifecycle guide and reflection-only optional-runtime helper outside the distributable tree. Add an MSBuild import for the returned targets file to the author source project. It invokes support generation after Build, using the mode and executable path recorded when it was generated; ZIP packaging remains a separate `package` command. Regenerate after changing those settings, or override `LuzCorpSecExecutable`. Keep the integration files outside your public ZIP.

CLI exit code `0` means the requested operation passed; `2` means analysis or declaration validation needs author action; `1` means an exception or invalid command prevented completion. A package command blocked by invalid input can return `1`; read its diagnostic instead of treating every failure as an unresolved-question result. With `--json`, results go to stdout and exceptions to stderr. Successful package generation never claims in-game recovery passed.

Generate again from final build outputs so descriptor hashes match every shipped plugin, adapter and payload. Thunderstore dependency mappings use `plugin GUID | Author-Package-Version`; unresolved required mappings stop that preset. Desktop and CLI writes use the same authoring lease.

Other package targets are `linux-x64`, `osx-x64`, `osx-arm64` and `all`. Packaging runs the offline fixtures and headless UI checks, creates self-contained ZIPs, retains third-party notices and validates the release payload. Every ZIP includes the player guide, CorpSec walkthrough, continuity reference, quick start, release notes, changelog and this command reference. Run macOS packaging on macOS for a locally signed bundle; cross-built packages include a preparation helper. No packages are notarized.

The default fixtures build from this repository alone, including independent stateless, companion-file and custom-adapter examples. An additional workspace integration check uses real Object Framework outputs:

```sh
python tools/package.py --rid all --object-framework
```

That option requires both `Loader/bin/Release/net6.0` and `Loader/bin/Continuity/net6.0` in a sibling `NivalisFurnishings` source project. It fails if either is absent. Run the fixture directly with `dotnet run --project Tests/Tests.csproj -c Release -- --object-framework`. A clean public checkout reports that optional check as not run; it does not claim to have tested an unavailable mod build.

Core owns package/profile state, deployment, platform paths, launcher updates and game-update recovery. Desktop owns Avalonia UI and explicit user actions. Tests use disposable game folders; their placeholder executables are never run. Update-process tests launch only the test runner.

To prepare a numbered release, keep the versions in Core/Core.csproj and Desktop/Desktop.csproj equal and update the guides, CHANGELOG.md, RELEASE-NOTES.md and RELEASE-NOTES.txt. The latter is the GitHub release body used by the workflow. Store copy is maintained under `Store/Nexus`.

Tag releases as `vMAJOR.MINOR.PATCH`. The updater accepts stable releases from Hvizeu/LUZ-Civic-Terminal, selecting `LUZ-Civic-Terminal-VERSION-RID.zip` with GitHub's SHA-256 asset digest. Upload each completed platform ZIP and its `.sha256` file before making the release visible. Drafts and prereleases are not offered by the updater. The tag workflow packages all four targets from the public source checkout. A manual release must provide the same filenames, checksums and version parity.

Validate on each native platform before claiming native support. A build or headless run does not establish game compatibility, native keyring behavior or a working Wine/Proton setup.

## Shared recovery source

Core/Recovery contains the release snapshot of the shared Nivalis recovery source, compiled directly into Core. SOURCE-HASHES.json records its SHA-256 hashes. A clean checkout needs no sibling projects or additional runtime DLL. When updating the shared recovery implementation, copy all five files from the maintained NivalisShared source and refresh the hashes together; run the recovery fixtures before packaging.

Git preserves the recovery files byte-for-byte so checkout line-ending conversion cannot invalidate the manifest. Packaging verifies the manifest and, when present, the maintained sibling sources.
