# PC Doctor

PC Doctor is a Windows desktop utility for checking common PC health issues, finding driver updates, and running selected troubleshooting fixes. It is built with C#/.NET 8 and WPF.

## Features

- Scan device status, touchpad detection, storage, memory, restart state, and Windows Update availability.
- Search Windows Update for driver updates, select updates to install, and export a driver backup.
- Browse troubleshooting fixes and run selected fixes. A restore point is created before running fixes.
- Read supported BIOS settings and, where available, enable a selected setting.
- Export a PDF diagnostic report or copy a redacted text report.
- Keep the app in the system tray and periodically check for problems while it is hidden.
- Optionally check an HTTPS update feed for app and fix-definition updates.

Some checks depend on Windows features and hardware support, so results can vary between PCs.

## Requirements

- Windows 10 or Windows 11 (x64).
- To build from source: .NET 8 SDK.
- Administrator approval to run the app. The application manifest requests elevation because some diagnostic and repair operations require it.

## Build and run from source

From the `PCDoctor` project directory:

```powershell
dotnet restore
dotnet build
dotnet run
```

The app starts elevated. If launching from a terminal, run the terminal as Administrator first; otherwise Windows may prevent `dotnet run` from starting the app.

## Publish a standalone executable

Run the included `Build.bat` from the `PCDoctor` project directory, or use:

```powershell
dotnet publish .\PCDoctor.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\dist
```

The executable is written to `dist\PCDoctor.exe`. It includes the .NET runtime and does not require the .NET desktop runtime to be installed. Right-click the executable and choose **Run as administrator** (or approve the Windows elevation prompt) to launch it. The first launch may take a little longer while the self-contained app initializes.

## Using the app

- **Dashboard:** scan the PC and review findings; the one-click action scans and searches Windows Update for drivers.
- **Drivers:** search, select, and install updates offered by Windows Update, or export installed drivers to a backup folder in your user profile.
- **Fix Center:** search for matching fixes, review the selected actions, then run them if appropriate.
- **BIOS:** inspect supported settings. Changing firmware settings can affect the PC; review the confirmation prompt carefully.
- **Log:** review scan, update, and repair activity.

Closing the window hides PC Doctor in the system tray. Use the tray icon's **Exit** command to close it completely.

## Configuration and data

The app stores its settings at `%AppData%\PCDoctor\settings.json`. Settings are created on first launch. `UpdateUrl` can be set to an HTTPS URL for an update feed; it is empty by default, so update checks are not configured until you provide one.

An update feed has this shape (see `update.sample.json`):

```json
{
  "version": "1.3.0",
  "url": "https://your-host.example/pcdoctor/PCDoctor-1.3.0.exe",
  "sha256": "SHA256_HEX_OF_THE_EXE",
  "notes": "What changed in this version",
  "fixesUrl": "https://your-host.example/pcdoctor/fixes.json",
  "fixesSha256": "SHA256_HEX_OF_FIXES_JSON"
}
```

The app downloads update files only from HTTPS URLs and verifies their SHA-256 hashes before using them. Leave the `fixesUrl` and `fixesSha256` fields empty if you are not distributing updated fix definitions.

Additional fixes can be supplied in `fixes.json` next to the executable or in `%AppData%\PCDoctor\fixes.json`. Each entry defines an `id`, `title`, `desc`, optional search `keys`, and `steps`. Supported steps are:

```json
[
  { "log": "A message to show in the log" },
  { "cmd": "command and arguments" },
  { "open": "program or URL" },
  { "delete": "path to delete" }
]
```

Only use fix definitions from a source you trust: command and delete steps can make changes to the PC.

Diagnostic checks use local Windows management and registry information. Driver searches and installs use Windows Update. Update-feed access occurs only when an HTTPS update URL is configured. Reports can contain device and system details; review and redact them before sharing.
