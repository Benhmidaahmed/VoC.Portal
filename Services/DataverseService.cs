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
        /// entityName = "xrmbox_questionnairedesatisfaction"
        /// attribute = "xrmbox_Json" (exact case depuis Power Apps)
        /// </summary>
        public string? GetSurvey(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("id invalide", nameof(id));

            const string entityName = "xrmbox_questionnairedesatisfaction";
            const string targetAttribute = "xrmbox_json"; // EXACT case from Power Apps

            var columns = new ColumnSet(targetAttribute);

            try
            {
                var entity = _client.Retrieve(entityName, id, columns);
                if (entity == null) return null;

                // Recherche par nom exact (case?sensitive)
                if (entity.Attributes.TryGetValue(targetAttribute, out var value) && value != null)
                {
                    return value.ToString();
                }

                // Si non trouvé : log des attributs retournés pour debug
                Console.WriteLine($"[Dataverse] Attribut '{targetAttribute}' non trouvé pour '{entityName}' (id={id}). Attributs retournés :");
                foreach (var kv in entity.Attributes)
                {
                    Console.WriteLine($"  - {kv.Key} ({kv.Value?.GetType().Name ?? "null"})");
                }

                // Tentative de récupération insensible à la casse (diagnostic)
                var match = entity.Attributes.FirstOrDefault(a => string.Equals(a.Key, targetAttribute, StringComparison.OrdinalIgnoreCase));
                if (!match.Equals(default(KeyValuePair<string, object>)) && match.Value != null)
                {
                    Console.WriteLine($"[Dataverse] Attribut trouvé en ignore-case : '{match.Key}'");
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
        /// Utilisez ceci pour vérifier la casse exacte et l'existence du champ.
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
        /// Info de connexion / debug : userId + org name
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

        public Guid SubmitResponse(Models.SubmitResponseRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (req.SurveyId == Guid.Empty) throw new ArgumentException("SurveyId requis", nameof(req.SurveyId));
            if (req.ParticipantId == Guid.Empty) throw new ArgumentException("ParticipantId requis", nameof(req.ParticipantId));
            if (req.CampagneId == Guid.Empty) throw new ArgumentException("CampagneId requis", nameof(req.CampagneId));
            if (string.IsNullOrWhiteSpace(req.ResponseJson)) throw new ArgumentException("ResponseJson requis", nameof(req.ResponseJson));

            const string entityName = "xrmbox_reponsedesatisfaction";

            var entity = new Entity(entityName);

            // Stockage du JSON (champ choisi : xrmbox_name)
            entity["xrmbox_name"] = req.ResponseJson;

            // Lookups : attributs avec préfixe xrmbox_
            entity["xrmbox_questionnairedesatisfaction"] = new EntityReference("xrmbox_questionnairedesatisfaction", req.SurveyId);
            entity["xrmbox_campagnedesatisfaction"] = new EntityReference("xrmbox_campagnedesatisfaction", req.CampagneId);
            entity["xrmbox_participant"] = new EntityReference("xrmbox_participant", req.ParticipantId);

            try
            {
                var createdId = _client.Create(entity);
                return createdId;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}