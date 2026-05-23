# Home Access Control Dashboard

A home access control dashboard built with React, ASP.NET Core (C#), EF Core, SQLite, MQTT, and a Raspberry Pi door client, deployable to Azure. It runs locally with zero infrastructure: the API uses a file-based SQLite database and the Pi client falls back to a fake relay, so `dotnet run`, `npm run dev`, and `python door.py` exercise every layer without Docker, SQL Server, or hardware.

## Prerequisites

- .NET 10 SDK
- Node 20
- Python 3.11
- An MQTT broker for the unlock round-trip: `brew install mosquitto` (macOS) or `apt install mosquitto` (Linux), then `mosquitto -p 1883`. Without a broker the API and web still run; only the unlock round-trip is skipped.

## Installation

Start the API:

```bash
cd api/HomeAccess.Api
dotnet run
```

- Listens on `http://localhost:5000`; Swagger at `http://localhost:5000/swagger`.
- On startup it applies the committed EF Core migration (creating a local `homeaccess.db` SQLite file) and seeds `admin@home.local` / `Admin123!`.
- No `dotnet ef` step needed; the `Initial` migration is committed in `Migrations/`.

Start the React app:

```bash
cd web && npm install && npm run dev
```

Visit `http://localhost:5173` and sign in with the seeded admin.

Start the Pi client:

```bash
cd pi-client && pip install -r requirements.txt && python door.py
```

On a laptop the GPIO import fails gracefully and prints `[relay] ON/OFF`, so no hardware is needed.

## Usage

Verify end to end: browser click, API, MQTT, "Pi", MQTT, API, DB, log.

Click **Unlock** in the browser, the `door.py` terminal prints `[relay] ON`/`OFF`, then click **View log** and the granted event appears.
