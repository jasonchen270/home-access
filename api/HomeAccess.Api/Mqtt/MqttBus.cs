// =============================================================================
// MqttBus.cs is the bridge between the HTTP API and the Pi.
//
// CONCEPT: MQTT is publish/subscribe.
//   - The Pi SUBSCRIBES to "home/door/front/cmd"  (commands FROM us)
//   - The Pi PUBLISHES to "home/door/front/evt"   (events TO us)
//   - The API does the opposite.
//
// Why a broker (Mosquitto) in the middle instead of HTTP-to-Pi directly?
//   1. The Pi is usually behind NAT, so the API can't reach it, but it can reach
//      the broker. The Pi opens an outbound TCP connection and keeps it alive.
//   2. If the Pi goes offline briefly, MQTT can queue with QoS 1 + retained
//      messages, which HTTP doesn't give you for free.
//   3. Multiple devices, multiple subscribers (web app, mobile, alarm system)
//      all decouple naturally.
//
// IHostedService = ASP.NET Core lifecycle hook. Start when app starts, stop on
// shutdown. We use the ManagedClient variant which auto-reconnects on disconnect.
// =============================================================================

using System.Text;
using System.Text.Json;
using HomeAccess.Api.Data;
using HomeAccess.Api.Models;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace HomeAccess.Api.Mqtt;

public class MqttBus : IHostedService
{
    private readonly IManagedMqttClient _client;
    private readonly IServiceScopeFactory _scopeFactory;  // we need this to get a scoped DbContext from a singleton
    private readonly ILogger<MqttBus> _log;
    private readonly IConfiguration _cfg;

    public MqttBus(IServiceScopeFactory scopeFactory, ILogger<MqttBus> log, IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _cfg = cfg;
        _client = new MqttFactory().CreateManagedMqttClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var host = _cfg["Mqtt:Host"] ?? "localhost";
        var port = int.Parse(_cfg["Mqtt:Port"] ?? "1883");

        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId($"home-access-api-{Guid.NewGuid():N}")
                .Build())
            .Build();

        // Subscribe to ALL device events. "+" is a single-level wildcard, "#" multi-level.
        // "home/door/+/evt" matches "home/door/front/evt", "home/door/garage/evt", etc.
        await _client.SubscribeAsync(new[] { new MqttTopicFilterBuilder()
            .WithTopic("home/door/+/evt").Build() });

        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        await _client.StartAsync(options);
        _log.LogInformation("MQTT connected to {Host}:{Port}", host, port);
    }

    public Task StopAsync(CancellationToken ct) => _client.StopAsync();

    // PUBLIC API: called by AccessController to send an unlock command.
    public Task PublishUnlockAsync(string deviceTopic, string userId)
    {
        var payload = JsonSerializer.Serialize(new { action = "unlock", userId, ts = DateTimeOffset.UtcNow });
        return _client.EnqueueAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"{deviceTopic}/cmd")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
    }

    // Handler for messages FROM the Pi. Logs them as EntryEvents.
    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;                                 // home/door/front/evt
            var deviceTopic = topic[..topic.LastIndexOf("/evt", StringComparison.Ordinal)]; // home/door/front
            var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            var msg = JsonSerializer.Deserialize<DeviceEvent>(json);
            if (msg is null) return;

            // CreateScope() so we get a per-message DbContext (DbContext is NOT thread-safe).
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var device = await db.Devices.FirstOrDefaultAsync(d => d.MqttTopic == deviceTopic);
            if (device is null) { _log.LogWarning("Unknown device topic {Topic}", deviceTopic); return; }

            device.IsOnline = msg.type != "offline";
            device.LastSeenAt = DateTimeOffset.UtcNow;

            db.EntryEvents.Add(new EntryEvent
            {
                DeviceId = device.Id,
                Type = msg.type switch
                {
                    "granted"  => EntryEventType.UnlockGranted,
                    "denied"   => EntryEventType.UnlockDenied,
                    "physical" => EntryEventType.PhysicalEntry,
                    "online"   => EntryEventType.DeviceOnline,
                    "offline"  => EntryEventType.DeviceOffline,
                    _          => EntryEventType.UnlockRequested,
                },
                Note = msg.note,
                UserId = msg.userId,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to process MQTT message");
        }
    }

    private record DeviceEvent(string type, string? userId, string? note);
}
