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

        public ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("--- [DÉMARRAGE] ReminderWorker est lancé ---");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("--- [BOUCLE] Début du cycle de vérification : {Time} ---", DateTime.Now);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var dataverseService = scope.ServiceProvider.GetService<DataverseService>();

                    var threshold = DateTime.Now;

                    // Récupérer les invitations candidates
                    var invites = await db.SurveyInvitations
                        .Where(i => !i.IsUsed && i.ReminderCount < 3)
                        .ToListAsync(stoppingToken);

                    _logger.LogInformation("[INFO] Invitations candidates trouvées en base : {Count}", invites.Count);

                    foreach (var inv in invites)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        _logger.LogInformation("[ANALYSE] Vérification Invitation ID: {Id} (Token: {Token})", inv.Id, inv.Token);

                        bool hasStarted = inv.LastPartialSave.HasValue;
                        bool hasStaleDraft = hasStarted && inv.LastPartialSave.Value <= threshold;
                        bool reminderDelayPassed = inv.LastReminderSent.HasValue && inv.LastReminderSent.Value <= threshold;

                        _logger.LogDebug("[DEBUG] ID:{Id} -> A Commencé:{S}, Brouillon Expire:{B}, Délai Rappel Passé:{D}",
                            inv.Id, hasStarted, hasStaleDraft, reminderDelayPassed);

                        var shouldSend = hasStaleDraft || reminderDelayPassed;

                        if (!shouldSend)
                        {
                            _logger.LogInformation("[IGNORÉ] Invitation {Id} ne remplit pas encore les critères de temps.", inv.Id);
                            continue;
                        }

                        _logger.LogInformation("[ACTION] Tentative d'envoi pour l'invitation {Id}...", inv.Id);

                        // Récupérer l'email du participant via Dataverse
                        string? email = null;
                        string participantName = string.Empty;
                        try
                        {
                            if (dataverseService != null)
                            {
                                _logger.LogInformation("[DATAVERSE] Recherche d'email pour Participant Dataverse ID: {PId}", inv.ParticipantDataverseId);

                                var ctx = dataverseService.GetSurveyContextInfo(inv.ParticipantDataverseId);
                                Guid? campagneId = null;

                                if (ctx != null)
                                {
                                    try { campagneId = (Guid?)(ctx.CampagneId ?? null); } catch { }
                                }

                                if (campagneId.HasValue)
                                {
                                    var participants = dataverseService.GetParticipantsByCampaign(campagneId.Value);
                                    var participant = participants?.FirstOrDefault(p => p.Id == inv.ParticipantDataverseId);
                                    email = participant?.Email;
                                    // Utiliser l'email comme fallback pour le nom si le nom n'est pas disponible
                                    participantName = participant?.Email ?? string.Empty;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("[ERREUR] DataverseService est null !");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[ERREUR DATAVERSE] Impossible de récupérer l'email pour l'invitation {Id}", inv.Id);
                        }

                        if (string.IsNullOrWhiteSpace(email))
                        {
                            _logger.LogWarning("[ALERTE] Aucun email trouvé pour l'invitation {Id}. Ignoré.", inv.Id);
                            continue;
                        }

                        // Récupérer la campagne liée pour obtenir le template de rappel
                        var campaign = await db.Campaigns
                            .AsNoTracking()
                            .FirstOrDefaultAsync(c => c.DataverseId == inv.CampaignDataverseId, stoppingToken);

                        string subjectTemplate = campaign?.ReminderSubject ?? "Rappel : merci de compléter le sondage";
                        string bodyTemplate = campaign?.ReminderBody ?? $"Bonjour,[br][br]Merci de compléter votre sondage : [SurveyLink][br][br]Cordialement.";

                        var link = $"https://localhost:7265/Survey/Fill?token={inv.Token}";
                        var campaignName = campaign?.Name ?? string.Empty;

                        // Remplacements simples de tags
                        var subject = subjectTemplate
                            .Replace("[ClientName]", participantName)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[SurveyLink]", link);

                        var body = bodyTemplate
                            .Replace("[ClientName]", participantName)
                            .Replace("[CampaignName]", campaignName)
                            .Replace("[SurveyLink]", link)
                            .Replace("[br]", "<br/>"); // optionally support [br] marker

                        try
                        {
                            _logger.LogInformation("[EMAIL] Envoi du mail à {Email} pour l'invitation {Id}", email, inv.Id);
                            await emailService.SendEmailAsync(email, subject, body);

                            // Mise à jour SQL
                            inv.ReminderCount += 1;
                            inv.LastReminderSent = DateTime.Now;

                            db.SurveyInvitations.Update(inv);
                            await db.SaveChangesAsync(stoppingToken);

                            _logger.LogInformation("[SUCCÈS] Rappel envoyé et compteur mis à jour (Count={Count}) pour ID {Id}", inv.ReminderCount, inv.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[ERREUR ENVOI] Échec de l'envoi ou de la mise à jour SQL pour l'invitation {Id}", inv.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ERREUR CRITIQUE] Une erreur est survenue dans la boucle du ReminderWorker.");
                }

                _logger.LogInformation("--- [FIN DE BOUCLE] Prochaine vérification dans 60 secondes ---");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("--- [ARRÊT] ReminderWorker arrêté ---");
        }
    }
}