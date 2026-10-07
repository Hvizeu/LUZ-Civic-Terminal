# LUZ Civic Terminal 0.4.5

Fixes LUZ refusing to select a game's new location after moving Nivalis with Steam.

Open **Maintenance > Detect Steam install** or **Choose game folder**, select the new location, and confirm that it is the same moved installation. LUZ keeps your profiles and reconnects their profile and BepInEx restore points. Complete any pending recovery, then review and apply your profile again.

Configuration capture now stages files before replacing cached settings, and a missing configuration folder after a move no longer erases the saved copy. File access errors include more context, and an unreadable loader or game log no longer prevents exporting the other diagnostics.

**Registry compatibility:** this release upgrades the library registry format to record installation history. The previous registry is backed up during migration. Older LUZ versions cannot open the upgraded registry; keep using 0.4.5 or newer after updating.

Windows filesystem fixtures and headless UI checks passed. Linux x64 and macOS Intel/Apple Silicon packages are cross-built and package-checked; native operation on those systems remains unverified. The original reporter's specific permission failure has not been reproduced.

In LUZ 0.4.0 or newer, use **Maintenance > LUZ updates > Check LUZ updates** to get this release. Users on 0.3.x need to download their platform ZIP and run its installer once. SHA-256 checksum files accompany each download.
