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

namespace Xrmbox.VoC.Portal.Services
{
    public class DataverseOptions
    {
        public string? ConnectionString { get; set; }
    }

    public class DataverseService : IDisposable
    {
        private readonly ServiceClient _client;

        public DataverseService(IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
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
        /// Enregistre la réponse. 
        /// Modifié pour accepter les soumissions sans ParticipantId ou CampagneId.
        /// </summary>
        public Guid SubmitResponse(Models.SubmitResponseRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            // Seuls SurveyId et ResponseJson sont obligatoires
            if (req.SurveyId == Guid.Empty) throw new ArgumentException("SurveyId requis");
            if (string.IsNullOrWhiteSpace(req.ResponseJson)) throw new ArgumentException("ResponseJson requis");

            const string entityName = "xrmbox_reponsedesatisfaction";
            var entity = new Entity(entityName);

            // On stocke le JSON dans le nom ou un champ dédié
            entity["xrmbox_name"] = "Réponse Portail - " + DateTime.Now.ToString("g");

            // Lien vers le questionnaire (Obligatoire)
            entity["xrmbox_questionnairedesatisfaction"] = new EntityReference("xrmbox_questionnairedesatisfaction", req.SurveyId);

            // --- GESTION DES IDS OPTIONNELS (POUR LES NON-CONNECTÉS / ANONYMES) ---

            // Si CampagneId est fourni dans l'URL et valide
            if (req.CampagneId.HasValue && req.CampagneId.Value != Guid.Empty)
            {
                entity["xrmbox_campagnedesatisfaction"] = new EntityReference("xrmbox_campagnedesatisfaction", req.CampagneId.Value);
            }

            // Si ParticipantId est fourni dans l'URL et valide
            if (req.ParticipantId.HasValue && req.ParticipantId.Value != Guid.Empty)
            {
                // Vérifier si la table cible est 'xrmbox_participant' ou 'contact' selon ton environnement
                entity["xrmbox_participant"] = new EntityReference("xrmbox_participantalacampagne", req.ParticipantId.Value);
            }

            // Optionnel : stocker le JSON dans un champ spécifique si tu en as un
            // entity["xrmbox_rawjson"] = req.ResponseJson;

            try
            {
                var createdId = _client.Create(entity);
                return createdId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dataverse Error] SubmitResponse failed: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}