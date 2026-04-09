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
        public class SurveyContextInfo
        {
            public Guid? SurveyId { get; set; }
            public Guid? CampagneId { get; set; }
        }

        /// <summary>
        /// Tente de synchroniser une LocalResponse vers Dataverse.
        /// Met à jour les champs IsSynced, DataverseId et SyncError en base locale.
        /// </summary>
        public void SyncLocalResponseToDataverse(LocalResponse local)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));

            // Nom logique de la table de destination
            const string entityName = "xrmbox_reponsedesatisfaction";
            var dtEntity = new Entity(entityName);

            try
            {
                dtEntity["xrmbox_name"] = local.Name;

                // 1. Lien vers le Questionnaire
                dtEntity["xrmbox_questionnairedesatisfaction"] = new EntityReference("xrmbox_questionnairedesatisfaction", local.SurveyId);

                // 2. Lien vers le Participant (FIX APPLIQUÉ ICI)
                if (local.ParticipantId.HasValue && local.ParticipantId.Value != Guid.Empty)
                {
                    // On utilise le nom logique du champ : xrmbox_participant
                    // Mais la table cible reste : xrmbox_participantalacampagne
                    dtEntity["xrmbox_participant"] = new EntityReference("xrmbox_participantalacampagne", local.ParticipantId.Value);
                }

                // 3. Lien vers la Campagne
                if (local.CampagneId.HasValue && local.CampagneId.Value != Guid.Empty)
                {
                    dtEntity["xrmbox_campagnedesatisfaction"] = new EntityReference("xrmbox_campagnedesatisfaction", local.CampagneId.Value);
                }

                // 4. Données JSON du sondage (SurveyJS)
                dtEntity["cr7a2_reponsesjson"] = local.ResponseJson;

                // Envoi vers Dataverse
                var createdId = _client.Create(dtEntity);

                // Mise à jour SQL locale si succès
                local.IsSynced = true;
                local.DataverseId = createdId.ToString();
                local.SyncError = null;

                _dbContext.LocalResponses.Update(local);
                _dbContext.SaveChanges();

                Console.WriteLine($"[Sync Success] Response {local.Id} synced to Dataverse ID: {createdId}");
            }
            catch (Exception ex)
            {
                // Enregistrement de l'erreur dans SQL pour debug
                local.IsSynced = false;
                local.SyncError = ex.Message;

                _dbContext.LocalResponses.Update(local);
                _dbContext.SaveChanges();

                Console.WriteLine($"[Sync Error] {ex.Message}");
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
        public string SubmitResponse(Xrmbox.VoC.Portal.Models.SubmitResponseRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            if (req.SurveyId == Guid.Empty) throw new ArgumentException("SurveyId requis");
            if (string.IsNullOrWhiteSpace(req.ResponseJson)) throw new ArgumentException("ResponseJson requis");

            // Gestion propre de l'ID de campagne
            Guid? campagneGuid = null;
            if (req.CampagneId.HasValue && req.CampagneId.Value != Guid.Empty)
            {
                campagneGuid = req.CampagneId.Value;
            }

            // 1. Construction de l'objet local
            // Note : On force IsCompleted = true car cette méthode est appelée lors du onComplete
            var local = new LocalResponse
            {
                Name = "Réponse Portail - " + DateTime.Now.ToString("g"),
                SurveyId = req.SurveyId,
                ParticipantId = req.ParticipantId,
                CampagneId = campagneGuid,
                ResponseJson = req.ResponseJson,
                SubmittedAt = DateTime.Now,
                IsSynced = false,
                DataverseId = null,
                SyncError = null,
                Token = req.Token ?? Guid.Empty,
                IsCompleted = true // CRUCIAL : On marque que c'est une réponse terminée
            };

            try
            {
                // 2. Sauvegarde en base de données locale (SQL)
                _dbContext.LocalResponses.Add(local);
                _dbContext.SaveChanges(); // On génère l'ID local ici

                // 3. Tentative de synchronisation vers Dataverse
                // On n'appelle QUE cette fonction pour cet enregistrement précis.
                SyncLocalResponseToDataverse(local);

                // --- CORRECTION : SUPPRESSION DE SyncPendingResponses() ICI ---
                // Appeler SyncPendingResponses() ici créait le doublon car il 
                // scannait la base avant que le premier cycle ne soit fini.

                // 4. Retourner l'ID de suivi (Dataverse si possible, sinon Local)
                if (local.IsSynced && !string.IsNullOrWhiteSpace(local.DataverseId))
                {
                    return local.DataverseId;
                }

                return local.Id.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] SubmitResponse failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tente de récupérer l'ID du questionnaire (Survey) associé à un participant Dataverse
        /// en suivant participant -> campagne -> questionnaire.
        /// Retourne null si non trouvé.
        /// </summary>
        public dynamic GetSurveyContextInfo(Guid participantId)
        {
            if (participantId == Guid.Empty) return null;

            try
            {
                // 1) Récupérer le participant et son lien vers la campagne
                var participant = _client.Retrieve("xrmbox_participantalacampagne", participantId,
                    new ColumnSet("xrmbox_campagnedesatisfaction"));

                var campRef = participant.GetAttributeValue<EntityReference>("xrmbox_campagnedesatisfaction");

                if (campRef == null)
                {
                    Console.WriteLine("[DEBUG] Le participant n'est pas lié à une campagne.");
                    return null;
                }

                // 2) Récupérer la campagne pour avoir l'ID du questionnaire (xrmbox_questionnaire)
                var campaign = _client.Retrieve("xrmbox_campagnedesatisfaction", campRef.Id,
                    new ColumnSet("xrmbox_questionnaire"));

                var surveyRef = campaign.GetAttributeValue<EntityReference>("xrmbox_questionnaire");

                // On retourne un objet contenant les deux IDs
                return new
                {
                    SurveyId = surveyRef?.Id,
                    CampagneId = campRef.Id
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] GetSurveyContextInfo failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}