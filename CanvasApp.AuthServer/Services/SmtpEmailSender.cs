using System;
using System.Configuration;
using System.Net;
using System.Net.Mail;

namespace CanvasApp.AuthServer.Services
{
    /// <summary>
    /// Sends mail via the SMTP server configured in App.config (Gmail by default).
    /// Settings keys: SmtpHost, SmtpPort, SmtpUser, SmtpPassword, SmtpFromAddress,
    /// SmtpFromName, SmtpEnableSsl. Falls back to Gmail submission defaults.
    /// Credentials come from the git-ignored App.secrets.config (see App.secrets.config.example).
    /// </summary>
    public class SmtpEmailSender
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _user;
        private readonly string _password;
        private readonly string _fromAddress;
        private readonly string _fromName;
        private readonly bool _enableSsl;

        public SmtpEmailSender()
        {
            var settings = ConfigurationManager.AppSettings;
            _host = settings["SmtpHost"] ?? "smtp.gmail.com";
            _port = int.TryParse(settings["SmtpPort"], out var p) ? p : 587;
            _user = settings["SmtpUser"] ?? "";
            _password = settings["SmtpPassword"] ?? "";
            _fromAddress = settings["SmtpFromAddress"] ?? _user;
            _fromName = settings["SmtpFromName"] ?? "CanvasApp";
            _enableSsl = !bool.TryParse(settings["SmtpEnableSsl"], out var s) || s;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_user) && !string.IsNullOrWhiteSpace(_password);

        public void Send(string toAddress, string subject, string htmlBody)
        {
            using (var msg = new MailMessage())
            using (var client = new SmtpClient(_host, _port))
            {
                msg.From = new MailAddress(_fromAddress, _fromName);
                msg.To.Add(new MailAddress(toAddress));
                msg.Subject = subject;
                msg.Body = htmlBody;
                msg.IsBodyHtml = true;

                client.EnableSsl = _enableSsl;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_user, _password);
                client.Timeout = 15000;
                client.Send(msg);
            }
        }
    }
}
