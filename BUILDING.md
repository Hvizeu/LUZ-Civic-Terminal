# Building LUZ

Requires .NET SDK 10 and Python 3.11 or newer. Game binaries and game sources are not needed to build or test the launcher.

```sh
dotnet build Desktop/Desktop.csproj -c Release
dotnet run --project Tests/Tests.csproj -c Release
python tools/package.py --rid win-x64
```

Other package targets are `linux-x64`, `osx-x64`, `osx-arm64` and `all`. Packaging runs the offline fixtures and headless UI checks, creates self-contained ZIPs, retains third-party notices, and validates the release payload. Run macOS packaging on macOS for a locally signed bundle; cross-built packages include a preparation helper. No packages are notarized.

Core owns package/profile state, deployment, platform paths, launcher updates and game-update recovery. Desktop owns Avalonia UI and explicit user actions. Tests use disposable game folders; their placeholder executables are never run. Update-process tests launch only the test runner.

To prepare a numbered release, keep the versions in Core/Core.csproj and Desktop/Desktop.csproj equal and update the release notes. Tag releases as `vMAJOR.MINOR.PATCH`. The updater accepts stable releases from Hvizeu/LUZ-Civic-Terminal, selecting `LUZ-Civic-Terminal-VERSION-RID.zip` with GitHub's SHA-256 asset digest. Upload each completed platform ZIP and its `.sha256` file before making the release visible. Drafts and prereleases are not offered by the updater.

Validate on each native platform before claiming native support. A build or headless run does not establish game compatibility, native keyring behavior or a working Wine/Proton setup.
