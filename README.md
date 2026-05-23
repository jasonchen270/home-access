# Home Access Control Dashboard

React + ASP.NET Core (C#) + EF Core + SQLite + MQTT + Raspberry Pi, deployable to Azure.

Runs locally with zero infrastructure: the API uses a file-based SQLite database
and the Pi client falls back to a fake relay, so `dotnet run` + `npm run dev` +
`python door.py` exercises every layer without Docker, SQL Server, or hardware.
The `deploy/` docker-compose is optional, for the SQL-Server-on-Azure path.

---

## Layout

```
api/HomeAccess.Api/          ASP.NET Core Web API
  Program.cs                 : DI, middleware pipeline, MQTT host registration
  Models/Entities.cs         : domain model (User, Device, EntryEvent, UserDeviceAccess)
  Data/AppDbContext.cs       : EF Core DbContext + Identity
  Data/SeedData.cs           : admin user + sample devices on first run
  Mqtt/MqttBus.cs            : MQTT pub/sub bridge between API and Pi
  Controllers/AuthController.cs     : login / logout / me (cookie auth)
  Controllers/DevicesController.cs  : list / unlock / events / grant access

web/                         Vite + React + TypeScript SPA
  src/App.tsx                : login screen + dashboard + entry log

pi-client/                   Python MQTT client for the Pi (with laptop fallback)
  door.py                    : subscribes to cmd topic, drives the relay

deploy/
  docker-compose.yml         : local SQL Server + Mosquitto for dev
  mosquitto.conf
  azure-deploy.md            : step-by-step Azure deployment
```

---

## Running locally

The flow to verify end to end: **browser click → API → MQTT → "Pi" → MQTT → API → DB → log**.

### 1. Prereqs
- **.NET 10 SDK**, **Node 20**, **Python 3.11**.
- An MQTT broker for the unlock round-trip: `brew install mosquitto` (macOS) or
  `apt install mosquitto` (Linux), then `mosquitto -p 1883`.
  *(Without a broker the API and web still run; only the unlock round-trip is skipped.)*

### 2. API
```bash
cd api/HomeAccess.Api
dotnet run
```
- Listens on `http://localhost:5000`; Swagger at `http://localhost:5000/swagger`.
- On startup it applies the committed EF Core migration (creating a local
  `homeaccess.db` SQLite file) and seeds `admin@home.local` / `Admin123!`.
- No `dotnet ef` step needed; the `Initial` migration is committed in `Migrations/`.

### 3. React app
```bash
cd web && npm install && npm run dev
```
Visit `http://localhost:5173` and sign in with the seeded admin.

### 4. Pi client
```bash
cd pi-client && pip install -r requirements.txt && python door.py
```
On a laptop the GPIO import fails gracefully and prints `[relay] ON/OFF`, so no hardware is needed.

### 5. End to end
Click **Unlock** in the browser → the `door.py` terminal prints `[relay] ON`/`OFF`
→ click **View log** → the granted event appears.

---

## Security model

Three independent gates a request must pass to physically unlock a door:

| Layer | Question | Where it lives |
|---|---|---|
| **Authentication** | Are you signed in? | Cookie middleware (`Program.cs`) |
| **Role authorization** | Are you a Member or Admin? | `[Authorize]` attribute |
| **Row-level access** | Is THIS device shared with you? | `UserDeviceAccess` join in `DevicesController` |
| **Schedule** | Is it in your allowed time window? | `IsWithinSchedule()` in `DevicesController` |

Defense in depth: bypassing one layer doesn't unlock the door, because the next
layer also blocks. The attempt is always logged before authorization is checked,
for the audit trail.
