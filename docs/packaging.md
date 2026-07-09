# KeySounds packaging notes

## Portable build

Recommended portable folder publish:

```bash
dotnet publish KeySounds.csproj -c Release -r win-x64 --self-contained true -o artifacts/portable/win-x64
```

Folder deployment is preferred for the current Avalonia/NAudio asset layout. Validate single-file separately before using it for release.

## Installer-ready build

Use the portable publish output as the installer input folder for WiX, Inno Setup, or MSIX.

Installer expectations:

- executable name: `KeySounds.exe`
- app data remains under `%LocalAppData%\KeySounds`
- no user data is installed into the program directory
- no test assemblies are included
- the fallback sound pack is materialized into app data on first run
- custom user sound packs remain in app data

## Start with Windows

The app uses the per-user Run key:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The value points to the current executable path with quotes and `--startup`. No admin rights are required.

## Code signing placeholder

Sign release outputs after publish and before installer packaging. Sign:

- `KeySounds.exe`
- installer executable/package

Do not commit certificates or private keys. Code signing helps Windows reputation and SmartScreen but does not replace testing.

## Upgrade behavior

App data is migrated in place. Back up `%LocalAppData%\KeySounds` before release-candidate upgrade tests.

## Uninstall considerations

The uninstaller may remove program files. User data under `%LocalAppData%\KeySounds` should be optional to remove and clearly disclosed.
