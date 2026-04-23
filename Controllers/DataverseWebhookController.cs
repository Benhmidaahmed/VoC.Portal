using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Text.Json;

using System.Text.Json.Serialization;

using System.Threading.Tasks;

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

        private readonly IConfiguration _configuration;

        public DataverseWebhookController(AppDbContext dbContext, IEmailService emailService, IConfiguration configuration)

        {

            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

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



            // Synchronisation suppression Email (Delete Dataverse)
            if (string.Equals(evt.MessageName, "Delete", StringComparison.OrdinalIgnoreCase)
                && string.Equals(evt.EntityType, "cr7a2_emaildecampagne", StringComparison.OrdinalIgnoreCase))
            {
                var emailToDelete = await _dbContext.CampaignEmails
                    .FirstOrDefaultAsync(e => e.DataverseId == evt.EntityId);

                if (emailToDelete != null)
                {
                    // Vérification des invitations liées (via la campagne de l'email)
                    var hasLinkedInvitations = await _dbContext.SurveyInvitations
                        .AnyAsync(i => i.CampaignDataverseId == emailToDelete.CampaignId);

                    if (hasLinkedInvitations)
                    {
                        await TryAddLogAsync(
                            "WebhookReceived",
                            "Warning",
                            $"Suppression annulée pour l'email {evt.EntityId} : des invitations liées existent.",
                            "CampaignEmail");

                        return Ok();
                    }

                    _dbContext.CampaignEmails.Remove(emailToDelete);
                    await _dbContext.SaveChangesAsync();

                    await TryAddLogAsync(
                        "WebhookReceived",
                        "Success",
                        $"Email de campagne supprimé localement : {evt.EntityId}",
                        "CampaignEmail");
                }

                return Ok();
            }



            // Synchronisation suppression Campagne (Delete Dataverse)

            if (string.Equals(evt.MessageName, "Delete", StringComparison.OrdinalIgnoreCase)

                && string.Equals(evt.EntityType, "xrmbox_campagnedesatisfaction", StringComparison.OrdinalIgnoreCase))

            {

                var campaignToDelete = await _dbContext.Campaigns

                    .FirstOrDefaultAsync(c => c.DataverseId == evt.EntityId);



                if (campaignToDelete != null)

                {

                    var relatedEmails = await _dbContext.CampaignEmails

                        .Where(e => e.CampaignId == evt.EntityId)

                        .ToListAsync();



                    if (relatedEmails.Any())

                    {

                        _dbContext.CampaignEmails.RemoveRange(relatedEmails);

                    }



                    var relatedInvitations = await _dbContext.SurveyInvitations

                        .Where(i => i.CampaignDataverseId == evt.EntityId)

                        .ToListAsync();



                    if (relatedInvitations.Any())

                    {

                        _dbContext.SurveyInvitations.RemoveRange(relatedInvitations);

                    }



                    _dbContext.Campaigns.Remove(campaignToDelete);

                    await _dbContext.SaveChangesAsync();



                    await TryAddLogAsync(

                        "WebhookReceived",

                        "Success",

                        $"Campagne supprimée localement : {evt.EntityId}",

                        "Campaign");

                }



                return Ok();

            }



            try

            {

                // --- LOGIQUE QUESTIONNAIRE ---

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

                // --- LOGIQUE CAMPAGNE (Modifiée pour corriger le stockage d'emails) ---

                else if (string.Equals(evt.EntityType, "xrmbox_campagnedesatisfaction", StringComparison.OrdinalIgnoreCase))
                {
                    var campaign = await _dbContext.Campaigns
                        .FirstOrDefaultAsync(c => c.DataverseId == evt.EntityId);

                    var isCreated = false;
                    if (campaign == null)
                    {
                        campaign = new Campaign { DataverseId = evt.EntityId };
                        _dbContext.Campaigns.Add(campaign);
                        isCreated = true;
                    }

                    campaign.Name = evt.Name ?? campaign.Name;
                    campaign.PageDesignHtml = evt.PageDesignHtml ?? campaign.PageDesignHtml;
                    campaign.CouleurPrimaire = evt.CouleurPrimaire ?? campaign.CouleurPrimaire;
                    campaign.LastSync = DateTime.UtcNow;
                    campaign.StateCode = evt.StateCode;
                    campaign.StatusCode = evt.StatusCode;

                    // ✅ Ne mettre à jour SurveyDataverseId que si la valeur est présente
                    if (evt.SurveyDataverseId.HasValue && evt.SurveyDataverseId.Value != Guid.Empty)
                        campaign.SurveyDataverseId = evt.SurveyDataverseId.Value;

                    await _dbContext.SaveChangesAsync();

                    await TryAddLogAsync("WebhookReceived", "Success",
                        isCreated ? $"Campagne CRÉÉE : {evt.EntityId}" : $"Campagne MISE À JOUR : {evt.EntityId}",
                        "Campaign");

                    if (evt.Participants != null && evt.Participants.Any())
                    {
                        int created = 0;
                        int skipped = 0;
                        int emailsSent = 0;
                        int emailsFailed = 0;

                        // Récupérer le template d'invitation (Role = 0)
                        var invitationTemplate = await _dbContext.CampaignEmails
                            .FirstOrDefaultAsync(e => e.CampaignId == evt.EntityId && e.Role == 0);

                        if (invitationTemplate == null)
                        {
                            await TryAddLogAsync("SyncInvitations", "Warning",
                                $"Aucun template email (Role=0) trouvé pour campagne {evt.EntityId}. Emails non envoyés.",
                                "CampaignEmail");
                        }

                        var baseUrl = _configuration["AppSettings:BaseUrl"];

                        foreach (var p in evt.Participants)
                        {
                            if (p.Id == Guid.Empty) { skipped++; continue; }

                            // Anti-doublon
                            var alreadyExists = await _dbContext.SurveyInvitations
                                .AnyAsync(i =>
                                    i.ParticipantDataverseId == p.Id &&
                                    i.CampaignDataverseId == evt.EntityId &&
                                    i.IsActive);

                            if (alreadyExists) { skipped++; continue; }

                            // ✅ CORRECTION 1 : IsActive = true (était false dans ton code)
                            var token = Guid.NewGuid();
                            var invitation = new SurveyInvitation
                            {
                                Token = token,
                                ParticipantDataverseId = p.Id,
                                CampaignDataverseId = evt.EntityId,
                                ExpirationDate = DateTime.UtcNow.AddDays(30),
                                IsUsed = false,
                                IsActive = true,  // ✅ ÉTAIT false — BUG CRITIQUE
                                ReminderCount = 0,
                                SyncStatus = "Pending"
                            };

                            _dbContext.SurveyInvitations.Add(invitation);
                            created++;

                            // ✅ CORRECTION 2 : Envoi de l'email d'invitation
                            if (invitationTemplate != null && !string.IsNullOrWhiteSpace(p.Email))
                            {
                                try
                                {
                                    var surveyLink = $"{baseUrl}/Survey/Fill?token={token}";

                                    var subject = (invitationTemplate.Subject ?? "")
                                         .Replace("[ClientName]", p.ClientName ?? "")
    .Replace("[SurveyLink]", surveyLink)
    .Replace("[CampagneName]", campaign.Name ?? "")
    .Replace("[CampaignName]", campaign.Name ?? "")
     .Replace("{{ClientName}}", p.ClientName ?? "")
    .Replace("{{SurveyLink}}", surveyLink)
    .Replace("{{CampagneName}}", campaign.Name ?? "");

                                    var body = (invitationTemplate.Body ?? "")
                                       .Replace("[ClientName]", p.ClientName ?? "")
    .Replace("[SurveyLink]", surveyLink)
    .Replace("[CampagneName]", campaign.Name ?? "")
    .Replace("[CampaignName]", campaign.Name ?? "")
    .Replace("{{ClientName}}", p.ClientName ?? "")
    .Replace("{{SurveyLink}}", surveyLink)
    .Replace("{{CampagneName}}", campaign.Name ?? "");

                                    await _emailService.SendEmailAsync(p.Email, subject, body);
                                    emailsSent++;

                                    await TryAddLogAsync("SendInvitationEmail", "Success",
                                        $"Email envoyé à {p.Email}, token={token}",
                                        "SurveyInvitation");
                                }
                                catch (Exception emailEx)
                                {
                                    emailsFailed++;
                                    await TryAddLogAsync("SendInvitationEmail", "Error",
                                        $"Échec envoi à {p.Email} : {emailEx.Message}",
                                        "SurveyInvitation");
                                    // L'invitation est quand même gardée en base
                                }
                            }
                            else if (string.IsNullOrWhiteSpace(p.Email))
                            {
                                await TryAddLogAsync("SendInvitationEmail", "Warning",
                                    $"Participant {p.Id} sans email — invitation créée, email non envoyé.",
                                    "SurveyInvitation");
                            }
                        }

                        // ✅ CORRECTION 3 : SaveChanges manquait complètement
                        await _dbContext.SaveChangesAsync();

                        await TryAddLogAsync("SyncInvitations", "Success",
                            $"{created} invitation(s) créée(s), {skipped} doublon(s), {emailsSent} email(s) envoyé(s), {emailsFailed} échec(s)",
                            "SurveyInvitation");
                    }
                    else
                    {
                        await TryAddLogAsync("SyncInvitations", "Warning",
                            $"Aucun participant reçu pour campagne {evt.EntityId}.",
                            "SurveyInvitation");
                    }



                    await TryAddLogAsync("WebhookReceived", "Success", $"Processed {evt.EntityType}", evt.EntityType);

                }
            }

            catch (Exception ex)

            {

                await TryAddLogAsync("WebhookReceived", "Error", ex.ToString(), evt.EntityType);

            }



            return Ok();

        }



        // Nouveau endpoint indépendant pour journaliser l'exécution d'un Power Automate Flow

        [HttpPost("flow-log")]

        [IgnoreAntiforgeryToken]

        public async Task<IActionResult> FlowLog([FromBody] FlowLogDto flow)

        {

            Console.WriteLine($"LOG REÇU : Flow={flow?.FlowName}, Status={flow?.Status}, Msg={flow?.ErrorMessage}");



            if (flow == null)

            {

                await TryAddLogAsync("LogFlowExecution", "Error", "Payload null", null);

                return BadRequest();

            }



            try

            {

                var userFriendlyMessage = BuildUserFriendlyMessage(flow.ErrorMessage);



                await TryAddLogAsync(

                    action: "PowerAutomateFlow",

                    status: flow.Status ?? string.Empty,

                    ErrorMessage: userFriendlyMessage,

                    entityName: flow.FlowName ?? string.Empty

                );



                return Ok();

            }

            catch (Exception ex)

            {

                await TryAddLogAsync("LogFlowExecution", "Error", ex.Message, flow.FlowName);

                return StatusCode(500);

            }

        }



        private static string BuildUserFriendlyMessage(string? errorPayload)

        {

            if (string.IsNullOrWhiteSpace(errorPayload))

                return "Erreur technique (vérifier Power Automate)";



            try

            {

                using var doc = JsonDocument.Parse(errorPayload);



                // Si ce n'est pas un tableau (historique d'actions), on extrait le message direct

                if (doc.RootElement.ValueKind != JsonValueKind.Array)

                {

                    return ExtractOutputsMessage(doc.RootElement) ?? errorPayload;

                }



                foreach (var action in doc.RootElement.EnumerateArray())

                {

                    var status = GetString(action, "status");

                    if (!string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)) continue;



                    var actionName = GetString(action, "name") ?? string.Empty;

                    var rawMessage = ExtractOutputsMessage(action);



                    // --- PRIORITÉ 1 : Détection spécifique des oublis de configuration ---



                    // Cas : Oubli de questionnaire ou participant (Validation Error Dataverse)

                    if (rawMessage != null && rawMessage.Contains("One or more validation errors occurred", StringComparison.OrdinalIgnoreCase))

                    {

                        return "Erreur de configuration : Un champ obligatoire est manquant (ex: Questionnaire ou Client non renseigné).";

                    }



                    // Cas : Liste vide (Lister les lignes)

                    if (actionName.Contains("Lister_les_lignes", StringComparison.OrdinalIgnoreCase))

                    {

                        var body = GetElement(action, "outputs", "body");

                        if (IsEmptyValueArray(body))

                        {

                            return "Erreur : Aucun participant éligible n'a été trouvé pour cette campagne.";

                        }

                    }



                    // Cas : Condition spécifique au questionnaire

                    if (actionName.Contains("Questionnaire", StringComparison.OrdinalIgnoreCase))

                    {

                        return "Erreur : Le questionnaire lié est introuvable ou désactivé.";

                    }



                    // --- PRIORITÉ 2 : Erreurs techniques Dataverse (Connectivité) ---

                    var statusCode = GetInt(action, "outputs", "statusCode");

                    if (statusCode.HasValue && statusCode.Value >= 400)

                    {

                        return $"Erreur technique Dataverse ({statusCode}) : {rawMessage ?? "Détail indisponible"}";

                    }



                    // --- PRIORITÉ 3 : Fallback ---

                    if (!string.IsNullOrWhiteSpace(rawMessage)) return rawMessage;

                }

            }

            catch

            {

                return "Format d'erreur illisible";

            }



            return "Erreur inconnue lors de l'exécution du flux";

        }



        private static bool IsEmptyValueArray(JsonElement? body)

        {

            if (body == null || body.Value.ValueKind == JsonValueKind.Undefined) return false;



            if (body.Value.ValueKind == JsonValueKind.Object

                && body.Value.TryGetProperty("value", out var valueElem)

                && valueElem.ValueKind == JsonValueKind.Array)

            {

                return valueElem.GetArrayLength() == 0;

            }



            return false;

        }



        private static string? ExtractOutputsMessage(JsonElement element)

        {

            return GetString(element, "outputs", "body", "message")

                ?? GetString(element, "outputs", "body", "error", "message")

                ?? GetString(element, "outputs", "body", "title")

                ?? GetString(element, "outputs", "message")

                ?? GetString(element, "outputs", "title");

        }



        private static string? GetString(JsonElement element, params string[] path)

        {

            var current = element;

            foreach (var segment in path)

            {

                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))

                {

                    return null;

                }

            }



            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();

        }



        private static int? GetInt(JsonElement element, params string[] path)

        {

            var current = element;

            foreach (var segment in path)

            {

                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))

                {

                    return null;

                }

            }



            if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value)) return value;

            return null;

        }



        private async Task TryAddLogAsync(string action, string status, string ErrorMessage, string? entityName)

        {

            try

            {

                var log = new IntegrationLog

                {

                    EventDate = DateTime.UtcNow,

                    EntityName = entityName ?? string.Empty,

                    Action = action,

                    Status = status,

                    Message = ErrorMessage

                };



                _dbContext.IntegrationLogs.Add(log);

                await _dbContext.SaveChangesAsync();

            }

            catch

            {

                // Ne pas remonter l'exception de logging pour ne pas casser le webhook

            }

        }



        [HttpPost("sync-email")]

        [IgnoreAntiforgeryToken]

        public async Task<IActionResult> SyncEmail([FromBody] JsonElement data)

        {

            try

            {

                var emailId = data.GetProperty("id").GetGuid();

                var subject = data.GetProperty("subject").GetString();

                var body = data.GetProperty("body").GetString();

                var campaignId = data.GetProperty("campaignId").GetGuid();



                // ✅ CORRECTION 1 : lire Role depuis le JSON (pas hardcodé)

                int role = 1; // fallback

                if (data.TryGetProperty("Role", out var roleProp) && roleProp.ValueKind == JsonValueKind.Number)

                    role = roleProp.GetInt32();



                // ✅ CORRECTION 2 : lire DelayDays depuis le JSON

                int? delayDays = null;

                if (data.TryGetProperty("delayDays", out var delayProp) && delayProp.ValueKind == JsonValueKind.Number)

                    delayDays = delayProp.GetInt32();



                var existingEmail = await _dbContext.CampaignEmails

                    .FirstOrDefaultAsync(e => e.DataverseId == emailId);



                if (existingEmail != null)

                {

                    existingEmail.Subject = subject ?? existingEmail.Subject;

                    existingEmail.Body = body ?? existingEmail.Body;

                    existingEmail.Role = role;       // ✅ mise à jour du Role

                    existingEmail.DelayDays = delayDays;  // ✅ mise à jour du DelayDays

                    _dbContext.CampaignEmails.Update(existingEmail);

                }

                else

                {

                    var newEmail = new CampaignEmail

                    {

                        DataverseId = emailId,

                        Subject = subject ?? "",

                        Body = body ?? "",

                        CampaignId = campaignId,

                        Role = role,       // ✅ valeur lue depuis Power Automate

                        DelayDays = delayDays   // ✅ valeur lue depuis Power Automate

                    };

                    _dbContext.CampaignEmails.Add(newEmail);

                }



                await _dbContext.SaveChangesAsync();

                return Ok(new { message = "Email synchronisé avec succès" });

            }

            catch (Exception ex)

            {

                return BadRequest(new { error = ex.Message });

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
            public string? MessageName { get; set; }
            public Guid? SurveyDataverseId { get; set; }
            public string? PageDesignHtml { get; set; } // Ajoutez ceci

            public string? CouleurPrimaire { get; set; }



            // Templates d'email

            public string? InvitationSubject { get; set; }

            public string? InvitationBody { get; set; }

            public string? ReminderSubject { get; set; }

            public string? ReminderBody { get; set; }

            public int? DelayDays { get; set; }



            public List<ParticipantDto>? Participants { get; set; }

        }



        public class ParticipantDto

        {

            public Guid Id { get; set; }

            public string? Email { get; set; }

            public int StatusCode { get; set; }

            public string? ClientName { get; set; }

        }



        // DTO demandé pour l'endpoint flow-log

        public class FlowLogDto

        {

            public string? FlowName { get; set; }

            public string? Status { get; set; }

            public string? ErrorMessage { get; set; }

        }



        private static JsonElement? GetElement(JsonElement element, params string[] path)

        {

            var current = element;

            foreach (var segment in path)

            {

                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))

                {

                    return null;

                }

            }

            return current;

        }

    }

}