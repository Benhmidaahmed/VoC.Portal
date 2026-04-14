using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace Xrmbox.VoC.Portal.Services
{
    // On dit que cette classe "implémente" l'interface
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var emailMessage = new MimeMessage();

            // "Xrmbox Portal" est le nom qui s'affichera chez le client
            emailMessage.From.Add(new MailboxAddress("Xrmbox Portal", _config["Smtp:User"]));
            emailMessage.To.Add(new MailboxAddress("", email));
            emailMessage.Subject = subject;
            emailMessage.Body = new TextPart("html") { Text = htmlMessage };

            using (var client = new SmtpClient())
            {
                // Utiliser StartTls pour le port 587 (compatible Gmail)
                await client.ConnectAsync(_config["Smtp:Server"], int.Parse(_config["Smtp:Port"]), MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_config["Smtp:User"], _config["Smtp:Pass"]);
                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);
            }
        }
    }
}