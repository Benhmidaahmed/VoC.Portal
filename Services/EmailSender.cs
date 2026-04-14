        using System.Net;
        using System.Net.Mail;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Identity.UI.Services;
        using Microsoft.Extensions.Configuration;

        namespace Xrmbox.VoC.Portal.Services
        {
            public class EmailSender : IEmailSender
            {
                private readonly IConfiguration _configuration;

                public EmailSender(IConfiguration configuration)
                {
                    _configuration = configuration;
                }

                public async Task SendEmailAsync(string email, string subject, string htmlMessage)
                {
                    var smtpHost = _configuration["Smtp:Server"];
                    var smtpPort = int.Parse(_configuration["Smtp:Port"] ?? "587");
                    var smtpUser = _configuration["Smtp:User"];
                    var smtpPass = _configuration["Smtp:Pass"];
                    var fromAddress = _configuration["SendGrid:FromEmail"] ?? smtpUser;
                    var fromName = _configuration["SendGrid:FromName"] ?? "Xrmbox Support";

                    using var message = new MailMessage();
                    message.From = new MailAddress(fromAddress, fromName);
                    message.To.Add(new MailAddress(email));
                    message.Subject = subject;
                    message.Body = htmlMessage;
                    message.IsBodyHtml = true;

                    using var client = new SmtpClient(smtpHost, smtpPort)
                    {
                        EnableSsl = true, // IMPORTANT pour Gmail sur 587 avec StartTLS
                        Credentials = new NetworkCredential(smtpUser, smtpPass)
                    };

                    await client.SendMailAsync(message);
                }
            }
        }