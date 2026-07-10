# Device Linking in ScreenBux

This document explains how a controlled device (a child's PC) is linked to a parent
account, both from the **user's point of view** and from a **technical/architectural**
perspective.

---

## 1. User Guide

### Prerequisites

| What | Where |
|---|---|
| ScreenBux WebServer | Running (dev: `https://localhost:44323`) |
| ScreenBux WebClient | Running (dev: `https://localhost:5001`) |
| ScreenBux Service | Running on the **child's PC** (run as Administrator for process enforcement) |
| ScreenBux Agent | Running on the **child's PC** (tray/desktop app) |

### Step-by-step

#### On the parent's browser

1. Open the ScreenBux WebClient and **register or sign in** to your parent account.
2. Click **Link Device** in the navigation menu.
3. Click **Generate Code**.  
   An 8-character code (e.g. `2BYJAEBK`) is displayed.  
   The code is valid for **15 minutes**.
4. Share the code with whoever is at the child's PC (tell them verbally, write it on a
   sticky note, send a message — the code itself is not secret for the duration it is
   valid).

#### On the child's PC (ScreenBux Agent)

5. Open the **ScreenBux Agent** window.  
   If the device is not yet linked, a **"Link Device to Parent Account"** panel is
   visible at the top of the window.
6. Type the 8-character code into the text field and click **Link This Device**.
7. Wait a moment. One of two things happens:
   - ✅ **"Device linked successfully!"** — the panel disappears permanently.
	 The Service will now receive policy updates and enforce parental controls for
	 this account automatically.
   - ❌ An error message appears (wrong code, expired code, server unreachable).
	 Ask the parent to generate a new code and try again.

#### After linking

- The Link Device panel in the Agent is hidden on all future launches — it is no
  longer needed for this machine.
