# Learning roadmap

Suggested order. Each step has: **concepts** to understand, **files** in this repo to read, and a **stretch task** that forces you to actually use the knowledge instead of just reading.

---

## 1. C# language essentials (skip if you already know C#)

Spend 1-2 hours here, not more. You can learn the rest by doing.

**Concepts to nail down:**
- `class` vs `record` vs `struct`. (Records = immutable value-equality types. Used heavily for DTOs.)
- Properties (`public string Name { get; set; }`). They're not fields; they're sugar for getter/setter methods.
- `async` / `await` and `Task<T>`. Identical mental model to JavaScript promises.
- Nullable reference types (`string?` vs `string`). C# 8+ enforces null-handling at compile time when `<Nullable>enable</Nullable>` is set.
- LINQ (`.Where(...).Select(...).ToList()`). It's the same idea as JS array methods.

**Resource:** Microsoft's [C# tour](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/) is short and good.

---

## 2. ASP.NET Core fundamentals

**Concepts:**
- **Dependency injection container**: `builder.Services.Add*()` registers a class, then any controller can ask for it via constructor parameter. Lifetimes: `Singleton` (one for the app), `Scoped` (one per request), `Transient` (new every time).
- **Middleware pipeline**: each `app.Use*()` wraps the next. Order matters. Mental model: nested function calls.
- **Attribute routing**: `[Route("api/[controller]")]` + `[HttpGet("{id:int}")]` define URLs declaratively.
- **Configuration**: `appsettings.json` → `appsettings.Development.json` overrides → environment variables override that → command-line args override that. Access via `IConfiguration`.

**Read in this order:**
1. `api/HomeAccess.Api/Program.cs`: the comments walk through every line.
2. `api/HomeAccess.Api/Controllers/AuthController.cs`: smallest controller, full DI pattern.

**Stretch task:** Add a `GET /api/health` endpoint that returns `{"status":"ok","time": <utc now>}`. You'll touch routing, DI (inject `TimeProvider`), and JSON serialization.

---

## 3. EF Core

This is the part most likely to feel magical/confusing. Focus on these mental models:

**Concepts:**
- **DbContext is a session.** It tracks entities you've loaded. When you call `SaveChangesAsync()` it diffs the tracked state against the in-memory state and emits the minimal SQL.
- **Conventions over configuration.** `int Id` → primary key. `Device Device` + `int DeviceId` → foreign key. You only need the fluent API when you're overriding conventions.
- **Migrations** are the source of truth for the schema. `dotnet ef migrations add <Name>` generates a C# file describing the diff; `dotnet ef database update` applies it. Commit the migration files to git.
- **Loading patterns.** Lazy loading (off by default in EF Core, which is good), eager loading (`.Include(d => d.Events)`), explicit loading. Knowing which avoids N+1 queries.
- **`AsNoTracking()`**: for read-only queries, skips change tracking. Big perf win for list endpoints.

**Read:**
1. `api/HomeAccess.Api/Models/Entities.cs`: see how navigation properties become joins.
2. `api/HomeAccess.Api/Data/AppDbContext.cs`: `OnModelCreating` for things attributes can't do.
3. `Controllers/DevicesController.cs` `Events()`: a real LINQ query with filtering, ordering, paging, projection.

**Stretch tasks:**
- Add a `Note` property to `Device`. Generate a migration (`dotnet ef migrations add AddDeviceNote`). Look at the generated `.cs` file before applying it.
- Write a query that returns each user with the count of unlock events in the last 24 hours.

---

## 4. ASP.NET Identity & auth

**Concepts:**
- `IdentityUser` / `IdentityRole`: built-in entity types you can extend (we extended with `DisplayName`).
- `UserManager<T>` and `SignInManager<T>`: the two services you'll use 90% of the time.
- Cookie auth vs JWT. We chose cookies because (a) browser sends them automatically, (b) they're HTTP-only so XSS can't steal them. JWT is better when you have non-browser clients.
- `[Authorize]` vs `[Authorize(Roles="Admin")]` vs `[Authorize(Policy="...")]`. Policies are the powerful one; they let you write arbitrary handlers.

**Read:** `Controllers/AuthController.cs` and the `[Authorize]` decorations across `DevicesController.cs`.

**Stretch task:** Add a custom authorization policy `"CanUnlock"` that resolves to "is admin OR has UserDeviceAccess for this device". Replace the manual checks in the unlock endpoint with `[Authorize(Policy="CanUnlock")]`. This forces you to learn `IAuthorizationHandler`, which is the *real* ASP.NET auth pattern.

---

## 5. MQTT

**Concepts:**
- **Pub/sub topics**: strings like `home/door/front/cmd`. Slash-separated, hierarchical. Wildcards: `+` (one level), `#` (multi-level, must be last).
- **QoS 0/1/2**: at-most-once, at-least-once, exactly-once. Default 0 is fire-and-forget; use 1 for commands you actually care about.
- **Retained messages**: broker stores the last message on a topic and ships it to new subscribers. Perfect for "current state" topics.
- **Last-will**: a message the broker publishes on your behalf if your client disconnects ungracefully. We use it to publish `{"type":"offline"}` retained.
- **Clean session vs persistent session**: whether the broker remembers your subscriptions across reconnects.

