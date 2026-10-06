0.4.3

- Resolve linked home and application roots before installation and library startup, fixing the linked-folder rejection on layouts such as Bazzite's /home alias. Links inside application payloads and managed mod data remain blocked.
- Add Remove LUZ Nexus registration to Windows Maintenance and the extracted installer. It removes LUZ's registration even after its application files were deleted, while preserving other managers and Windows' protected default choice.
- Label browser registration as a system-wide NXM change. Choosing a replacement manager remains a Windows Default apps action; pasting a Nivalis link does not change defaults.
- Linked-root and registry cleanup fixtures pass on Windows. Native Bazzite installation remains unverified.

0.4.2

- Identify interrupted profile deployments and BepInEx installations separately, with matching recovery actions at the top of Maintenance.
- Recover the exact backup recorded by the interrupted operation; historical restore buttons also route to the required recovery when applicable.
- Report remaining recovery blockers after a restore instead of telling users to Apply while Apply and Play are disabled.
- Show missing or unreadable recovery backups without clearing their safety records, and include recovery details in diagnostics.
- Validate all original loader backup files before changing game files during restoration.

0.4.1

- Fix valid BepInEx dependency ranges being rejected as version numbers, including the HUD Overhaul report `1.03 / >=1.03`.
- Match BepInEx 6 range rules for exact versions, bounds, wildcards, alternatives and prereleases.
- Existing profiles work with the fix without reimporting mods. Missing or incompatible dependencies still block deployment.

0.4.0

- Check the public GitHub repository for launcher updates automatically or on demand.
- Download and verify the platform package, install it for the current user, and restart LUZ while preserving profiles and game files.
- Add game-update diagnostics and a binding refresh that backs up the old BepInEx files first.
- Refresh existing Windows LUZ protocol registration when installing a new version, preserving the selected default handler.
- Linux/macOS updates remain untested on native systems.

0.3.2 — initial public preview

- Mod profiles, ZIP/DLL imports, existing-installation imports and deployment backups.
- Drag-to-reorder controls, dependency sorting and declared conflict checks.
- Recommended BepInEx installation from the official server, with loader backup and restore.
- Thunderstore catalogue and updates; optional Nexus API and NXM support, subject to account permissions.
- Import complete Object Studio collections into a profile.
- Windows x64, plus experimental Linux x64 and macOS Intel/Apple Silicon packages. Linux/macOS native operation and gameplay remain unverified.
