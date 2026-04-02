using System.Threading.Tasks;

namespace Xrmbox.VoC.Portal.Services
{
    public interface IEmailService
    {
        // On définit juste la signature de la méthode
        Task SendEmailAsync(string email, string subject, string htmlMessage);
    }
}