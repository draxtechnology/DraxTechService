using CryptoModule;
using System;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Drax360Service.Panels
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
        #endregion

        public PanelEmail(string baselogfolder, string identifier) : base(baselogfolder, identifier, "EMLMan", "EML")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                from = base.GetSetting<string>(kemailkey, "From");
                server = base.GetSetting<string>(kemailkey, "SMTPServer");
                port = base.GetSetting<int>(kemailkey, "SMTPPort"); ;
                username = base.GetSetting<string>(kemailkey, "LoginName");
                password = base.GetSetting<string>(kemailkey, "Password");
                //if (password.EndsWith("="))
                //{
                //    password = AesDecryptor.DecryptString(password, "");
                //}
                smtpauth = base.GetSetting<int>(kemailkey, "SMTPAuthorisation");

                enablessl = true;

                TestMessage("mike.holmes@draxtechnology.com");
            }
        }

        public override string FakeString => throw new NotImplementedException();

        public override void Alert(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void DisableDevice(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void DisableZone(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EnableDevice(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EnableZone(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Evacuate(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void EvacuateNetwork(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void MuteBuzzers(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Reset(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void Silence(string passedValues)
        {
            throw new NotImplementedException();
        }

        public override void StartUp(int fakemode)
        {

        }
        public void TestMessage(string to)
        {
            string testmessage = base.GetSetting<string>(kemailkey, "TestMessage");
            MailMessage message = new MailMessage(from, to, "Test", testmessage);
            send_message(message);
        }


        private void send_message(MailMessage message)
        {

            // assuming SMTP for now, as most modern

            using (SmtpClient smtp = new SmtpClient(server, port))
            {
                if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(password) || smtpauth == 1)
                {
                    smtp.Credentials = new System.Net.NetworkCredential(username, password);
                }
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
    }
}