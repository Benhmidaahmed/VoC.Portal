using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models.Local;
using Microsoft.EntityFrameworkCore;

namespace Xrmbox.VoC.Portal.Services
{
    public class DataverseOptions
    {
        public string? ConnectionString { get; set; }
    }

    public record CampaignDto(Guid Id, string Name);
    public record ParticipantDto(Guid Id, string? Email);

    public partial class DataverseService : IDisposable
    {
        private readonly ServiceClient _client;
        private readonly AppDbContext _dbContext;

        public DataverseService(IConfiguration configuration, AppDbContext dbContext)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (dbContext == null) throw new ArgumentNullException(nameof(dbContext));

            var conn = configuration["Dataverse:ConnectionString"];
            if (string.IsNullOrWhiteSpace(conn))
            {
                throw new InvalidOperationException("La clé Dataverse:ConnectionString est requise dans la configuration.");
            }

            _client = new ServiceClient(conn);
            if (!_client.IsReady)
            {
                throw new InvalidOperationException("Impossible de se connecter à Dataverse via ServiceClient. Vérifiez la connection string.");
            }

            _dbContext = dbContext;
        }

        /// <summary>
        /// Récupère le JSON du questionnaire.
        /// </summary>
        public string? GetSurvey(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("id invalide", nameof(id));

            const string entityName = "xrmbox_questionnairedesatisfaction";
            const string targetAttribute = "xrmbox_json";

            var columns = new ColumnSet(targetAttribute);

            try
            {
                var entity = _client.Retrieve(entityName, id, columns);
                if (entity == null) return null;

                if (entity.Attributes.TryGetValue(targetAttribute, out var value) && value != null)
                {
                    return value.ToString();
                }

                // Diagnostic si non trouvé
                var match = entity.Attributes.FirstOrDefault(a => string.Equals(a.Key, targetAttribute, StringComparison.OrdinalIgnoreCase));
                if (!match.Equals(default(KeyValuePair<string, object>)) && match.Value != null)
                {
                    return match.Value.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetSurvey failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Retourne la liste des noms logiques d'attributs pour une entité.
        /// </summary>
        public IEnumerable<string> GetEntityAttributes(string entityLogicalName)
        {
            if (string.IsNullOrWhiteSpace(entityLogicalName)) throw new ArgumentNullException(nameof(entityLogicalName));

            try
            {
                var req = new RetrieveEntityRequest
                {
                    LogicalName = entityLogicalName,
                    EntityFilters = EntityFilters.Attributes,
                    RetrieveAsIfPublished = true
                };

                var resp = (RetrieveEntityResponse)_client.Execute(req);
                var metadata = resp.EntityMetadata;
                if (metadata?.Attributes == null) return Array.Empty<string>();

                return metadata.Attributes.Select(a => a.LogicalName).OrderBy(n => n).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetEntityAttributes failed for '{entityLogicalName}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Info de connexion / debug
        /// </summary>
        public object GetConnectionInfo()
        {
            try
            {
                var who = (WhoAmIResponse)_client.Execute(new WhoAmIRequest());
                return new
                {
                    UserId = who.UserId,
                    OrgUniqueName = _client.ConnectedOrgUniqueName,
                    OrgFriendlyName = _client.ConnectedOrgFriendlyName
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetConnectionInfo failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Récupère toutes les campagnes (xrmbox_campagnedesatisfaction) en sélectionnant l'id logique et le nom.
        /// </summary>
        public IEnumerable<CampaignDto> GetAllCampaigns()
        {
            try
            {
                var query = new QueryExpression("xrmbox_campagnedesatisfaction")
                {
                    ColumnSet = new ColumnSet("xrmbox_campagnedesatisfactionid", "xrmbox_name")
                };

                var results = _client.RetrieveMultiple(query);
                return results.Entities
                    .Select(e => new CampaignDto(
                        e.Id,
                        e.GetAttributeValue<string>("xrmbox_name") ?? string.Empty))
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetAllCampaigns failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Récupère les participants pour une campagne donnée (lookup xrmbox_campagnedesatisfaction).
        /// Sélectionne l'id primaire et le champ email xrmbox_adressecourriel.
        /// </summary>
        public IEnumerable<ParticipantDto> GetParticipantsByCampaign(Guid campaignId)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("campaignId invalide", nameof(campaignId));

            try
            {
                var query = new QueryExpression("xrmbox_participantalacampagne")
                {
                    ColumnSet = new ColumnSet("xrmbox_participantalacampagneid", "xrmbox_adressecourriel"),
                    Criteria = new FilterExpression()
                };

                query.Criteria.AddCondition("xrmbox_campagnedesatisfaction", ConditionOperator.Equal, campaignId);

                var results = _client.RetrieveMultiple(query);
                return results.Entities
                    .Select(e => new ParticipantDto(
                        e.Id,
                        e.GetAttributeValue<string>("xrmbox_adressecourriel")))
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetParticipantsByCampaign failed for '{campaignId}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tente de synchroniser une LocalResponse vers Dataverse.
        /// Met à jour les champs IsSynced, DataverseId et SyncError en base locale.
        /// </summary>
        public void SyncLocalResponseToDataverse(LocalResponse local)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));

            const string entityName = "xrmbox_reponsedesatisfaction";
            var dtEntity = new Entity(entityName);

            try
            {
                dtEntity["xrmbox_name"] = local.Name;
                dtEntity["xrmbox_questionnairedesatisfaction"] = new EntityReference("xrmbox_questionnairedesatisfaction", local.SurveyId);

                if (local.ParticipantId.HasValue && local.ParticipantId.Value != Guid.Empty)
                {   
                    dtEntity["xrmbox_participantalacampagne"] = new EntityReference("xrmbox_participantalacampagne", local.ParticipantId.Value);
                }

                if (local.CampagneId.HasValue && local.CampagneId.Value != Guid.Empty)
                {
                    dtEntity["xrmbox_campagnedesatisfaction"] = new EntityReference("xrmbox_campagnedesatisfaction", local.CampagneId.Value);
                }

                // Utilise le nom logique exact que tu viens de créer
                dtEntity["cr7a2_reponsesjson"] = local.ResponseJson;

                var createdId = _client.Create(dtEntity);

                local.IsSynced = true;
                local.DataverseId = createdId.ToString();
                local.SyncError = null;

                _dbContext.LocalResponses.Update(local);
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                // En cas d'erreur, enregistrer le message d'erreur pour diagnostics
                local.IsSynced = false;
                local.SyncError = ex.ToString();

                try
                {
                    _dbContext.LocalResponses.Update(local);
                    _dbContext.SaveChanges();
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"[Dataverse Error] Failed to persist sync error for LocalResponse Id {local.Id}: {saveEx.Message}");
                }
            }
        }

        public void SyncPendingResponses()
        {
            // 1. On récupère les IDs uniquement pour éviter les problèmes de cache EF
            var pendingIds = _dbContext.LocalResponses
                .AsNoTracking()
                .Where(r => !r.IsSynced)
                .Select(r => r.Id)
                .ToList();

            if (!pendingIds.Any()) return;

            Console.WriteLine($"[Sync] Relance de {pendingIds.Count} anciennes réponses...");

            foreach (var id in pendingIds)
            {
                // 2. On récupère proprement l'entité par son ID pour chaque itération
                var response = _dbContext.LocalResponses.Find(id);

                if (response != null)
                {
                    // On tente la synchro
                    SyncLocalResponseToDataverse(response);
                }
            }

            // 3. On sauvegarde tout à la fin
            _dbContext.SaveChanges();
        }


        /// <summary>
        /// Enregistre la réponse localement, tente la synchronisation immédiate puis retourne l'ID local (int) ou l'ID Dataverse (GUID) sous forme de chaîne.
        /// </summary>
        public string SubmitResponse(Models.SubmitResponseRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            // Seuls SurveyId et ResponseJson sont obligatoires
            if (req.SurveyId == Guid.Empty) throw new ArgumentException("SurveyId requis");
            if (string.IsNullOrWhiteSpace(req.ResponseJson)) throw new ArgumentException("ResponseJson requis");

            // Construire l'objet local
            var local = new LocalResponse
            {
                Name = "Réponse Portail - " + DateTime.Now.ToString("g"),
                SurveyId = req.SurveyId,
                ParticipantId = req.ParticipantId,
                CampagneId = req.CampagneId,
                ResponseJson = req.ResponseJson,
                SubmittedAt = DateTime.Now,
                IsSynced = false,
                DataverseId = null,
                SyncError = null
            };

            try
            {
                // Sauvegarde locale
                _dbContext.LocalResponses.Add(local);
                _dbContext.SaveChanges(); // obtient l'Id local

                // Tentative de synchronisation immédiate
                SyncLocalResponseToDataverse(local);
                SyncPendingResponses();

                // Retourner l'ID Dataverse si synchronisé, sinon l'ID local en string
                if (local.IsSynced && !string.IsNullOrWhiteSpace(local.DataverseId))
                {
                    return local.DataverseId!;
                }

                return local.Id.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] SubmitResponse failed (local persist): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tente de récupérer l'ID du questionnaire (Survey) associé à un participant Dataverse
        /// en suivant participant -> campagne -> questionnaire.
        /// Retourne null si non trouvé.
        /// </summary>
        public Guid? GetSurveyIdForParticipant(Guid participantId)
        {
            if (participantId == Guid.Empty) throw new ArgumentException("participantId invalide", nameof(participantId));

            try
            {
                // 1) Récupérer le participant
                var participant = _client.Retrieve("xrmbox_participantalacampagne", participantId, new ColumnSet("xrmbox_campagnedesatisfaction"));

                if (participant == null || !participant.Contains("xrmbox_campagnedesatisfaction"))
                {
                    Console.WriteLine("[DEBUG] Le participant n'est pas lié à une campagne.");
                    return null;
                }

                var campRef = participant.GetAttributeValue<EntityReference>("xrmbox_campagnedesatisfaction");
                if (campRef == null) return null;

                // 2) Récupérer la campagne avec le BON NOM DE CHAMP : xrmbox_questionnaire
                var campaign = _client.Retrieve("xrmbox_campagnedesatisfaction", campRef.Id, new ColumnSet("xrmbox_questionnaire"));

                if (campaign == null || !campaign.Contains("xrmbox_questionnaire"))
                {
                    Console.WriteLine("[DEBUG] Le champ xrmbox_questionnaire est vide dans la campagne.");
                    return null;
                }

                var surveyRef = campaign.GetAttributeValue<EntityReference>("xrmbox_questionnaire");
                return surveyRef?.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetSurveyIdForParticipant failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}