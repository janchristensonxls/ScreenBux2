# API Documentation

## Named Pipes API

The Windows Service exposes a Named Pipes server for inter-process communication.

### Connection Details

- **Pipe Name**: `ScreenBuxServicePipe`
- **Direction**: InOut (bidirectional)
- **Transport**: Named Pipes (local only)

### Message Format

All messages are JSON-formatted strings with a `MessageType` property.

### Message Types

#### 1. ProcessReport

Sent from Agent to Service to report a detected foreground process.

**Direction**: Agent → Service

**Request**:
```json
{
  "MessageType": "ProcessReport",
  "Timestamp": "2024-01-07T12:00:00Z",
  "Process": {
    "ProcessId": 1234,
    "ProcessName": "chrome",
    "WindowTitle": "Google Chrome",
    "ExecutablePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "DetectedAt": "2024-01-07T12:00:00Z"
  }
}
```

**Response**:
- If process is allowed:
```json
{
  "MessageType": "Response",
  "Timestamp": "2024-01-07T12:00:00Z",
  "Success": true,
  "Message": "Process allowed"
}
```

- If process should be blocked:
```json
{
  "MessageType": "CloseProcess",
  "Timestamp": "2024-01-07T12:00:00Z",
  "ProcessId": 1234,
  "Reason": "Application blocked by parental control policy"
}
```

#### 2. GetPolicy

Request current policy configuration from the Service.

**Direction**: Agent/Client → Service

**Request**:
```json
{
  "MessageType": "GetPolicy",
  "Timestamp": "2024-01-07T12:00:00Z"
}
```

**Response**:
```json
{
  "MessageType": "PolicyResponse",
  "Timestamp": "2024-01-07T12:00:00Z",
  "Configuration": {
    "EnableMonitoring": true,
    "CheckIntervalSeconds": 5,
    "LogActivity": true,
    "Policies": [
      {
        "ApplicationName": "chrome",
        "ExecutablePath": "chrome.exe",
        "Action": "Block",
        "AllowedTimeWindows": [],
        "MaxUsageMinutesPerDay": 0,
        "BlockOnWeekdays": true,
        "BlockOnWeekends": true
      }
    ]
  }
}
```

## REST API (Web Server)

The Web Server exposes REST endpoints for policy management.

### Base URL

- Development: `https://localhost:7000`
- Production: Configure in deployment

### Endpoints

#### GET /api/policy

Get the current policy configuration.

**Response**: 200 OK
```json
{
  "EnableMonitoring": true,
  "CheckIntervalSeconds": 5,
  "LogActivity": true,
  "Policies": [...]
}
```

**Error Responses**:
- 404 Not Found: Policy file not found
- 500 Internal Server Error: Error reading policy

#### PUT /api/policy

Update the policy configuration.

**Request Body**:
```json
{
  "EnableMonitoring": true,
  "CheckIntervalSeconds": 5,
  "LogActivity": true,
  "Policies": [...]
}
```

**Response**: 200 OK
```json
{
  "message": "Policy updated successfully"
}
```

**Error Responses**:
- 400 Bad Request: Invalid policy format
- 500 Internal Server Error: Error writing policy

#### POST /api/policy/reload

Trigger the Windows Service to reload its policy configuration.

**Response**: 200 OK
```json
{
  "message": "Policy reload requested"
}
```

## SignalR Hub

The Web Server hosts a SignalR hub for real-time communication with web clients.

### Hub URL

`wss://localhost:7000/monitoringHub` (or `ws://` for HTTP)

### Client Methods (Client → Server)

#### GetStatus

Request current service status.

**Parameters**: None

**Triggers**: `ReceiveStatus` event on caller

#### CloseProcess

Request to close a specific process.

**Parameters**:
- `processId` (int): Process ID to close

**Triggers**: `ProcessClosedResponse` event on caller

### Server Methods (Server → Client)

#### ProcessDetected

Broadcast when a new process is detected.

**Parameters**:
```typescript
{
  ProcessId: number;
  ProcessName: string;
  WindowTitle: string;
  ExecutablePath: string;
  DetectedAt: Date;
}
```

#### PolicyUpdated

Broadcast when the policy configuration is updated.

**Parameters**:
```typescript
{
  EnableMonitoring: boolean;
  CheckIntervalSeconds: number;
  LogActivity: boolean;
  Policies: AppPolicy[];
}
```

#### ReceiveStatus

Response to GetStatus request.

**Parameters**:
```typescript
{
  Timestamp: Date;
  Message: string;
}
```

#### ProcessClosedResponse

Response to CloseProcess request.

**Parameters**:
```typescript
{
  ProcessId: number;
  Success: boolean;
  Message: string;
}
```

## Policy Configuration Schema

### PolicyConfiguration

```typescript
interface PolicyConfiguration {
  EnableMonitoring: boolean;
  CheckIntervalSeconds: number;
  LogActivity: boolean;
  Policies: AppPolicy[];
}
```

### AppPolicy

```typescript
interface AppPolicy {
  ApplicationName: string;
  ExecutablePath: string;
  Action: "Allow" | "Block" | "TimeRestricted";
  AllowedTimeWindows: TimeWindow[];
  MaxUsageMinutesPerDay: number;
  BlockOnWeekdays: boolean;
  BlockOnWeekends: boolean;
}
```

### TimeWindow

```typescript
interface TimeWindow {
  StartTime: string;  // Format: "HH:mm:ss"
  EndTime: string;    // Format: "HH:mm:ss"
  DaysOfWeek: number[];  // 0=Sunday, 1=Monday, ..., 6=Saturday
}
```

### PolicyAction Enum

- `Allow` (0): Application is always allowed
- `Block` (1): Application is always blocked
- `TimeRestricted` (2): Application is only allowed during specified time windows

## Example Usage

### C# Client Example

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

var pipeName = "ScreenBuxServicePipe";
using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
await client.ConnectAsync();

var message = new {
    MessageType = "GetPolicy",
    Timestamp = DateTime.UtcNow
};

var json = JsonSerializer.Serialize(message);
var bytes = Encoding.UTF8.GetBytes(json);
await client.WriteAsync(bytes, 0, bytes.Length);

var buffer = new byte[4096];
var bytesRead = await client.ReadAsync(buffer, 0, buffer.Length);
var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
Console.WriteLine(response);
```

### JavaScript/TypeScript SignalR Client Example

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://localhost:7000/monitoringHub")
  .withAutomaticReconnect()
  .build();

// Register handlers
connection.on("ProcessDetected", (processInfo) => {
  console.log("Process detected:", processInfo);
});

connection.on("PolicyUpdated", (config) => {
  console.log("Policy updated:", config);
});

// Start connection
await connection.start();

// Request status
await connection.invoke("GetStatus");

// Close a process
await connection.invoke("CloseProcess", 1234);
```

## Error Handling

All API endpoints and SignalR methods should implement proper error handling:

- Network errors: Retry with exponential backoff
- Service unavailable: Display user-friendly error message
- Invalid data: Validate before sending
- Permission denied: Check service permissions

## Rate Limiting

Consider implementing rate limiting for:
- Policy update requests (max 1 per minute)
- Process close requests (max 10 per minute)
- Status requests (max 60 per minute)

## Authentication & Authorization

**Important**: This implementation does not include authentication or authorization. For production use, you should:

1. Implement authentication for the Web Server API
2. Use JWT tokens or similar for SignalR connections
3. Implement role-based access control (RBAC)
4. Secure Named Pipes with appropriate ACLs
5. Use HTTPS for all external communications
