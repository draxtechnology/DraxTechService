using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DraxTechnology.Panels
{
    internal class PanelEmail : AbstractPanel
    {
        #region constants
        const string kemailkey = "Email";
        #endregion

        #region private variables
        public string from;
        public string server;
        public int port;
        public string username;
        public string password;
        public int smtpauth;
        public bool enablessl;

        private readonly string baseFolder;
        private string systemName;
        private readonly List<EmailGroup> groups = new List<EmailGroup>();
        private readonly Queue<EmailQueueItem> sendQueue = new Queue<EmailQueueItem>();
        private readonly object queueLock = new object();
        private Timer sendTimer;
        #endregion

        public PanelEmail(string baselogfolder, string identifier) : base(baselogfolder, identifier, "EMLMan", "EML")
        {
            baseFolder = baselogfolder;

            if (!String.IsNullOrEmpty(identifier))
            {
                from = base.GetSetting<string>(kemailkey, "From");
                server = base.GetSetting<string>(kemailkey, "SMTPServer");
                port = base.GetSetting<int>(kemailkey, "SMTPPort");
                username = base.GetSetting<string>(kemailkey, "LoginName");
                password = base.GetSetting<string>(kemailkey, "Password");
                smtpauth = base.GetSetting<int>(kemailkey, "SMTPAuthorisation");
                enablessl = true;

                systemName = base.GetSetting<string>(kemailkey, "SystemName");
                if (String.IsNullOrEmpty(systemName)) systemName = "Drax360";

                LoadEmailGroups();
                sendTimer = new Timer(DrainQueue, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            }
        }

        public override string FakeString => throw new NotImplementedException();

        public override void Alert(string passedValues) => EnqueueEvent("FIRE", passedValues);
        public override void Evacuate(string passedValues) => EnqueueEvent("EVACUATE", passedValues);
        public override void EvacuateNetwork(string passedValues) => EnqueueEvent("EVACUATE NETWORK", passedValues);
        public override void Reset(string passedValues) => EnqueueEvent("RESET", passedValues);
        public override void Silence(string passedValues) => EnqueueEvent("SILENCE", passedValues);
        public override void MuteBuzzers(string passedValues) { }
        public override void DisableDevice(string passedValues) => EnqueueEvent("DEVICE DISABLED", passedValues);
        public override void DisableZone(string passedValues) => EnqueueEvent("ZONE DISABLED", passedValues);
        public override void EnableDevice(string passedValues) => EnqueueEvent("DEVICE ENABLED", passedValues);
        public override void EnableZone(string passedValues) => EnqueueEvent("ZONE ENABLED", passedValues);
        public override void Analogue(string passedValues) { }
        public override void StartUp(int fakemode) { }

        public void TestMessage(string to)
        {
            string testmessage = base.GetSetting<string>(kemailkey, "TestMessage");
            MailMessage message = new MailMessage(from, to, "Test", testmessage);
            send_message(message);
        }

        // passedValues format from client: "Node,Loop,Zone,IP"
        private void EnqueueEvent(string eventTypeText, string passedValues)
        {
            if (String.IsNullOrEmpty(eventTypeText)) return;

            string[] parts = passedValues?.Split(',') ?? new string[0];
            string node = GetPart(parts, 0);
            string loop = GetPart(parts, 1);
            string zone = GetPart(parts, 2);
            string ip = GetPart(parts, 3);

            string reference = $"{node}-{loop}-{zone}";
            string nodeName = String.IsNullOrEmpty(ip) ? string.Empty : $"Panel {ip}";
            string location = string.Empty;
            string classification = string.Empty;
            string date = DateTime.Now.ToString("dd/MM/yyyy");
            string time = DateTime.Now.ToString("HH:mm:ss");

            lock (queueLock)
            {
                foreach (var group in groups.Where(g => g.InUse && !g.ReportsOnly))
                {
                    string filterString = eventTypeText;
                    if (group.LocText) filterString += " " + location;
                    if (group.NodeText) filterString += " " + nodeName;

                    if (!MatchesFilter(filterString, group.Filter)) continue;

                    string to = group.Addresses.FirstOrDefault();
                    if (String.IsNullOrEmpty(to)) continue;

                    string extras = String.Join(", ", group.Addresses.Skip(1));
                    sendQueue.Enqueue(new EmailQueueItem
                    {
                        To = to,
                        Cc = group.Bcc ? "" : extras,
                        Bcc = group.Bcc ? extras : "",
                        Subject = $"{systemName} Event: {eventTypeText}",
                        Body = BuildBody(eventTypeText, location, nodeName, reference, time, date, classification),
                    });
                }
            }
        }

        private void DrainQueue(object state)
        {
            EmailQueueItem item;
            lock (queueLock)
            {
                if (sendQueue.Count == 0) return;
                item = sendQueue.Dequeue();
            }
            try
            {
                var msg = new MailMessage();
                msg.From = new MailAddress(from);
                msg.To.Add(item.To);
                if (!String.IsNullOrEmpty(item.Cc)) msg.CC.Add(item.Cc);
                if (!String.IsNullOrEmpty(item.Bcc)) msg.Bcc.Add(item.Bcc);
                msg.Subject = item.Subject;
                msg.Body = item.Body;
                msg.IsBodyHtml = false;
                send_message(msg);
            }
            catch (Exception ex)
            {
                NotifyClient($"Error building email: {ex.Message}");
            }
        }

        private string BuildBody(string eventTypeText, string location, string nodeName, string reference, string time, string date, string classification)
        {
            var sb = new StringBuilder();
            sb.AppendLine(eventTypeText);
            sb.AppendLine();
            sb.AppendLine($"LOCATION  : {location}");
            if (!String.IsNullOrEmpty(nodeName))
                sb.AppendLine($"NODE      : {nodeName}");
            sb.AppendLine($"REFERENCE : {reference}");
            sb.AppendLine($"DATE/TIME : {time} on {date}");
            if (!String.IsNullOrEmpty(classification))
                sb.AppendLine($"CLASSIFICATION : {classification}");
            sb.AppendLine();
            sb.AppendLine($"This is an event email generated automatically by {systemName}. Replies to this email will not be read.");
            sb.AppendLine($"Please contact the {systemName} administrator if you wish to be removed from this email list.");
            return sb.ToString();
        }

        // Filter terms: "FIRE,FAULT" = OR match; "+FIRE" = must contain; "-TEST" = must not contain
        private static bool MatchesFilter(string text, string filter)
        {
            if (String.IsNullOrEmpty(filter)) return true;
            text = text.ToUpper();
            bool anyMatch = false;
            foreach (string raw in filter.ToUpper().Split(','))
            {
                string term = raw.Trim();
                if (term.StartsWith("+"))
                {
                    if (!text.Contains(term.Substring(1))) return false;
                    anyMatch = true;
                }
                else if (term.StartsWith("-"))
                {
                    if (text.Contains(term.Substring(1))) return false;
                    anyMatch = true;
                }
                else
                {
                    if (text.Contains(term)) anyMatch = true;
                }
            }
            return anyMatch;
        }

        private void LoadEmailGroups()
        {
            string jsonPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DraxClient", "emailgroups.json");

            if (!File.Exists(jsonPath))
            {
                base.NotifyClient($"PanelEmail: groups file not found ({jsonPath})", false);
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var raw = serializer.Deserialize<List<ClientEmailGroup>>(json);
                if (raw == null) return;

                int n = 1;
                foreach (var cg in raw)
                {
                    if (!cg.Enabled) continue;

                    var addrs = (cg.Addresses ?? new List<ClientEmailAddress>())
                        .Select(a => a.Email)
                        .Where(e => IsEmailValid(e))
                        .ToList();

                    if (addrs.Count == 0) continue;

                    groups.Add(new EmailGroup
                    {
                        Number = n++,
                        Name = cg.Name ?? string.Empty,
                        InUse = true,
                        Filter = cg.Keywords ?? string.Empty,
                        LocText = cg.LocText,
                        NodeText = cg.NodeText,
                        Html = cg.HTML,
                        Bcc = cg.BCC,
                        ReportsOnly = cg.Reports,
                        Addresses = addrs,
                    });
                }
            }
            catch (Exception ex)
            {
                base.NotifyClient($"PanelEmail: error loading groups — {ex.Message}", false);
            }

            base.NotifyClient($"PanelEmail: {groups.Count} active group(s) loaded", false);
        }

        private void send_message(MailMessage message)
        {
            using (SmtpClient smtp = new SmtpClient(server, port))
            {
                if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(password) || smtpauth == 1)
                    smtp.Credentials = new System.Net.NetworkCredential(username, password);
                smtp.EnableSsl = enablessl;
                try
                {
                    smtp.Send(message);
                    base.NotifyClient("Send Email " + message.Subject + " " + message.To, false);
                }
                catch (Exception ex)
                {
                    NotifyClient($"Error in send_message: {ex.Message}");
                }
            }
        }

        #region Helpers
        private static bool IsEmailValid(string email)
        {
            if (String.IsNullOrEmpty(email)) return false;
            try { return new MailAddress(email).Address == email; }
            catch { return false; }
        }

        private static string GetPart(string[] parts, int index) =>
            index < parts.Length ? parts[index].Trim() : string.Empty;
        #endregion

        // POCOs matching the client's emailgroups.json schema
        private class ClientEmailGroup
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public bool Enabled { get; set; }
            public string Keywords { get; set; }
            public List<ClientEmailAddress> Addresses { get; set; }
            public bool LocText { get; set; }
            public bool NodeText { get; set; }
            public bool HTML { get; set; }
            public bool BCC { get; set; }
            public bool Reports { get; set; }
        }

        private class ClientEmailAddress
        {
            public string Email { get; set; }
        }
    }

    internal class EmailGroup
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public bool InUse { get; set; }
        public string Filter { get; set; }
        public bool LocText { get; set; }
        public bool NodeText { get; set; }
        public bool Html { get; set; }
        public bool Bcc { get; set; }
        public bool ReportsOnly { get; set; }
        public List<string> Addresses { get; set; } = new List<string>();
    }

    internal class EmailQueueItem
    {
        public string To { get; set; }
        public string Cc { get; set; }
        public string Bcc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}