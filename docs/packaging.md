# KeySounds packaging notes

## Portable build

Recommended portable folder publish:

```powershell
dotnet publish KeySounds.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64-portable
```

Helper script:

```powershell
.\scripts\publish-portable.ps1
```

Expected executable:

```text
artifacts\publish\win-x64-portable\KeySounds.exe
```

Folder deployment is preferred for the current Avalonia/NAudio asset layout. Validate single-file separately before using it for release.

No installer is currently provided. The portable folder is the release artifact until WiX, Inno Setup, MSIX, or another installer path is added. The Phase 3 review publish measured 206.01 MiB / 216,018,885 bytes across 230 files. Re-measure the folder size for each release candidate because runtime and dependency changes can move it.

## Installer-ready build

Use the portable publish output as the installer input folder for WiX, Inno Setup, or MSIX.

Installer expectations:

- executable name: `KeySounds.exe`
- app data remains under `%LocalAppData%\KeySounds`
- no user data is installed into the program directory
- no test assemblies are included
- the fallback sound pack is materialized into app data on first run
- custom user sound packs remain in app data

## App data and diagnostics

Runtime data is stored under `%LocalAppData%\KeySounds`.

Diagnostics exports are written by default under `%LocalAppData%\KeySounds\diagnostics`.

Local error reports, when enabled, are written under `%LocalAppData%\KeySounds\reports`.

## Start with Windows

The app uses the per-user Run key:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The value points to the current executable path with quotes and `--startup`. No admin rights are required.

Example value:

```text
"C:\Path\To\KeySounds.exe" --startup
```

## Code signing placeholder

Sign release outputs after publish and before installer packaging. Sign:

- `KeySounds.exe`
- installer executable/package

Do not commit certificates or private keys. Code signing helps Windows reputation and SmartScreen but does not replace testing.

## Upgrade behavior

App data is migrated in place. Back up `%LocalAppData%\KeySounds` before release-candidate upgrade tests.

## Uninstall considerations

The uninstaller may remove program files. User data under `%LocalAppData%\KeySounds` should be optional to remove and clearly disclosed.

For a manual portable uninstall:

1. Exit KeySounds from the tray menu.
2. Delete the portable publish folder.
3. Optionally delete `%LocalAppData%\KeySounds`.
4. If Start with Windows was enabled, remove the `KeySounds` value from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
