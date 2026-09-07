# Installation Guide

## Prerequisites

- Windows 10/11 (for Service and Agent components)
- .NET 10 Runtime or SDK
- Administrator privileges (for installing the Windows Service)

## Installation Steps

### 1. Install the Windows Service

The Windows Service must be installed with administrator privileges to monitor and control
applications. It should simply be installed and left **running** as a normal Windows Service -
it does *not* need to be run interactively, and it does not take a link code as a CLI argument
or startup parameter. Linking happens later, from the Agent's tray icon, over the already-running
Service's named pipe (see step 4 below); the Service just needs to already be up and listening.

#### Option A: Using SC (Windows Service Control)

```powershell
# Open PowerShell as Administrator
cd C:\path\to\ScreenBux2\src\ScreenBux.Service\bin\Release\net10.0

# Create the service
sc create ScreenBuxService binPath="C:\path\to\ScreenBux.Service.exe" start=auto

# Start the service
sc start ScreenBuxService

# Check service status
sc query ScreenBuxService
```

#### Option B: Using .NET CLI (Development)

For development and testing, you can run the service directly (still no link code needed here -
just leave it running and use the Agent's tray menu to link once it's up):

```bash
cd src/ScreenBux.Service
dotnet run
```

### 2. Configure Policy

The Service maintains its own local policy cache once linked to an account (see `docs/ARCHITECTURE.md`
for the two-store policy model) - you do not need to hand-author a `policy.json` up front for a normal
install. This step is only relevant if you want to seed a policy manually before the Service has ever
synced with the WebServer:

```bash
# Copy the sample policy
cp policy.json C:\ProgramData\ScreenBux\policy.json

# Edit the policy file with your preferred text editor
notepad C:\ProgramData\ScreenBux\policy.json
```

### 3. Install the Windows Agent

The Agent runs in the current user's session and lives in the system tray. It should start
automatically when a user logs in.

#### Option A: Add to Startup (User Mode)

1. Create a shortcut to `ScreenBux.Agent.exe`
2. Press `Win + R` and type `shell:startup`
3. Move the shortcut to the Startup folder

#### Option B: Run Manually

```bash
cd src/ScreenBux.Agent
dotnet run
```

### 4. Link the Device

With both the Service (step 1) and Agent (step 3) running, and the WebServer/WebClient reachable,
generate a link code from the parent's WebClient "Link Device" page (`POST api/devices/linkcode`,
valid for 15 minutes). Then, on the controlled machine:

1. Right-click the ScreenBux icon in the system tray.
2. Choose **Link Device...** from the context menu.
3. Enter the 8-character code and click **Link**.

The Agent sends the code to the already-running, already-elevated Service over the local named
pipe (`ScreenBuxServicePipe`); the Service redeems it against the WebServer and stores the
resulting device token in its local `device.json`. No CLI flags, no interactive/elevated run of
the Service, and no `LinkCode` config entry are needed for this - those were leftovers from an
earlier design and do not reflect how linking works today. (There *is* a legacy
`LinkCode`-in-`appsettings.json` fallback for headless auto-linking, but it is inactive unless you
explicitly add that key.)

### 5. Install the Updater

