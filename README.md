# Shastra Guard

Small Windows helper for Purview Shastra (attendance). It brings Shastra to the front at login, unlock and before locking or shutting down, so people remember to punch in, punch out and mark breaks.

## Install on a computer (about 1 minute)

1. The employee signs in to Windows with their own account. Purview Shastra must be installed as an app, with its icon on the desktop.
2. Click Start, type `PowerShell`, right-click **Windows PowerShell** and choose **Run as administrator**. Enter the admin password.
3. Paste this line and press Enter:

```
irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/setup.ps1 | iex
```

4. Wait for the green DONE message. Test: press Windows + L once (Shastra appears), then again (it locks).

To remove it, use the same steps with:

```
irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/unsetup.ps1 | iex
```

## Files

- `setup.ps1` / `unsetup.ps1`: the one-line installer and uninstaller above.
- `Shastra Guard Installer.zip`: the same installer as a zip, for computers without internet access to GitHub. Browsers may flag the zip, so prefer the one-line install.
- `version.txt`: the current version. Installed copies check this file at start and every 6 hours, and update themselves when it changes.
- `ShastraGuard.cs`: the program. Installed copies download this file when `version.txt` changes.

Do not edit `version.txt` by hand unless `ShastraGuard.cs` has the matching `CurrentVersion`.
