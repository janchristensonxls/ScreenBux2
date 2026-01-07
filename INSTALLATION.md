# Installation Guide

## Prerequisites

- Windows 10/11 (for Service and Agent components)
- .NET 8.0 Runtime or SDK
- Administrator privileges (for installing the Windows Service)

## Installation Steps

### 1. Install the Windows Service

The Windows Service must be installed with administrator privileges to monitor and control applications.

#### Option A: Using SC (Windows Service Control)

```powershell
# Open PowerShell as Administrator
cd C:\path\to\ScreenBux2\src\ScreenBux.Service\bin\Release\net8.0

# Create the service
sc create ScreenBuxService binPath="C:\path\to\ScreenBux.Service.exe" start=auto

# Start the service
sc start ScreenBuxService

# Check service status
sc query ScreenBuxService
```

#### Option B: Using .NET CLI (Development)

For development and testing, you can run the service directly:

```bash
cd src/ScreenBux.Service
dotnet run
```

### 2. Configure Policy

Copy and edit the `policy.json` file to define your parental control rules:

```bash
# Copy the sample policy
cp policy.json C:\ProgramData\ScreenBux\policy.json

# Edit the policy file with your preferred text editor
notepad C:\ProgramData\ScreenBux\policy.json
```

Update `appsettings.json` in the Service project to point to your policy file:

```json
{
  "PolicyFilePath": "C:\\ProgramData\\ScreenBux\\policy.json"
}
```

### 3. Install the Windows Agent

The Agent should start automatically when a user logs in.

#### Option A: Add to Startup (User Mode)

1. Create a shortcut to `ScreenBux.Agent.exe`
2. Press `Win + R` and type `shell:startup`
3. Move the shortcut to the Startup folder

#### Option B: Run Manually

```bash
cd src/ScreenBux.Agent
dotnet run
```

### 4. Install the Web Server

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

The API will be available at:
- HTTPS: https://localhost:7000
- HTTP: http://localhost:5000

### 5. Access the Web Client

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

## Troubleshooting

### Service won't start

1. Check Windows Event Viewer for error messages
2. Verify the service executable path is correct
3. Ensure .NET 8.0 runtime is installed
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
