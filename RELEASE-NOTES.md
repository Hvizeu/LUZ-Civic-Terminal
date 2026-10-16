# LUZ Civic Terminal 0.4.9

This release strengthens compatibility inspection and prevents incomplete recovered saves from being treated as successfully published.

- Compatibility checks support schema 2 compiled contracts, transitive generic inheritance, forwarded types and assembly-qualified signatures. Existing schema 1 metadata remains supported; unknown requirements remain Unverified.
- Recovery inspection requires agreement between the native save, required companions, receipt, journal and committed checkpoint. Incomplete publication remains blocked, with restartable restoration and retained diagnostic evidence.
- Routine compatibility checks avoid hashing archived recovery payloads repeatedly. Deployment still preserves those files, and recovery inspection verifies the referenced generations.
- Scanner cancellation, timeouts and bounded inputs remain enforced without process RAM ceilings.
- Shared recovery source hashes are checked during validation, and Git preserves the verified source bytes across platforms.

Existing configurations and plugin identities are preserved. Compatibility overrides remain explicit, valid for one attempt against the exact report, and cannot bypass save-integrity or cleanup failures. Ordinary profile application does not modify saves.

## Update

In LUZ 0.4.0 or newer, use **Maintenance > LUZ updates > Check LUZ updates**. Startup checks run at most once every 24 hours. Users on 0.3.x can download their platform ZIP and run its installer once. All four downloads include matching SHA-256 checksum files.