**Read:**
1. `api/HomeAccess.Api/Mqtt/MqttBus.cs`: server side.
2. `pi-client/door.py`: device side. Note the `will_set()` and `retain=True` calls.

**Stretch tasks:**
- Use `mosquitto_sub -h localhost -t "home/#" -v` (install `mosquitto-clients`) to watch every message flowing through the broker. Click "Unlock" in the UI and see the round trip.
- Add a `home/door/+/heartbeat` topic the Pi publishes on every 30s. Have the API mark devices offline after 90s of silence using a timer.

---

## 6. Raspberry Pi integration

**Concepts:**
- **GPIO basics**: pins are 3.3V logic. NEVER drive a 12V door strike directly; use an opto-isolated relay.
- **gpiozero**: high-level Python wrapper. `OutputDevice(17)` is enough.
- **systemd service**: make `door.py` start on boot and restart on crash. Way more reliable than `python door.py &`.
- **Networking**: Pi connects outbound to the broker, so it works from any home network without port-forwarding.
- **Provisioning**: give each Pi a unique `client_id` and credentials. Don't bake them into the image; use a config file so you can clone the SD card.

**Read:** `pi-client/door.py`.

**Stretch tasks:**
- Wire up an actual relay on a Pi (or simulate with an LED, using the same code).
- Write a `door.service` systemd unit and put it in `pi-client/`. `systemctl enable --now door`. Reboot the Pi and confirm it comes back.
- Add a physical button (`gpiozero.Button`) that publishes `{"type":"physical"}` when pressed.

---

## 7. Azure deployment

**Concepts:**
- **App Service**: managed Linux box that runs your `dotnet publish` output. Set env vars in portal, scale by sliding a dial.
- **Azure SQL**: managed SQL Server. Connection string is the only difference from local SQL Server.
- **Static Web Apps**: for the React build. Has a built-in API proxy + GitHub Actions integration.
- **Managed Identity + Key Vault**: the "right" way to handle secrets. Don't put passwords in app settings forever.
- **App Insights**: distributed tracing. One trace ID flows from React fetch through API through SQL through MQTT, lights up as a waterfall.
- **Bicep**: ARM template successor. Declarative infra. Worth learning AFTER you've done it imperatively with `az` CLI once.

**Read:** `deploy/azure-deploy.md` for step-by-step CLI commands.

**Stretch tasks:**
- Deploy the API to Azure App Service. Get the seeded admin login working from the deployed URL.
- Set up a GitHub Actions workflow so pushing to `main` redeploys.
- Move the SQL password to Key Vault, configure App Service Managed Identity to read it.

---

## 8. Home access control security model (priority #2)

Now that the wiring works, deepen the actual security thinking.

**Concepts:**
- **Defense in depth**: multiple independent gates. Bypassing one shouldn't unlock anything.
- **Audit before authorize**: log the *attempt* before deciding. Forensic value > preventing log spam.
- **Principle of least privilege**: members default to no access; admins explicitly grant.
- **Time-based authorization**: schedules. Common pattern: cleaner gets weekday daytime access, kid gets after-school window, guest gets one-hour window.
- **Compromise modes**: what happens if (a) the broker is compromised? (b) the API is compromised? (c) a device's credentials leak? (d) a member's session cookie is stolen? Walk through each and trace what damage they enable.
- **Reliability ≠ security but they interact.** If the API is down, should the Pi still allow physical-button entry? (Yes. Otherwise a power outage locks people inside.) This is a real design decision.

**Read:**
- `Controllers/DevicesController.cs`: the `Unlock` endpoint and `IsWithinSchedule`.
- `Models/Entities.cs`: how `EntryEvent.UserId` is nullable so physical entries are still logged.

**Stretch tasks:**
- Add a "panic revoke" admin endpoint that, when called, deletes all UserDeviceAccess rows for a user AND publishes `{"action":"forget","userId":"..."}` to every device so even cached local allowlists drop them. Think about why both halves are needed.
- Add rate limiting on `/api/devices/{id}/unlock` (use `Microsoft.AspNetCore.RateLimiting`). What's a sensible per-user limit? Per-IP limit? Why both?
- Add a "duress code", a second password that silently triggers an alert. Where in the system does it live? (Hint: NOT on the Pi.)

---

## How to actually use this doc

Don't read it top-to-bottom. Open it in one tab, the relevant source file in another, and switch back and forth. The "stretch tasks" are the part that builds skill; reading without doing fades fast.

When you get stuck (you will), the order to ask is:

1. Re-read the comments in the relevant source file; most gotchas are flagged inline.
2. Check the "Common gotchas" section in the main README.
3. Search the exact error message. .NET errors are very Google-able; MQTT errors less so.
4. Ask in a relevant community (Stack Overflow, the .NET Discord) with the file open and the specific error.
