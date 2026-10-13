# LUZ Civic Terminal 0.4.8

LUZ now checks compiled mod contracts before applying a profile or launching Nivalis, and can prepare a captured save generation for recovery by a compatible Unofficial Patch.

- Profile checks identify missing game types, methods, fields and dependencies without running the inspected mods. Checks use the files selected by profile order for Apply and the installed files for Play.
- Binding checks verify the recognized loader's generation fingerprint. Missing or unverifiable bindings are reported as Unverified; Refresh game bindings remains available in Maintenance.
- Incompatible or unverified reports require an explicit acknowledgment for one attempt. Changed inputs invalidate it. File ownership, interrupted operations and save-integrity failures cannot be overridden; mod runtime guards still apply.
- Maintenance can prepare saved-data recovery with the game closed, preserving a verified source save and matching companions. Native recovery requires compatible Unofficial Patch and Object Framework components, a separate owner-launched game session and the explicit recovery action. Ordinary profile application does not modify saves.
- Interrupted recovery retains its journal and verified checkpoints. Unsupported companions or uncertain writes stop recovery rather than discarding state.

Static checks do not establish complete gameplay compatibility and do not repair third-party binaries. The tested Performance Fix 1.0.5 package references the removed ItemListUI.set_isUpdating(Boolean) method; newer packages need their own inspection.

Windows fixtures, headless UI checks and package checks passed. Linux x64 and macOS Intel/Apple Silicon packages are cross-built and package-checked; native operation remains unverified. macOS packages are not notarized. Native save recovery and gameplay acceptance remain pending.

In LUZ 0.4.0 or newer, use Maintenance > LUZ updates > Check LUZ updates. Startup checks run at most once every 24 hours. Users on 0.3.x can download their platform ZIP and run its installer once. Each download has a SHA-256 checksum file.
