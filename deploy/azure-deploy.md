# Deploying to Azure

This walks through the cheapest viable Azure setup for this app. Goal: learn
Azure App Service + Azure SQL + a managed MQTT story, end-to-end.

## Architecture on Azure

```
   Browser ──► Azure Static Web Apps  (React)
                     │  (proxied API route)
                     ▼
              Azure App Service        (ASP.NET Core)
                     │
        ┌────────────┼────────────────┐
        ▼            ▼                ▼
   Azure SQL    HiveMQ Cloud /     App Insights
   Database     Azure Event Grid    (logs/metrics)
                MQTT broker
                     ▲
                     │ TLS + username/password
                Raspberry Pi
```

> Why not host Mosquitto in a container on Azure? You can (Azure Container
> Apps), but a managed MQTT (HiveMQ Cloud free tier, or Azure Event Grid's
> MQTT broker) saves you operating it. For learning, do Container Apps once
> then graduate to managed.

## Step-by-step

### 1. Provision (use Azure CLI; learn the imperative form first, ARM/Bicep later)

```bash
az login
RG=home-access-rg; LOC=eastus
az group create -n $RG -l $LOC

# SQL
az sql server create -g $RG -n homeaccess-sql-$RANDOM \
    -u sqladmin -p 'StrongPwd!2345' -l $LOC
az sql db create -g $RG -s <server-name> -n HomeAccess \
    --service-objective Basic   # ~$5/mo

# App Service (Linux, .NET 10)
az appservice plan create -g $RG -n hap-plan --is-linux --sku B1
az webapp create -g $RG -p hap-plan -n homeaccess-api --runtime "DOTNETCORE:10.0"

# Connection string + MQTT settings as App Service config
az webapp config connection-string set -g $RG -n homeaccess-api \
    --connection-string-type SQLAzure \
    --settings Default="Server=tcp:<server>.database.windows.net,1433;..."

az webapp config appsettings set -g $RG -n homeaccess-api \
    --settings Mqtt__Host=<broker-host> Mqtt__Port=8883
```

### 2. CI/CD with GitHub Actions

Add `.github/workflows/deploy.yml`:

```yaml
on: { push: { branches: [main] } }
jobs:
  api:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.x }
      - run: dotnet publish api/HomeAccess.Api -c Release -o out
      - uses: azure/webapps-deploy@v3
        with:
          app-name: homeaccess-api
          publish-profile: ${{ secrets.AZURE_PUBLISH_PROFILE }}
          package: out
```

Get the publish profile from the Azure portal → App Service → "Get publish
profile", paste it into the GitHub repo secret.

### 3. Switch the provider from SQLite to Azure SQL

For zero-setup local dev, `Program.cs` uses SQLite
(`opt.UseSqlite("Data Source=homeaccess.db")`). To deploy on Azure SQL you make
three changes:

1. Add the SQL Server provider back:
   `dotnet add package Microsoft.EntityFrameworkCore.SqlServer`.
2. In `Program.cs`, swap `UseSqlite(...)` for
   `opt.UseSqlServer(builder.Configuration.GetConnectionString("Default"))`
   (the connection string is already in `appsettings.json`; in Azure it comes
   from App Service config, which overrides the file).
3. **Regenerate the migration for the new provider.** Migrations are
   provider-specific, so the committed SQLite migration won't apply to SQL Server.
   Delete `Migrations/`, then `dotnet ef migrations add Initial` against the
   SqlServer-configured context. (Also note: the `DateTimeOffset → long` value
   converter in `AppDbContext` is harmless on SQL Server but unnecessary there,
   since SQL Server supports `DateTimeOffset` natively. You can leave it or drop it.)

`Program.cs` calls `db.Database.Migrate()` on startup, so the first deploy runs
migrations against Azure SQL automatically. For zero-downtime production patterns,
switch to `dotnet ef migrations bundle` and run it as a separate deploy step.

### 4. The Pi → Azure path

- Pi connects to the managed broker over TLS:8883 (NOT 1883 in prod; always TLS).
- Use a per-device username/password OR (better) X.509 client certs.
- Each Pi gets a unique `client_id` so the broker can enforce ACLs per device.

### 5. What to learn next once it's live

- App Insights: trace a request from React → API → SQL → MQTT in one timeline.
- Managed Identity: replace SQL password with the App Service's identity.
- Key Vault: pull the MQTT password from Vault at startup.
- Bicep: re-create the whole RG from a single declarative file (`bicep build`,
  `az deployment group create`).
