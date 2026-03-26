using System;

namespace Xrmbox.VoC.Portal.Models
{
    public class SubmitResponseRequest
    {
        public Guid SurveyId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid CampagneId { get; set; }

        // JSON string contenant les réponses (serialisé côté client)
        public string ResponseJson { get; set; } = string.Empty;
    }
}