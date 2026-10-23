# LUZ Civic Terminal 0.7.8

Changes since 0.7.7:

- Accepts the same universal mod ZIP for manual installation and LUZ import.
- Activates a mod's embedded Continuity contract only when the protected runtime is present. Ordinary profiles no longer reject a universal mod because that optional runtime is absent.
- Keeps protected sessions strict: the runtime, catalog, native build identity and exact mod payload must still match before protected play.

## Installation

Download the ZIP for your platform and extract the whole archive outside the game folder. Run its installer. .NET is included. Existing users on LUZ 0.4.0 or newer can use Maintenance > LUZ updates; 0.3.x requires a manual installation. Profiles and author settings remain in LUZ's separate data folder.

Windows is the main desktop build. Linux and macOS builds are experimental; macOS packages are not notarized. Protected game sessions require native Windows, catalogued Nivalis Nights build 26118043 or retained recovery identity 25984046, BepInEx 6.0.0-be.788 and compatible mods. Ordinary mod management works without protected saves.

[Source and guides](https://github.com/Hvizeu/LUZ-Civic-Terminal) | [Support my work](https://ko-fi.com/henriquevizeu)
