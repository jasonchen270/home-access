// =============================================================================
// Program.cs is the entry point. In .NET 6+ this replaces the old Startup.cs.
//
// LEARNING ORDER for this file:
//   1. WebApplication.CreateBuilder = creates a DI (dependency injection) container
//      and config pipeline. Everything you "register" goes into a service collection.
//   2. builder.Services.Add*() = register dependencies. Anything registered here
//      can be injected into controller constructors.
//   3. builder.Build() = freezes the DI container and returns the app.
//   4. app.Use*() = configure the HTTP middleware pipeline (order MATTERS).
//   5. app.Run() = start listening.
// =============================================================================

using HomeAccess.Api.Data;
using HomeAccess.Api.Mqtt;
using HomeAccess.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- 1. EF Core DbContext registration -------------------------------------
// AddDbContext<T>() registers AppDbContext as a "scoped" service (one per HTTP request).
// The lambda configures which database provider + connection string to use.
// Connection string lives in appsettings.json under "ConnectionStrings:Default".
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=homeaccess.db"));

// ---- 2. ASP.NET Identity ---------------------------------------------------
// This wires up: user table, role table, password hashing, sign-in manager, etc.
// AddEntityFrameworkStores<AppDbContext>() tells Identity to persist to OUR DbContext
// (so Users/Roles tables live in the same DB as Devices/EntryEvents).
builder.Services
    .AddIdentity<AppUser, IdentityRole>(opt =>
    {
        opt.Password.RequireNonAlphanumeric = false; // relaxed for learning; tighten in prod
        opt.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookie-based auth: when the React app POSTs to /api/auth/login, we set an
// HTTP-only cookie. Subsequent requests carry it automatically. No JWT plumbing.
builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.Cookie.HttpOnly = true;
    opt.Cookie.SameSite = SameSiteMode.Lax;
    // For an API (not server-rendered HTML) we want 401/403 responses, not redirects.
    opt.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    opt.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

// ---- 3. MQTT background service --------------------------------------------
// AddSingleton<MqttBus>() = ONE instance for the whole app lifetime (the MQTT
// connection is long-lived, not per-request).
// AddHostedService = ASP.NET will call StartAsync() on app boot and StopAsync()
// on shutdown. This is the canonical way to run background workers.
builder.Services.AddSingleton<MqttBus>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttBus>());

// ---- 4. Standard web stuff -------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173")    // Vite default dev port
     .AllowAnyHeader().AllowAnyMethod()
     .AllowCredentials()));                    // needed because we use cookies, not JWT

var app = builder.Build();

// ---- 5. Middleware pipeline (ORDER MATTERS) --------------------------------
// Each app.Use*() adds a middleware. A request passes through them top→bottom,
// then the response passes back through bottom→top. If you put UseAuthorization
// BEFORE UseAuthentication, the user will never be authenticated. Common gotcha.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();          // visit https://localhost:5001/swagger
}

app.UseCors();
app.UseAuthentication();         // "who are you?" reads the cookie
app.UseAuthorization();          // "are you allowed?" checks [Authorize] attributes
app.MapControllers();

// ---- 6. Auto-migrate + seed on startup -------------------------------------
// In production you'd run `dotnet ef database update` separately. For learning
// it's nice to have the DB just work the first time you press F5.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.RunAsync(scope.ServiceProvider);
}

app.Run();