`ScreenBux.Updater` is an always-elevated Windows Service that installs/updates the Service and
Agent on this machine (they can't safely replace their own running executables). Two ways to
install it - see [`installer/README.md`](installer/README.md) for full details:

#### Option A: Quick start (PowerShell)

```powershell
dotnet publish src\ScreenBux.Updater -c Release -o .\publish\Updater
.\installer\Install-Updater.ps1 -PublishedOutputPath .\publish\Updater -ServerBaseUrl https://screenbux-api.azurewebsites.net
```

#### Option B: MSI (WiX)

```powershell
dotnet publish src\ScreenBux.Updater -c Release -o installer\ScreenBux.Updater.Installer\PublishedOutput
dotnet build installer\ScreenBux.Updater.Installer -c Release
msiexec /i installer\ScreenBux.Updater.Installer\bin\x64\Release\ScreenBux.Updater.Installer.msi SERVERBASEURL=https://screenbux-api.azurewebsites.net
```

> **Uninstall note**: because the Updater - not any package manager - installs the Service and
> Agent, removing the Updater alone would normally orphan them (left registered/running with no
> supported way to remove them). Both tools above guard against this: pass
> `-RemoveManagedComponents` to the PowerShell script, or simply uninstall the MSI (its uninstall
> automatically cleans up the managed Service and Agent first, unless this is a version upgrade).
> See the Uninstallation section below.

### 6. Install the Web Server

The Web Server can be hosted using IIS, Kestrel, or run as a standalone application.

#### Option A: As a Windows Service

```powershell
# Publish the application
dotnet publish -c Release -o C:\inetpub\ScreenBuxWebServer

# Install as Windows Service
sc create ScreenBuxWebServer binPath="C:\inetpub\ScreenBuxWebServer\ScreenBux.WebServer.exe" start=auto

# Start the service
sc start ScreenBuxWebServer
```

#### Option B: Development Mode

```bash
cd src/ScreenBux.WebServer
dotnet run
```

The API will be available at (per `Properties/launchSettings.json`; a standalone published/hosted
deployment can use whatever port you configure via `--urls`/`ASPNETCORE_URLS`):
- HTTPS: https://localhost:44323
- HTTP: http://localhost:5246

### 7. Access the Web Client

The Web Client can be accessed through a web browser.

```bash
cd src/ScreenBux.WebClient
dotnet run
```

Navigate to https://localhost:5001 in your web browser.

## Uninstallation

### Remove Windows Service

```powershell
# Stop the service
sc stop ScreenBuxService

# Delete the service
sc delete ScreenBuxService
```

### Remove Web Server Service

```powershell
# Stop the service
sc stop ScreenBuxWebServer

# Delete the service
sc delete ScreenBuxWebServer
```

### Remove Agent from Startup

Remove the shortcut from the Startup folder (`Win + R` → `shell:startup`).

### Remove the Updater (and optionally the Service/Agent it manages)

Because `ScreenBux.Updater` installed the Service and Agent outside of any package manager's
knowledge, uninstalling it requires explicit cleanup to avoid leaving them orphaned:

```powershell
# Remove the Updater only (Service/Agent keep running as-is):
.\installer\Install-Updater.ps1 -Uninstall

# Remove the Updater AND the managed Service and Agent (full removal):
.\installer\Install-Updater.ps1 -Uninstall -RemoveManagedComponents
```

If installed via MSI, `msiexec /x` (or Add/Remove Programs) automatically removes the managed
Service and Agent first via a built-in cleanup step, unless the uninstall is actually part of
a version upgrade.

## Troubleshooting

### Service won't start

1. Check Windows Event Viewer for error messages
2. Verify the service executable path is correct
3. Ensure .NET 10.0 runtime is installed
4. Check that the service account has appropriate permissions

### Agent can't connect to Service

1. Verify the Windows Service is running
2. Check that Named Pipes are enabled
3. Ensure no firewall rules are blocking local communication

### Web Client can't connect to Web Server

1. Verify the Web Server is running
2. Check the SignalR hub URL in `MonitoringService.cs`
3. Ensure CORS settings allow your client origin
4. Check firewall settings

## Configuration Files

- **Service**: `src/ScreenBux.Service/appsettings.json`
- **Web Server**: `src/ScreenBux.WebServer/appsettings.json`
- **Web Client**: `src/ScreenBux.WebClient/appsettings.json`
- **Policy**: `policy.json` (location configurable)

## Security Recommendations

1. Run the Windows Service with minimum required privileges
2. Protect the policy.json file with appropriate ACLs
3. Use HTTPS for all web communications in production
4. Implement authentication for the Web Server and Web Client
5. Regularly update the application and .NET runtime
6. Monitor service logs for suspicious activity
