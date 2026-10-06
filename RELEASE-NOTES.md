# LUZ Civic Terminal 0.4.2

Fixes the recovery flow that could report a successful profile restore while an interrupted BepInEx installation still kept Apply and Play disabled. Maintenance now shows each pending operation at the top, with a direct action for its matching backup. Complete both actions if both are listed.

Missing or incomplete backups stay blocked with an explanation and an Export recovery diagnostics action. Loader restoration checks all original backup files before changing the installation. Existing profiles and recovery records are preserved when upgrading.

In LUZ 0.4.0 or newer, open **Maintenance > LUZ updates > Check LUZ updates**, then install the offered version. Users on 0.3.x need to download the ZIP for their operating system and run its installer once. Profiles and game files stay in place.

Includes the 0.4.1 dependency-range fix. Windows fixture and headless UI checks cover recovery; the reporter's own installation has not been tested. Linux/macOS builds remain experimental and have not been tested on native systems.
