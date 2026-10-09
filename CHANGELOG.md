# Changelog

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

- Fix installation through linked home/application folders, including Bazzite-style home paths. Native Bazzite use remains unverified.
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
