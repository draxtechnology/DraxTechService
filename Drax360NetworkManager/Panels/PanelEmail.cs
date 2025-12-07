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
        private string from;
        string server;
        int port;
        string username;
        string password;
        bool smtpauth;
        bool enablessl;
        #endregion

        public PanelEmail(string baselogfolder, string identifier) : base(baselogfolder, identifier, "EmailMan", "EML")
        {
            if (!String.IsNullOrEmpty(identifier))
            {
                from = base.GetSetting<string>(kemailkey, "From");
                string server = base.GetSetting<string>(kemailkey, "SMTPServer");
                int port = base.GetSetting<int>(kemailkey, "SMTPPort"); ;
                string username = base.GetSetting<string>(kemailkey, "LoginName");
                string password = base.GetSetting<string>(kemailkey, "Password");
                if (password.EndsWith("="))
                {
                    
                    // temporarily disabled.
                   // password = AesDecryptor.DecryptString(password,"");
                    
                }
                bool smtpauth = base.GetSetting<bool>(kemailkey, "SMTPAuthorisation");

                bool enablessl = true;
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
                if (!String.IsNullOrEmpty(username) || !String.IsNullOrEmpty(password) || !smtpauth)
                {
                    smtp.Credentials = new System.Net.NetworkCredential(username, password);
                }
                smtp.EnableSsl = enablessl;

                try
                {
                    smtp.Send(message);
                    base.NotifyClient("Send Email "+message.Subject+" "+message.To, false);
                }
                catch (Exception ex)
                {  
                    NotifyClient($"Error in send_message: {ex.Message}");
                
            }
        }
    }
}