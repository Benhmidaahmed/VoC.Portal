using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
                    var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://localhost:7265";
                    var now = DateTime.UtcNow;

                    // Récupérer uniquement les invitations non utilisées, actives, avec moins de 3 rappels
                    var invites = await db.SurveyInvitations
                        .Where(i => !i.IsUsed && i.IsActive && i.ReminderCount < 3)
                        .ToListAsync(stoppingToken);

                    _logger.LogInformation("[ReminderWorker] {Count} invitation(s) candidate(s) au rappel.", invites.Count);

                    foreach (var inv in invites)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        // ✅ 1. Email et nom lus directement depuis l'invitation (base locale)
                        //       Plus aucun appel Dataverse — résout le bug "email introuvable"
                        string? email = inv.ParticipantEmail;
                        string clientName = inv.ParticipantName ?? "Client";

                        if (string.IsNullOrWhiteSpace(email))
                        {
                            _logger.LogWarning("[SKIP] Invitation {Id} : ParticipantEmail vide en base locale.", inv.Id);
                            continue;
                        }

                        // 2. Récupérer la campagne et son template de rappel (Role = 1)
                        var campaign = await db.Campaigns
                            .Include(c => c.Emails)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(c => c.DataverseId == inv.CampaignDataverseId, stoppingToken);

                        var reminderTemplate = campaign?.Emails.FirstOrDefault(e => e.Role == 1);

                        if (reminderTemplate == null)
                        {
                            _logger.LogWarning("[SKIP] Aucun template rappel (Role=1) pour campagne {Id}", inv.CampaignDataverseId);
                            continue;
                        }

                        // ✅ 3. Threshold calculé depuis DelayDays du template (en minutes)
                        //       Si DelayDays est null → défaut 1440 min (24h)
                        int delayMinutes = reminderTemplate.DelayDays ?? 1440;
                        var threshold = now.AddMinutes(-delayMinutes);

                        _logger.LogDebug("[DEBUG] Invitation {Id} | DelayMinutes={D} | Threshold={T} | LastPartialSave={Lps} | LastReminderSent={Lrs} | ReminderCount={Rc}",
                            inv.Id, delayMinutes, threshold, inv.LastPartialSave, inv.LastReminderSent, inv.ReminderCount);

                        // ✅ 4. Condition de déclenchement corrigée
                        //    - 1er rappel  : brouillon abandonné (LastPartialSave existe et délai dépassé)
                        //    - Rappels suivants : délai depuis le dernier rappel dépassé
                        bool hasStarted = inv.LastPartialSave.HasValue;
                        bool hasStaleDraft = hasStarted
                            && inv.LastPartialSave!.Value.ToUniversalTime() <= threshold;

                        bool reminderDelayPassed = inv.LastReminderSent.HasValue
                            && inv.LastReminderSent.Value.ToUniversalTime() <= threshold;

                        bool shouldSendReminder = hasStaleDraft
                            || (inv.ReminderCount > 0 && reminderDelayPassed);

                        if (!shouldSendReminder)
                        {
                            _logger.LogDebug("[SKIP] Invitation {Id} : délai non encore atteint.", inv.Id);
                            continue;
                        }

                        // 5. Préparation du contenu de l'email
                        string campaignName = campaign?.Name ?? "notre enquête";
                        var link = $"{baseUrl}/Survey/Fill?token={inv.Token}";

                        var subject = (reminderTemplate.Subject ?? "")
                            .Replace("[SurveyLink]", link)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[CampagneName]", campaignName)
                            .Replace("[ClientName]", clientName)
                            .Replace("{{SurveyLink}}", link)
                            .Replace("{{CampaignName}}", campaignName)
                            .Replace("{{CampagneName}}", campaignName)
                            .Replace("{{ClientName}}", clientName);

                        var body = (reminderTemplate.Body ?? "")
                            .Replace("[SurveyLink]", link)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[CampagneName]", campaignName)
                            .Replace("[ClientName]", clientName)
                            .Replace("{{SurveyLink}}", link)
                            .Replace("{{CampaignName}}", campaignName)
                            .Replace("{{CampagneName}}", campaignName)
                            .Replace("{{ClientName}}", clientName)
                            .Replace("[br]", "<br/>")
                            .Replace("\n", "<br/>");

                        // 6. Envoi de l'email
                        try
                        {
                            _logger.LogInformation("[RAPPEL] Envoi à {Email} (Client: {Name}, Invitation: {Id}, Rappel #{N})",
                                email, clientName, inv.Id, inv.ReminderCount + 1);

                            await emailService.SendEmailAsync(email, subject, body);

                            inv.ReminderCount += 1;
                            inv.LastReminderSent = DateTime.UtcNow;
                            inv.SyncStatus = "Reminded";

                            db.SurveyInvitations.Update(inv);
                            await db.SaveChangesAsync(stoppingToken);

                            _logger.LogInformation("[OK] Rappel #{N} envoyé pour invitation {Id}", inv.ReminderCount, inv.Id);
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