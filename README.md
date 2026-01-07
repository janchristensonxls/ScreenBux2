# ScreenBux2

A comprehensive Windows parental control application built with ASP.NET Core .NET 8.

## Architecture

ScreenBux2 consists of four main components:

### 1. Windows Service (ScreenBux.Service)
A privileged background Windows service that:
- Monitors application usage through Named Pipes communication
- Enforces policies defined in JSON configuration
- Implements process kill-tree functionality for closing applications and their children
- Provides a Named Pipes API for inter-process communication

### 2. Windows Agent (ScreenBux.Agent)
A WPF desktop application that:
- Detects foreground windows and active applications
- Reports application activity to the Windows Service via Named Pipes
- Implements graceful process closing when commanded by the Service
- Provides visual interface for monitoring status

### 3. Web Server (ScreenBux.WebServer)
An ASP.NET Core Web API that:
- Hosts a SignalR hub for real-time communication
- Provides REST API endpoints for policy management
- Bridges communication between web clients and the Windows Service
- Serves as the backend for remote management

### 4. Web Client (ScreenBux.WebClient)
A Blazor web application that:
- Connects to the Web Server via SignalR
- Displays real-time monitoring data
- Allows remote management of policies
- Provides a modern web-based control panel

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- Windows OS (for Service and Agent components)
- Visual Studio 2022 or later (recommended)

### Building the Solution

```bash
# Clone the repository
git clone https://github.com/janchristensonxls/ScreenBux2.git
cd ScreenBux2

# Restore dependencies and build
dotnet restore
dotnet build
```

### Running Components

#### 1. Start the Windows Service
```bash
cd src/ScreenBux.Service
dotnet run
```

Or install as a Windows Service:
```bash
sc create ScreenBuxService binPath="C:\path\to\ScreenBux.Service.exe"
sc start ScreenBuxService
```

#### 2. Start the Windows Agent
```bash
cd src/ScreenBux.Agent
dotnet run
```

#### 3. Start the Web Server
```bash
cd src/ScreenBux.WebServer
dotnet run
```

The API will be available at `https://localhost:7000` and `http://localhost:5000`

#### 4. Start the Web Client
```bash
cd src/ScreenBux.WebClient
dotnet run
```

The web interface will be available at `https://localhost:5001`

## Configuration

### Policy Configuration (policy.json)

The `policy.json` file defines the parental control rules. Example:

```json
{
  "EnableMonitoring": true,
  "CheckIntervalSeconds": 5,
  "LogActivity": true,
  "Policies": [
    {
      "ApplicationName": "chrome",
      "ExecutablePath": "chrome.exe",
      "Action": "TimeRestricted",
      "AllowedTimeWindows": [
        {
          "StartTime": "09:00:00",
          "EndTime": "17:00:00",
          "DaysOfWeek": [1, 2, 3, 4, 5]
        }
      ],
      "MaxUsageMinutesPerDay": 120,
      "BlockOnWeekdays": false,
      "BlockOnWeekends": true
    }
  ]
}
```

### Policy Actions
- **Allow**: Application is always allowed
- **Block**: Application is always blocked and will be closed immediately
- **TimeRestricted**: Application is only allowed during specified time windows

## Communication Flow

1. **Agent → Service**: The Windows Agent detects foreground applications and reports them to the Service via Named Pipes
2. **Service → Agent**: The Service evaluates policies and sends close commands back to the Agent when needed
3. **Web Client → Web Server**: The web interface connects to the Web Server via SignalR for real-time updates
4. **Web Server ↔ Service**: The Web Server can communicate with the Service for policy management and status updates

## Named Pipes API

The service exposes a Named Pipes endpoint at `ScreenBuxServicePipe` with the following message types:

- **ProcessReport**: Agent reports a detected process
- **CloseProcess**: Service commands Agent to close a process
- **GetPolicy**: Request current policy configuration
- **PolicyResponse**: Returns policy configuration

## SignalR Hub

The Web Server exposes a SignalR hub at `/monitoringHub` with the following methods:

- **GetStatus**: Request current service status
- **CloseProcess**: Request to close a specific process
- **ProcessDetected**: Server broadcasts detected processes (server → client)
- **PolicyUpdated**: Server broadcasts policy updates (server → client)

## REST API

### Policy Endpoints

- `GET /api/policy` - Get current policy configuration
- `PUT /api/policy` - Update policy configuration
- `POST /api/policy/reload` - Trigger policy reload in the service

## Development

### Project Structure
```
src/
├── ScreenBux.Shared/       # Shared models and contracts
├── ScreenBux.Service/      # Windows Service
├── ScreenBux.Agent/        # WPF Desktop Agent
├── ScreenBux.WebServer/    # ASP.NET Core Web API + SignalR
└── ScreenBux.WebClient/    # Blazor Web Application
```

### Key Technologies
- .NET 8.0
- ASP.NET Core
- SignalR
- WPF (Windows Presentation Foundation)
- Named Pipes
- Blazor

## Security Considerations

- The Windows Service should run with appropriate privileges to monitor and terminate processes
- Policy files should be protected with appropriate file system permissions
- SignalR connections should use authentication in production
- Consider implementing rate limiting on API endpoints
- Validate all policy configuration inputs

## License

See LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
