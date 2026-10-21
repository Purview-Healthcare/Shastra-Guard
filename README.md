# Shastra Guard

Small Windows helper for Purview Shastra (attendance). It brings Shastra to the front at login, unlock and before locking or shutting down, so people remember to punch in, punch out and mark breaks.

- `Shastra Guard Installer.zip`: what to install on each computer. See `READ ME FIRST.txt` inside.
- `version.txt`: the current version. Installed copies check this file at start and every 6 hours, and update themselves when it changes.
- `ShastraGuard.cs`: the program. Installed copies download this file when `version.txt` changes.

Do not edit `version.txt` by hand unless `ShastraGuard.cs` has the matching `CurrentVersion`.
