using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models.Local;
using Xrmbox.VoC.Portal.Services;

namespace Xrmbox.VoC.Portal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataverseWebhookController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IEmailService _emailService;

        public DataverseWebhookController(AppDbContext dbContext, IEmailService emailService)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ReceiveEvent([FromBody] DataverseEventDto evt)
        {
            if (evt == null)
            {
                await TryAddLogAsync("WebhookReceived", "Error", "Payload null", null);
                return BadRequest();
            }

            try
            {
                if (string.Equals(evt.EntityType, "xrmbox_questionnairedesatisfaction", StringComparison.OrdinalIgnoreCase))
                {
                    if (evt.StateCode == 0 && evt.StatusCode == 1)
                    {
                        var survey = await _dbContext.Surveys.FirstOrDefaultAsync(s => s.DataverseId == evt.EntityId);
                        if (survey == null)
                        {
                            survey = new Survey { DataverseId = evt.EntityId };
                            _dbContext.Surveys.Add(survey);
                        }
                        survey.JsonContent = evt.JsonContent;
                        survey.IsActive = true;
                        survey.LastSync = DateTime.UtcNow;
                    }
                    else
                    {
                        var survey = await _dbContext.Surveys.FirstOrDefaultAsync(s => s.DataverseId == evt.EntityId);
                        if (survey != null) survey.IsActive = false;

                        var invitations = await _dbContext.SurveyInvitations
                            .Where(i => i.IsActive)
                            .ToListAsync();

                        foreach (var inv in invitations) inv.IsActive = false;
                    }

                    await _dbContext.SaveChangesAsync();
                }
                else if (string.Equals(evt.EntityType, "xrmbox_campagnedesatisfaction", StringComparison.OrdinalIgnoreCase))
                {
                    var campaign = await _dbContext.Campaigns.FirstOrDefaultAsync(c => c.DataverseId == evt.EntityId);
                    if (campaign == null)
                    {
                        campaign = new Campaign { DataverseId = evt.EntityId };
                        _dbContext.Campaigns.Add(campaign);
                    }

                    // Mettre à jour les champs y compris les templates d'email provenant de l'événement
                    campaign.Name = evt.Name ?? campaign.Name;
                    campaign.StateCode = evt.StateCode;
                    campaign.StatusCode = evt.StatusCode;
                    campaign.SurveyDataverseId = evt.SurveyDataverseId;
                    campaign.LastSync = DateTime.UtcNow;

                    campaign.InvitationSubject = evt.InvitationSubject ?? campaign.InvitationSubject;
                    campaign.InvitationBody = evt.InvitationBody ?? campaign.InvitationBody;
                    campaign.ReminderSubject = evt.ReminderSubject ?? campaign.ReminderSubject;
                    campaign.ReminderBody = evt.ReminderBody ?? campaign.ReminderBody;

                    await _dbContext.SaveChangesAsync();

                    if (evt.StateCode == 0)
                    {
                        // Vérifier que le sondage lié à la campagne existe et est actif.
                        var linkedSurvey = await _dbContext.Surveys
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.DataverseId == evt.SurveyDataverseId);

                        if (linkedSurvey == null || !linkedSurvey.IsActive)
                        {
                            // Le questionnaire est absent ou inactif : on n'envoie aucune invitation.
                            await TryAddLogAsync(
                                "WebhookReceived",
                                "Error",
                                $"Linked survey {evt.SurveyDataverseId} not found or inactive. Skipping invitations for campaign {evt.EntityId}.",
                                "Survey");
                            return Ok();
                        }

                        var participants = evt.Participants ?? new List<ParticipantDto>();

                        foreach (var p in participants)
                        {
                            // Vérifie que le participant est actif dans Dataverse (StatusCode == 1).
                            // Cette vérification garantit l'envoi uniquement aux participants valides.
                            if (p.StatusCode != 1) continue;

                            var exists = await _dbContext.SurveyInvitations
                                .AnyAsync(i => i.ParticipantDataverseId == p.Id && i.CampaignDataverseId == evt.EntityId);

                            if (!exists)
                            {
                                var invitation = new SurveyInvitation
                                {
                                    Token = Guid.NewGuid(),
                                    ParticipantDataverseId = p.Id,
                                    CampaignDataverseId = evt.EntityId,
                                    ExpirationDate = DateTime.UtcNow.AddDays(7),
                                    IsUsed = false,
                                    IsActive = true,
                                    SyncStatus = "Pending"
                                };

                                _dbContext.SurveyInvitations.Add(invitation);
                                await _dbContext.SaveChangesAsync();

                                // Préparer le sujet et le corps en utilisant le template stocké dans la campagne (si présent)
                                var subjectTemplate = campaign.InvitationSubject ?? "Votre avis nous intéresse";
                                var bodyTemplate = campaign.InvitationBody ?? $"Bonjour,<br/><br/>Merci de compléter notre sondage : [SurveyLink]<br/><br/>Cordialement.";

                                // Remplacements simples de tags
                                // Remplacements simples de tags
                                // On utilise d'abord ClientName, sinon l'Email
                                var participantName = !string.IsNullOrEmpty(p.ClientName) ? p.ClientName : (p.Email ?? string.Empty); // fallback si nom indisponible
                                var campaignName = campaign.Name ?? string.Empty;
                                var link = $"https://localhost:7265/Survey/Fill?token={invitation.Token}";

                                var subject = subjectTemplate
                                    .Replace("[ClientName]", participantName)
                                    .Replace("[CampaignName]", campaignName)
                                    .Replace("[SurveyLink]", link);

                                var body = (bodyTemplate ?? string.Empty)
                                    .Replace("[ClientName]", participantName)
                                    .Replace("[CampaignName]", campaignName)
                                    .Replace("[SurveyLink]", link);

                                // Envoi d'email non bloquant : erreurs capturées pour ne pas annuler l'enregistrement
                                try
                                {
                                    await _emailService.SendEmailAsync(p.Email ?? string.Empty, subject, body);
                                }
                                catch (Exception ex)
                                {
                                    // Log l'erreur d'envoi sans interrompre le traitement
                                    await TryAddLogAsync("EmailSend", "Error", $"Email failed for participant {p.Id}: {ex}", "SurveyInvitation");
                                }
                            }
                        }
                    }
                }

                await TryAddLogAsync("WebhookReceived", "Success",
                    $"Processed event EntityType={evt.EntityType} EntityId={evt.EntityId} StateCode={evt.StateCode}",
                    evt.EntityType);
            }
            catch (Exception ex)
            {
                await TryAddLogAsync("WebhookReceived", "Error", ex.ToString(), evt.EntityType);
            }

            return Ok();
        }

        private async Task TryAddLogAsync(string action, string status, string message, string? entityName)
        {
            try
            {
                var log = new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = entityName ?? string.Empty,
                    Action = action,
                    Status = status,
                    Message = message
                };

                _dbContext.IntegrationLogs.Add(log);
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                // Ne pas remonter l'exception de logging pour ne pas casser le webhook
            }
        }

        // DTOs pour l'API webhook
        public class DataverseEventDto
        {
            public string EntityType { get; set; } = string.Empty;
            public Guid EntityId { get; set; }
            public int StateCode { get; set; }
            public int StatusCode { get; set; }
            public string? JsonContent { get; set; }
            public string? Name { get; set; }
            public Guid SurveyDataverseId { get; set; }

            // Templates d'email
            public string? InvitationSubject { get; set; }
            public string? InvitationBody { get; set; }
            public string? ReminderSubject { get; set; }
            public string? ReminderBody { get; set; }

            public List<ParticipantDto>? Participants { get; set; }
        }

        public class ParticipantDto
        {
            public Guid Id { get; set; }
            public string? Email { get; set; }
            public int StatusCode { get; set; }
            public string? ClientName { get; set; }
        }
    }
}