- The parent can see the device listed on the **Link Device** page in the WebClient.
- Policy changes made in the WebClient take effect on the child's PC within seconds
  (via the Service's background sync loop).

---

## 2. Technical Architecture

### Components involved

```
Parent Browser
  └─ WebClient (Blazor)          ← parent UI
		│  REST
		▼
  WebServer (ASP.NET Core)       ← authority; owns the database
		│  REST (redeem code)
		▼
  Service (Worker, child's PC)   ← enforcement engine
		│  Named Pipe
		▼
  Agent (WPF, child's PC)        ← child-side desktop app
```

### Data stores

| Store | Location | Content |
|---|---|---|
| SQL Server DB | WebServer host | Accounts, Devices, DeviceLinkCodes, PolicyDocuments |
| `device.json` | `%CommonApplicationData%\ScreenBux\` | DeviceId, MachineKey, DeviceToken |
| `policy.json` | same folder | Local policy cache (written by Service) |

---

### Flow diagram

```
Parent                WebClient           WebServer           Service              Agent
  │                      │                   │                   │                   │
  │── Click "Generate" ─►│                   │                   │                   │
  │                      │── POST /linkcode ►│                   │                   │
  │                      │                   │  Creates DB row   │                   │
  │                      │◄── {Code, Exp} ───│  DeviceLinkCode   │                   │
  │                      │                   │                   │                   │
  │◄── Displays code ────│                   │                   │                   │
  │                      │                   │                   │                   │
  │  (shares code out-of-band with child's PC)                   │                   │
  │                      │                   │                   │                   │
  │                      │                   │                   │◄── User enters ───│
  │                      │                   │                   │     code + click  │
  │                      │                   │                   │                   │
  │                      │                   │                   │──LinkDeviceRequest►│ (Named Pipe)
  │                      │                   │◄── POST /redeem ──│                   │
  │                      │                   │  Validates code   │                   │
  │                      │                   │  Creates Device   │                   │
  │                      │                   │  Issues JWT       │                   │
  │                      │                   │── DeviceToken ───►│                   │
  │                      │                   │                   │  Saves device.json│
  │                      │                   │                   │──LinkDeviceResponse►│
  │                      │                   │                   │                   │ Hides panel
```

---

### Named Pipe: Agent ↔ Service

The Agent and Service run as separate processes on the same machine. They communicate
over a **Windows Named Pipe** (`ScreenBuxServicePipe`).

| Message | Direction | Purpose |
|---|---|---|
| `LinkDeviceRequest` | Agent → Service | Carries the 8-character code entered by the user |
| `LinkDeviceResponse` | Service → Agent | Returns `Success`, human-readable `Message`, and `DeviceId` |
| `ProcessReport` | Agent → Service | Reports foreground window/process to enforce policy |
| `CloseProcessCommand` | Service → Agent | Instructs Agent to close a blocked process |
| `GetPolicy` / `PolicyResponse` | Agent ↔ Service | Agent fetches current policy rules |

All messages are JSON-serialised and share a `MessageType` string discriminator defined
in `ScreenBux.Shared/Messages/NamedPipeMessages.cs`.

The pipe runs in **Message** mode (`PipeTransmissionMode.Message`) so each write is
received as a complete, delimited unit — no length-prefix framing needed.

---

### Code redemption: Service ↔ WebServer

When the Service receives a `LinkDeviceRequest` it calls
`DevicePolicySyncService.RedeemCodeAsync(code)`, which:

1. Reads the local `DeviceState` (`device.json`) to get the stable `MachineKey`
   (a GUID generated once on first run, uniquely identifying this installation).
2. `POST api/devices/redeem` with:
   ```json
   {
	 "Code": "2BYJAEBK",
	 "DeviceName": "DESKTOP-ABCD",
	 "MachineKey": "<guid>"
   }
   ```
3. The WebServer (`DevicesController`):
   - Looks up the `DeviceLinkCode` row — fails if not found, already redeemed, or expired.
   - Creates (or updates) a `Device` row tied to the parent `Account`, recording
	 `AccountId`, `ChildProfileId`, `MachineKey`, `Name`, and `LinkedAt`.
   - Marks the code as redeemed (`RedeemedAt`, `RedeemedByDeviceId`).
   - Issues a **device-scoped JWT** (`token_type: device`, claims: `account_id`,
	 `device_id`) via `JwtTokenService.CreateDeviceToken`.
   - Returns `{ Token, DeviceId }`.
4. The Service writes `DeviceId` and `DeviceToken` into `device.json`.

From this point the device is linked. Subsequent runs of the Service skip the link step
(`DeviceState.IsLinked` is true).

---

### Policy sync after linking

`DevicePolicySyncService` is a `BackgroundService` that:

- **Waits** until `DeviceState.IsLinked` is true before attempting a SignalR connection.
- Connects to `MonitoringHub` on the WebServer using the **device JWT** as a bearer
  token, joining the parent account's SignalR group.
- Listens for `PolicyUpdated` hub events — when the parent saves new policy in the
  WebClient, the Service receives it immediately and writes a local `policy.json` cache.
- Also polls `GET api/devices/{id}/policy` periodically as a fallback in case the
  SignalR connection is lost.

---

### JWT token types

| Type | Issued by | Used by | Key claims |
|---|---|---|---|
| Account token | `POST api/account/login` | WebClient (browser) | `account_id`, `email` |
| Device token | `POST api/devices/redeem` | Service (background) | `account_id`, `device_id`, `token_type=device` |

Both tokens are HMAC-SHA256 signed JWTs validated by the same `JwtBearer` middleware
in the WebServer.

---

### Security notes

- A link code is **single-use** and **time-limited** (15 minutes). A redeemed or expired
  code cannot be used again.
- The `MachineKey` ties the device token to a specific installation. If `device.json` is
  deleted, the device must be re-linked.
- The device JWT does **not** grant access to parent-only endpoints — the `token_type`
  claim is checked by controllers that need to distinguish account vs. device callers.
- There is currently **no authentication or authorization on the Named Pipe itself**;
  any local process that can open `ScreenBuxServicePipe` can send messages. This is
  acceptable for a same-machine parent-control scenario but should be hardened if the
  threat model expands.
