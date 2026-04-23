using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Services;

namespace Xrmbox.VoC.Portal.Services
{
    public class ReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReminderWorker> _logger;
        private readonly IConfiguration _configuration;

        public ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger, IConfiguration configuration)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("--- [DÉMARRAGE] ReminderWorker est lancé ---");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var dataverseService = scope.ServiceProvider.GetService<DataverseService>();
                    var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://localhost:7265";
                    var threshold = DateTime.Now;


                    var invites = await db.SurveyInvitations
                        .Where(i => !i.IsUsed && i.ReminderCount < 3)
                        .ToListAsync(stoppingToken);

                    foreach (var inv in invites)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        bool hasStarted = inv.LastPartialSave.HasValue;
                        bool hasStaleDraft = hasStarted && inv.LastPartialSave.Value <= threshold;
                        bool reminderDelayPassed = inv.LastReminderSent.HasValue && inv.LastReminderSent.Value <= threshold;

                        // Condition : Brouillon abandonné OU délai de rappel classique passé
                        var shouldSendReminder = hasStaleDraft || (inv.ReminderCount > 0 && reminderDelayPassed);

                        if (!shouldSendReminder) continue;

                        var campaign = await db.Campaigns
                            .Include(c => c.Emails)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(c => c.DataverseId == inv.CampaignDataverseId);

                        var reminderTemplate = campaign?.Emails.FirstOrDefault(e => e.Role == 1);

                        if (reminderTemplate == null)
                        {
                            _logger.LogWarning("[SKIP] Aucun template de rappel (Role=1) pour la campagne {Id}", inv.CampaignDataverseId);
                            continue;
                        }

                        string? email = null;
                        string clientName = "Client";
                        string campaignName = campaign?.Name ?? "notre enquête";

                        var ctx = dataverseService?.GetSurveyContextInfo(inv.ParticipantDataverseId);
                        if (ctx != null)
                        {
                            var participants = dataverseService.GetParticipantsByCampaign((Guid)ctx.CampagneId);
                            var participant = participants?.FirstOrDefault(p => p.Id == inv.ParticipantDataverseId);

                            email = participant?.Email;
                            // Utilise ClientName du DTO s'il existe, sinon garde "Client"
                            if (!string.IsNullOrEmpty(participant?.ClientName))
                                clientName = participant.ClientName;
                        }

                        if (string.IsNullOrWhiteSpace(email)) continue;

                        var link = $"{baseUrl}/Survey/Fill?token={inv.Token}";

                        // 4. Préparation du contenu avec tous les remplacements
                        var subject = reminderTemplate.Subject
                            .Replace("[SurveyLink]", link)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[ClientName]", clientName);

                        var body = reminderTemplate.Body
                            .Replace("[SurveyLink]", link)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[ClientName]", clientName)
                            .Replace("[br]", "<br/>")
                            .Replace("\n", "<br/>");

                        try
                        {
                            _logger.LogInformation("[RAPPEL] Envoi du mail Role=1 à {Email} (Client: {Name})", email, clientName);
                            await emailService.SendEmailAsync(email, subject, body);

                            inv.ReminderCount += 1;
                            inv.LastReminderSent = DateTime.Now;
                            inv.SyncStatus = "Reminded";

                            db.SurveyInvitations.Update(inv);
                            await db.SaveChangesAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[ERREUR ENVOI] Invitation ID {Id}", inv.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur critique dans ReminderWorker.");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
    }
}