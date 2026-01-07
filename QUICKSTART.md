# ScreenBux2 - Quick Start Guide

## Overview

ScreenBux2 is a comprehensive Windows parental control application built with ASP.NET Core .NET 8. It provides real-time monitoring and control of application usage through a distributed architecture.

## Quick Start (Development)

### Prerequisites
- .NET 8.0 SDK
- Windows OS (for Service and Agent)

### Running All Components

Open 4 separate terminal windows and run:

#### Terminal 1 - Windows Service
```bash
cd src/ScreenBux.Service
dotnet run
```

#### Terminal 2 - Windows Agent
```bash
cd src/ScreenBux.Agent
dotnet run
```

#### Terminal 3 - Web Server
```bash
cd src/ScreenBux.WebServer
dotnet run
```

#### Terminal 4 - Web Client
```bash
cd src/ScreenBux.WebClient
dotnet run
```

### Access Points

- **Windows Agent**: Desktop application window
- **Web Server API**: https://localhost:7000 (Swagger: /swagger)
- **Web Client**: https://localhost:5001

## Architecture at a Glance

```
┌─────────────────┐        Named Pipes        ┌──────────────────┐
│  Windows Agent  │◄─────────────────────────►│ Windows Service  │
│     (WPF)       │                            │  (Background)    │
└─────────────────┘                            └──────────────────┘
                                                        │
                                                        │ Policy
                                                        ▼
                                               ┌─────────────────┐
                                               │  policy.json    │
                                               └─────────────────┘

┌─────────────────┐        SignalR/REST       ┌──────────────────┐
│   Web Client    │◄─────────────────────────►│   Web Server     │
│    (Blazor)     │                            │  (ASP.NET Core)  │
└─────────────────┘                            └──────────────────┘
```

## Key Features

### 1. Windows Service
- ✅ Named Pipes server for IPC
- ✅ JSON-based policy configuration
- ✅ Process kill-tree functionality
- ✅ Real-time policy enforcement
- ✅ Windows Service installation support

### 2. Windows Agent
- ✅ Foreground window detection (Win32 API)
- ✅ Named Pipes client communication
- ✅ Graceful process closing
- ✅ Activity logging and display
- ✅ Service connection monitoring

### 3. Web Server
- ✅ SignalR hub for real-time updates
- ✅ REST API for policy management
- ✅ CORS configuration
- ✅ Swagger/OpenAPI documentation

### 4. Web Client
- ✅ Blazor Server application
- ✅ Real-time process monitoring
- ✅ SignalR client integration
- ✅ Modern Bootstrap UI
- ✅ Activity history display

## Communication Flow

1. **Agent Detects Process**: Windows Agent monitors foreground windows
2. **Agent Reports**: Sends process info to Service via Named Pipes
3. **Service Evaluates**: Checks against policy.json rules
4. **Service Commands**: Sends close command if policy violated
5. **Agent Executes**: Closes the application gracefully or forcefully
6. **Web Updates**: Web Server broadcasts events to connected web clients

## Policy Example

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

## Testing the Application

### Test Scenario 1: Block an Application

1. Edit `policy.json` to block notepad:
```json
{
  "ApplicationName": "notepad",
  "ExecutablePath": "notepad.exe",
  "Action": "Block",
  "BlockOnWeekdays": true,
  "BlockOnWeekends": true
}
```

2. Restart the Windows Service
3. Start the Windows Agent
4. Open Notepad
5. Watch it get closed automatically

### Test Scenario 2: Web Monitoring

1. Start all 4 components
2. Open Web Client (https://localhost:5001)
3. Navigate to "Monitoring" page
4. Click "Connect"
5. Switch between different applications
6. Observe real-time updates in the web interface

### Test Scenario 3: Time Restrictions

1. Configure a time-restricted policy
2. Change system time to outside allowed window
3. Try to use the restricted application
4. Observe it being closed

## Project Structure

```
ScreenBux2/
├── src/
│   ├── ScreenBux.Shared/          # Common models and contracts
│   │   ├── Models/                # Data models (ProcessInfo, AppPolicy)
│   │   ├── Messages/              # Named Pipes messages
│   │   └── Contracts/             # Interfaces
│   │
│   ├── ScreenBux.Service/         # Windows Service
│   │   ├── Services/              # Business logic services
│   │   │   ├── PolicyService.cs
│   │   │   ├── ProcessKillerService.cs
│   │   │   └── NamedPipeServerService.cs
│   │   └── Program.cs
│   │
│   ├── ScreenBux.Agent/           # WPF Agent
│   │   ├── Services/              # Agent services
│   │   │   ├── ForegroundWindowDetector.cs
│   │   │   ├── MonitoringService.cs
│   │   │   └── NamedPipeClient.cs
│   │   └── MainWindow.xaml
│   │
│   ├── ScreenBux.WebServer/       # ASP.NET Core API
│   │   ├── Controllers/           # REST API controllers
│   │   ├── Hubs/                  # SignalR hubs
│   │   └── Program.cs
│   │
│   └── ScreenBux.WebClient/       # Blazor Web App
│       ├── Components/Pages/      # Razor pages
│       ├── Services/              # Client services
│       └── Program.cs
│
├── policy.json                     # Policy configuration
├── README.md                       # Main documentation
├── API.md                          # API reference
├── INSTALLATION.md                 # Installation guide
└── QUICKSTART.md                   # This file
```

## Troubleshooting

### Agent can't connect to Service
- Ensure Windows Service is running
- Check Named Pipes are not blocked
- Verify pipe name is "ScreenBuxServicePipe"

### Web Client can't connect
- Check Web Server is running on https://localhost:7000
- Verify SignalR hub URL in MonitoringService.cs
- Check CORS settings

### Process not being blocked
- Verify policy.json syntax
- Check ApplicationName matches process name
- Restart Windows Service after policy changes
- Review service logs

## Development Tips

### Debugging
- Use Visual Studio's "Start Multiple Projects" feature
- Set breakpoints in Named Pipes message handlers
- Monitor Windows Event Viewer for service logs

### Testing Named Pipes
- Use the Agent as a test client
- Check Named Pipes with `pipelist` tool
- Monitor with Process Monitor (procmon)

### Testing SignalR
- Use browser developer tools (F12)
- Check WebSocket connections
- Monitor SignalR messages in Network tab

## Next Steps

1. ✅ **Completed**: Basic implementation of all 4 components
2. 📋 **Todo**: Add authentication and authorization
3. 📋 **Todo**: Implement usage tracking and reports
4. 📋 **Todo**: Add email notifications
5. 📋 **Todo**: Create installer package
6. 📋 **Todo**: Add unit and integration tests
7. 📋 **Todo**: Implement database for activity logs
8. 📋 **Todo**: Add mobile app support

## Support & Documentation

- **README.md**: High-level overview and getting started
- **API.md**: Complete API reference for Named Pipes, REST, and SignalR
- **INSTALLATION.md**: Production installation and deployment
- **QUICKSTART.md**: This file - quick development setup

## License

See LICENSE file for details.

---

**Built with**: .NET 8, ASP.NET Core, WPF, Blazor, SignalR, Named Pipes
