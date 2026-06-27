using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using CryptoModule;

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
                // Client stores the password encrypted (AesDecryptor.EncryptOpenSSLCtr);
                // decrypt on read so it can be used as the SMTP credential. Empty stays empty.
                string storedPassword = base.GetSetting<string>(kemailkey, "Password");
                password = string.IsNullOrEmpty(storedPassword)
                    ? storedPassword
                    : AesDecryptor.DecryptOpenSSLCtr(storedPassword, "");
                smtpauth = base.GetSetting<int>(kemailkey, "SMTPAuthorisation");
                // SSL is on by default (matches the prior always-on behaviour and
                // most modern relays); the client disables it by writing EnableSSL=0
                // (or false/no). Absent or blank keeps SSL on so existing deployments
                // don't regress — a hardcoded true used to break plain port-25 relays.
                string sslSetting = (base.GetSetting<string>(kemailkey, "EnableSSL") ?? string.Empty).Trim();
                enablessl = !(sslSetting == "0"
                    || sslSetting.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || sslSetting.Equals("no", StringComparison.OrdinalIgnoreCase));

                systemName = base.GetSetting<string>(kemailkey, "SystemName");
                if (String.IsNullOrEmpty(systemName)) systemName = "Drax360";

                LoadEmailGroups();
            }
        }

        public override string FakeString => throw new NotImplementedException();
        public override string PanelVersion => "1.0.0.0";

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
            // new MailMessage(from, ...) throws on a blank sender; mirror the
            // EnqueueEvent guard and skip with a log rather than throw.
            if (String.IsNullOrEmpty(from))
            {
                NotifyClient("Test email not sent: no From address configured (EMAIL,From).", false);
                return;
            }

            string testmessage = base.GetSetting<string>(kemailkey, "TestMessage");
            MailMessage message = new MailMessage(from, to, "Test", testmessage);
            send_message(message);
        }

        // passedValues format from client: "Node,Loop,Zone,IP"
        private void EnqueueEvent(string eventTypeText, string passedValues)
        {
            if (String.IsNullOrEmpty(eventTypeText)) return;

            // new MailAddress(from) below throws on a blank sender; a misconfigured
            // From should log and skip, not take down the send path.
            if (String.IsNullOrEmpty(from))
            {
                NotifyClient("Email not sent: no From address configured (EMAIL,From).", false);
                return;
            }

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

            foreach (var group in groups.Where(g => g.InUse && !g.ReportsOnly))
            {
                string filterString = eventTypeText;
                if (group.LocText) filterString += " " + location;
                if (group.NodeText) filterString += " " + nodeName;

                if (!MatchesFilter(filterString, group.Filter)) continue;

                string to = group.Addresses.FirstOrDefault();
                if (String.IsNullOrEmpty(to)) continue;

                string extras = String.Join(", ", group.Addresses.Skip(1));
                string subject = $"{systemName} Event: {eventTypeText}";
                bool html = group.Html;
                string body = BuildBody(html, eventTypeText, location, nodeName, reference, time, date, classification);
                string cc = group.Bcc ? "" : extras;
                string bcc = group.Bcc ? extras : "";

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var msg = new MailMessage();
                        msg.From = new MailAddress(from);
                        msg.To.Add(to);
                        if (!String.IsNullOrEmpty(cc)) msg.CC.Add(cc);
                        if (!String.IsNullOrEmpty(bcc)) msg.Bcc.Add(bcc);
                        msg.Subject = subject;
                        msg.Body = body;
                        msg.IsBodyHtml = html;
                        send_message(msg);
                    }
                    catch (Exception ex)
                    {
                        NotifyClient($"Error building email: {ex.Message}");
                    }
                });
            }
        }

        private string BuildBody(bool html, string eventTypeText, string location, string nodeName, string reference, string time, string date, string classification)
        {
            if (html) return BuildHtmlBody(eventTypeText, location, nodeName, reference, time, date, classification);

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

        // HTML variant for groups with the Html flag set. Mirrors the plaintext
        // layout in a <pre> block so the aligned labels survive, with every value
        // HTML-encoded so an event string containing < & > can't break the markup.
        private string BuildHtmlBody(string eventTypeText, string location, string nodeName, string reference, string time, string date, string classification)
        {
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

            var sb = new StringBuilder();
            sb.Append("<html><body style=\"font-family:Consolas,'Courier New',monospace\">");
            sb.Append($"<p><strong>{Enc(eventTypeText)}</strong></p><pre>");
            sb.Append($"LOCATION  : {Enc(location)}\n");
            if (!String.IsNullOrEmpty(nodeName))
                sb.Append($"NODE      : {Enc(nodeName)}\n");
            sb.Append($"REFERENCE : {Enc(reference)}\n");
            sb.Append($"DATE/TIME : {Enc(time)} on {Enc(date)}");
            if (!String.IsNullOrEmpty(classification))
                sb.Append($"\nCLASSIFICATION : {Enc(classification)}");
            sb.Append("</pre>");
            sb.Append($"<p>This is an event email generated automatically by {Enc(systemName)}. Replies to this email will not be read.<br>");
            sb.Append($"Please contact the {Enc(systemName)} administrator if you wish to be removed from this email list.</p>");
            sb.Append("</body></html>");
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
            Paths.MigrateLegacyFile("emailgroups.json");
            string jsonPath = Paths.GetFile("emailgroups.json");

            if (!File.Exists(jsonPath))
            {
                base.NotifyClient($"PanelEmail: groups file not found ({jsonPath})", false);
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var raw = JsonSerializer.Deserialize<List<ClientEmailGroup>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

}
