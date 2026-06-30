using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

// MqttMonitor — a tiny console subscriber that prints the events the Drax
// service mirrors onto MQTT. It's a demo/diagnostic tool: run it next to the
// service (with MqttEnabled=true and a local broker) to watch the normalised
// fire-panel events stream out, without needing Node-RED or any UI yet.
//
// Usage:  MqttMonitor [host] [port] [topicPrefix]
//   defaults: localhost 1883 drax   (subscribes to "<prefix>/#")

string host = args.Length > 0 ? args[0] : "localhost";
int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 1883;
string prefix = (args.Length > 2 ? args[2] : "drax").TrimEnd('/');
string topicFilter = prefix + "/#";

Console.OutputEncoding = Encoding.UTF8;
Banner();

int count = 0;
var factory = new MqttFactory();
IMqttClient client = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer(host, port)
    .WithClientId("MqttMonitor-" + Environment.ProcessId)
    .WithCleanSession()
    .Build();

client.ApplicationMessageReceivedAsync += e =>
{
    string topic = e.ApplicationMessage.Topic;
    string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
    count++;
    if (topic.EndsWith("/event", StringComparison.OrdinalIgnoreCase))
        PrintEvent(topic, payload);
    else if (topic.EndsWith("/log", StringComparison.OrdinalIgnoreCase))
        PrintLine(ConsoleColor.DarkGray, $"  {Time(null)}  log   {payload}");
    else
        PrintLine(ConsoleColor.Gray, $"  {Time(null)}  {topic}  {payload}");
    return Task.CompletedTask;
};

// Reconnect on drop — the broker or the service may come and go during a demo.
client.DisconnectedAsync += async _ =>
{
    PrintLine(ConsoleColor.DarkYellow, $"  -- disconnected; reconnecting to {host}:{port} --");
    await Task.Delay(2000);
    try { await Connect(); } catch { /* next DisconnectedAsync retries */ }
};

// Ctrl+C exits cleanly.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; cts.Cancel(); };

await ConnectWithRetry(cts.Token);

try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (OperationCanceledException) { }

try { await client.DisconnectAsync(); } catch { }
Console.ResetColor();
Console.WriteLine($"\nStopped. {count} message(s) seen.");

// ---------------- helpers ----------------

async Task Connect()
{
    await client.ConnectAsync(options, CancellationToken.None);
    var sub = new MqttClientSubscribeOptionsBuilder()
        .WithTopicFilter(f => f.WithTopic(topicFilter)
                               .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
        .Build();
    await client.SubscribeAsync(sub, CancellationToken.None);
    PrintLine(ConsoleColor.Green, $"  -- connected to {host}:{port}, subscribed to {topicFilter} --\n");
}

async Task ConnectWithRetry(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try { await Connect(); return; }
        catch (Exception ex)
        {
            PrintLine(ConsoleColor.DarkYellow,
                $"  -- broker not reachable at {host}:{port} ({ex.Message}); retrying in 3s --");
            try { await Task.Delay(3000, token); } catch { return; }
        }
    }
}

void PrintEvent(string topic, string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string panel = Str(r, "panel");
        string type = Str(r, "type");
        bool on = r.TryGetProperty("on", out var onEl) && onEl.ValueKind == JsonValueKind.True;
        string text = Str(r, "text");
        string text2 = Str(r, "text2");
        string text3 = Str(r, "text3");
        string ts = Str(r, "tsUtc");

        int node = 0, loop = 0, input = 0;
        if (r.TryGetProperty("decoded", out var dec) && dec.ValueKind == JsonValueKind.Object)
        {
            node = Int(dec, "node"); loop = Int(dec, "loop"); input = Int(dec, "input");
        }

        string state = on ? "ON " : "off";
        string addr = $"N{node,-3} L{loop,-3} D{input,-3}";
        string extra = string.Join(" / ",
            new[] { text, text2, text3 }.Where(s => !string.IsNullOrWhiteSpace(s)));

        string line = $"  {Time(ts)}  {panel,-7} {state}  {type,-22} {addr}  {extra}";
        PrintLine(ColorFor(type, on), line);
    }
    catch
    {
        // Not the JSON we expected — show it raw rather than dropping it.
        PrintLine(ConsoleColor.Gray, $"  {Time(null)}  {topic}  {json}");
    }
}

static ConsoleColor ColorFor(string type, bool on)
{
    string t = type.ToLowerInvariant();
    if (t.Contains("alarm")) return on ? ConsoleColor.Red : ConsoleColor.Green;
    if (t.Contains("fault") || t.Contains("error")) return ConsoleColor.Yellow;
    if (t.Contains("isolation") || t.Contains("disable") || t.Contains("isolate")) return ConsoleColor.Cyan;
    if (t.Contains("reset") || t.Contains("silence")) return ConsoleColor.Green;
    return ConsoleColor.White;
}

static string Time(string? iso)
{
    if (!string.IsNullOrEmpty(iso) && DateTimeOffset.TryParse(iso, out var dto))
        return dto.ToLocalTime().ToString("HH:mm:ss");
    return DateTime.Now.ToString("HH:mm:ss");
}

static string Str(JsonElement e, string name)
    => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

static int Int(JsonElement e, string name)
    => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

void PrintLine(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

void Banner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=================================================================");
    Console.WriteLine("  Drax MQTT Monitor  -  live view of the service's event stream");
    Console.WriteLine($"  broker {host}:{port}   topics {topicFilter}");
    Console.WriteLine("  (Ctrl+C to quit)");
    Console.WriteLine("=================================================================");
    Console.ResetColor();
}
