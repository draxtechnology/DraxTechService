using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace DraxTechnology
{
    // Parallel, additive MQTT sink for the open head-end / Node-RED proof of
    // concept. It mirrors the same normalised event stream that feeds AMX
    // (tapped at the single nvms enqueue chokepoint in CSAMX) onto a local
    // broker, and subscribes to a command topic so a head-end can send controls
    // back. It is deliberately decoupled from AMX: nothing here can disturb the
    // live AMX path. When MqttEnabled is false every public method is a no-op.
    //
    // The shape mirrors AMXTransfer on purpose — singleton, a single-writer
    // outbound queue drained on a dedicated thread, a background connect/
    // reconnect loop, and an announce-once log gate so an outage doesn't spam.
    // The hard rule: a publish failure or a downed broker must never throw into
    // a panel parser thread, because those threads are also feeding AMX.
    internal class MqttTransfer
    {
        private static MqttTransfer _instance;
        private static readonly object _lock = new object();

        public static MqttTransfer Instance
        {
            get { lock (_lock) { return _instance ??= new MqttTransfer(); } }
        }

        // Surfaced through the same OutsideEvents/CustomEventArgs(notifyUI:false)
        // channel AMXTransfer and CSAMX use, so the MQTT lifecycle shows up in the
        // log trace without notifying the UI.
        public event EventHandler OutsideEvents;
        private void NotifyClient(string message)
            => OutsideEvents?.Invoke(this, new CustomEventArgs(message, false));

        // Config (read once in the constructor). All optional; sensible defaults.
        private readonly bool _enabled;
        private readonly string _broker = "localhost";
        private readonly int _port = 1883;
        private readonly string _topicPrefix = "drax";

        private string _panel = "panel";       // set in Start(); used in topic names
        private string _eventTopic = "drax/panel/event";
        private string _logTopic = "drax/panel/log";
        private string _cmdTopic = "drax/panel/cmd";

        private IMqttClient _client;
        private MqttClientOptions _options;

        // Single-writer outbound queue: PublishEvent/PublishLog enqueue here and a
        // dedicated thread drains it. Keeps serialization off the caller's thread
        // and means a slow/blocked broker never stalls the panel parser.
        private readonly BlockingCollection<(string Topic, string Payload)> _outbound
            = new BlockingCollection<(string, string)>();
        // Defensive cap. Events are also going to AMX, so dropping the oldest MQTT
        // mirror messages during a broker outage is acceptable — far better than
        // growing the queue without bound. 10k is well above any real burst.
        private const int kMaxQueueDepth = 10000;

        private Thread _senderThread;
        private CancellationTokenSource _cts;
        private volatile bool _stopRequested;
        private volatile bool _started;

        // Announce-once gates so a prolonged broker outage logs a line on the way
        // down and a line on the way back up, not one per retry (mirrors the
        // _reconnectAnnounced latch in AMXTransfer).
        private bool _connectAnnounced;
        private bool _outageAnnounced;

        private const int kReconnectDelayMs = 5000;

        private MqttTransfer()
        {
            try
            {
                string en = ConfigurationManager.AppSettings["MqttEnabled"]?.Trim();
                _enabled = bool.TryParse(en, out bool on) && on;

                string broker = ConfigurationManager.AppSettings["MqttBroker"]?.Trim();
                if (!string.IsNullOrEmpty(broker)) _broker = broker;

                string portStr = ConfigurationManager.AppSettings["MqttPort"]?.Trim();
                if (!string.IsNullOrEmpty(portStr)
                    && int.TryParse(portStr, out int p) && p > 0 && p <= 65535)
                    _port = p;

                string prefix = ConfigurationManager.AppSettings["MqttTopicPrefix"]?.Trim();
                if (!string.IsNullOrEmpty(prefix)) _topicPrefix = prefix.TrimEnd('/');
            }
            catch
            {
                // Config unavailable (e.g. running outside the service host) — stay
                // disabled rather than ever letting config reads stop startup.
                _enabled = false;
            }
        }

        // Bring the publisher up. No-op (with a single log line) when disabled, so
        // a normal install is unaffected. Safe to call once during service startup.
        public void Start(string panel)
        {
            if (!_enabled)
            {
                NotifyClient("MQTT mirror disabled (MqttEnabled is false)");
                return;
            }
            if (_started) return;

            _panel = string.IsNullOrWhiteSpace(panel) ? "panel" : panel.Trim().ToLowerInvariant();
            _eventTopic = $"{_topicPrefix}/{_panel}/event";
            _logTopic = $"{_topicPrefix}/{_panel}/log";
            _cmdTopic = $"{_topicPrefix}/{_panel}/cmd";

            try
            {
                var factory = new MqttFactory();
                _client = factory.CreateMqttClient();
                _options = new MqttClientOptionsBuilder()
                    .WithTcpServer(_broker, _port)
                    .WithClientId($"DraxService-{_panel}-{Environment.ProcessId}")
                    .WithCleanSession()
                    .Build();

                _client.ApplicationMessageReceivedAsync += OnMessageReceived;

                _cts = new CancellationTokenSource();
                _started = true;

                _senderThread = new Thread(SenderLoop)
                {
                    IsBackground = true,
                    Name = "MqttSender"
                };
                _senderThread.Start();

                // Long-lived connect/reconnect loop; fire-and-forget by design,
                // exactly like AMXTransfer.Run.
                _ = Task.Run(() => ConnectLoop(_cts.Token));

                NotifyClient($"MQTT mirror enabled -> {_broker}:{_port}, topics {_topicPrefix}/{_panel}/*");
            }
            catch (Exception ex)
            {
                _started = false;
                NotifyClient("MQTT start failed: " + ex.Message);
            }
        }

        public void Stop()
        {
            _stopRequested = true;
            _started = false;
            try { _outbound.CompleteAdding(); } catch { }
            try { _cts?.Cancel(); } catch { }
            try { _client?.DisconnectAsync().GetAwaiter().GetResult(); } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;
        }

        // Publish one normalised event (an NVM destined for AMX) as JSON. Called
        // from CSAMX on panel parser threads — must be cheap and must never throw.
        public void PublishEvent(NVM nvm, string panelExtension)
        {
            if (!_enabled || !_started || nvm == null) return;
            try
            {
                // Decode the packed event number (see CSAMX.MakeInputNumber):
                //   bit31 = on, bits27..30 = inputType, bits16..26 = node,
                //   bits8..15 = loop, bits0..7 = input.
                uint raw = unchecked((uint)nvm.OurEvent);
                bool onBit = (raw & 0x80000000u) != 0;
                uint body = raw & 0x7FFFFFFFu;

                string typeName = Enum.IsDefined(typeof(NwmData), nvm.OurType)
                    ? ((NwmData)nvm.OurType).ToString()
                    : nvm.OurType.ToString();

                var payload = new
                {
                    panel = _panel,
                    ext = panelExtension,
                    type = typeName,
                    typeId = nvm.OurType,
                    on = nvm.On != 0,
                    value = nvm.Value,
                    eventNumber = nvm.OurEvent,
                    decoded = new
                    {
                        on = onBit,
                        inputType = (int)((body >> 27) & 0x0F),
                        node = (int)((body >> 16) & 0x7FF),
                        loop = (int)((body >> 8) & 0xFF),
                        input = (int)(body & 0xFF)
                    },
                    text = nvm.Text,
                    text2 = nvm.Text2,
                    text3 = nvm.Text3,
                    tsUtc = DateTime.UtcNow.ToString("o")
                };

                Enqueue(_eventTopic, JsonSerializer.Serialize(payload));
            }
            catch (Exception ex)
            {
                NotifyClient("MQTT PublishEvent error: " + ex.Message);
            }
        }

        // Mirror a human-readable event/log line. Supplementary to the structured
        // event topic; handy for a quick Node-RED text feed.
        public void PublishLog(string message)
        {
            if (!_enabled || !_started || string.IsNullOrEmpty(message)) return;
            try { Enqueue(_logTopic, message); }
            catch (Exception ex) { NotifyClient("MQTT PublishLog error: " + ex.Message); }
        }

        private void Enqueue(string topic, string payload)
        {
            if (_outbound.IsAddingCompleted) return;
            if (_outbound.Count >= kMaxQueueDepth)
            {
                // Shed load rather than grow unbounded; AMX still has the event.
                _outbound.TryTake(out _);
            }
            _outbound.Add((topic, payload));
        }

        private void SenderLoop()
        {
            try
            {
                foreach (var (topic, payload) in _outbound.GetConsumingEnumerable(_cts.Token))
                {
                    if (_client == null || !_client.IsConnected)
                        continue; // not connected — drop the mirror; AMX still has it

                    try
                    {
                        var msg = new MqttApplicationMessageBuilder()
                            .WithTopic(topic)
                            .WithPayload(payload)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                            .Build();
                        _client.PublishAsync(msg, _cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // One line, not per-message spam: only when we thought we
                        // were connected. The reconnect loop handles recovery.
                        NotifyClient("MQTT publish failed: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                NotifyClient("MQTT sender thread terminated: " + ex.Message);
            }
        }

        private async Task ConnectLoop(CancellationToken token)
        {
            while (!_stopRequested && !token.IsCancellationRequested)
            {
                try
                {
                    if (_client != null && !_client.IsConnected)
                    {
                        await _client.ConnectAsync(_options, token);

                        // Subscribe to the inbound command topic on (re)connect.
                        var subOptions = new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter(f => f.WithTopic(_cmdTopic)
                                                   .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                            .Build();
                        await _client.SubscribeAsync(subOptions, token);

                        _outageAnnounced = false;
                        if (!_connectAnnounced)
                        {
                            NotifyClient($"MQTT connected to {_broker}:{_port}; listening for controls on {_cmdTopic}");
                            _connectAnnounced = true;
                        }
                        else
                        {
                            NotifyClient("MQTT reconnected");
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!_outageAnnounced)
                    {
                        NotifyClient($"MQTT broker not reachable at {_broker}:{_port}; retrying every {kReconnectDelayMs / 1000}s: {ex.Message}");
                        _outageAnnounced = true;
                        _connectAnnounced = false; // so the next success announces again
                    }
                }

                try { await Task.Delay(kReconnectDelayMs, token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                string topic = e.ApplicationMessage.Topic;
                NotifyClient($"MQTT command on {topic}: {payload}");

                // Hand off to the live DraxService instance, which owns the panels
                // and the control routing (handlepiperesponse). Mirrors the
                // OnManualControlFile / OnAmxPipeCommand hooks AMXTransfer uses.
                DraxService.OnMqttCommand?.Invoke(topic, payload);
            }
            catch (Exception ex)
            {
                NotifyClient("MQTT command dispatch error: " + ex.Message);
            }
            return Task.CompletedTask;
        }
    }
}
