# Home Access Control Dashboard

A learning-first project: React + ASP.NET Core (C#) + EF Core + SQLite + MQTT + Raspberry Pi, deployable to Azure.

> **Run it locally with zero infrastructure.** The API uses a file-based SQLite
> database and the Pi client falls back to a fake relay, so `dotnet run` +
> `npm run dev` + `python door.py` is enough to exercise every layer, with no Docker,
> no SQL Server, and no hardware. (The `deploy/` docker-compose is optional, only if
> you want to practice the SQL-Server-on-Azure path.)

> Goal of this repo is **learning**, not production. Code is heavily commented and deliberately structured so each file teaches one concept. Anywhere I deviated from "best practice" for the sake of clarity, the comment says so.

---

## Quick layout

```
api/HomeAccess.Api/          ASP.NET Core Web API (priority #1 to learn)
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

docs/
  LEARNING.md                : concept-by-concept syllabus tied to the code
```

---

## First 60 minutes: get the unlock loop working end-to-end

The single most motivating moment is seeing the chain: **browser click → API → MQTT → "Pi" → MQTT → API → DB → log shows up**. Hit that and the rest is detail.

### 1. Prereqs (5 min)
- Install **.NET 10 SDK**, **Node 20**, **Python 3.11**.
- For the MQTT round-trip you need a broker. Easiest: `brew install mosquitto`
  (macOS) or `apt install mosquitto` (Linux), then `mosquitto -p 1883`.
  *(No broker? The API and web still run; only the unlock round-trip is skipped.)*

### 2. Run the API (3 min)
```bash
cd api/HomeAccess.Api
dotnet run
```
- API is at `http://localhost:5000`. Visit `http://localhost:5000/swagger` to poke endpoints.
- On startup it **applies the committed EF Core migration** (creating a local
  `homeaccess.db` SQLite file) and seeds `admin@home.local` / `Admin123!`.
- No `dotnet ef` step needed. The `Initial` migration is committed in
  `Migrations/`. (Want to *learn* migrations? Add a column to an entity and run
  `dotnet ef migrations add <Name>`. See [docs/LEARNING.md](docs/LEARNING.md) §3.)

### 3. Run the React app (3 min)
```bash
cd web && npm install && npm run dev
```
Visit `http://localhost:5173`, sign in with the seeded admin.

### 4. Run the fake Pi (2 min)
```bash
cd pi-client && pip install -r requirements.txt && python door.py
```
On a laptop the GPIO import fails gracefully and prints `[relay] ON/OFF`, so no hardware is needed yet.

### 5. The payoff
Click **Unlock** in the browser → terminal running `door.py` prints `[relay] ON`/`OFF` → click **View log** → the granted event is there.

You just exercised every layer: `browser → API → MQTT → "Pi" → MQTT → API → DB → log`.

---

## Learning roadmap (read [docs/LEARNING.md](docs/LEARNING.md))

The doc walks you through, in order:

1. **C# / ASP.NET Core fundamentals**: DI, middleware, attribute routing, controllers. Anchor file: `Program.cs`.
2. **EF Core**: DbContext, change tracking, migrations, LINQ-to-SQL. Anchor files: `Data/AppDbContext.cs`, `Models/Entities.cs`.
3. **ASP.NET Identity & cookie auth**: users, roles, password hashing, `[Authorize]`. Anchor: `Controllers/AuthController.cs`.
4. **MQTT**: topics, QoS, retained messages, last-will. Anchor: `Mqtt/MqttBus.cs`.
5. **Raspberry Pi integration**: GPIO + paho-mqtt + systemd. Anchor: `pi-client/door.py`.
6. **Azure deployment**: App Service, Azure SQL, managed MQTT, GitHub Actions. Anchor: `deploy/azure-deploy.md`.
7. **Home access security model**: defense in depth, role + row-level + schedule checks. Anchor: `Controllers/DevicesController.cs`.

---

## The security model (priority #2: home-access logic)

Three independent gates a request must pass to physically unlock a door:

| Layer | Question | Where it lives |
|---|---|---|
| **Authentication** | Are you signed in? | Cookie middleware (`Program.cs`) |
| **Role authorization** | Are you a Member or Admin? | `[Authorize]` attribute |
| **Row-level access** | Is THIS device shared with you? | `UserDeviceAccess` join in `DevicesController` |
| **Schedule** | Is it in your allowed time window? | `IsWithinSchedule()` in `DevicesController` |

Defense in depth: bypassing one layer doesn't let you unlock the door, because the next layer also blocks. Always log the *attempt* before checking authorization (audit trail for forensics).

---

## Common gotchas you'll hit

- **CORS + cookies**: `AllowCredentials()` is mandatory on the server, `credentials: "include"` on the client. Drop either and login appears to succeed but the cookie isn't sent on the next request.
- **DbContext is not thread-safe**: that's why `MqttBus` (a singleton) creates a scope per message instead of holding a `DbContext` directly.
- **Middleware order matters**: `UseAuthentication()` must come before `UseAuthorization()`. Reversing them silently breaks login.
- **MQTT retained messages**: convenient for "last known state" but they persist on the broker until cleared, which can be surprising during dev.
- **Auto-migrate on startup is fine for learning, not production**. Production should run migrations as a separate deploy step.
- **SQLite can't `ORDER BY` a `DateTimeOffset`**: SQLite has no native date type, so EF Core throws *"SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY"* the moment you sort the entry log by time. The fix lives in `AppDbContext.OnModelCreating`: a value converter mapping `DateTimeOffset` to a sortable `long` (UTC ms). Worth understanding: it's a textbook example of a provider-specific quirk solved by an EF value converter, and it's why the events endpoint works on SQLite at all.
- **paho-mqtt 1.x vs 2.x**: `door.py` uses `CallbackAPIVersion.VERSION2`, which only exists in paho-mqtt **2.x**. `requirements.txt` pins `>=2.0`; installing 1.6.x crashes the client at startup.
