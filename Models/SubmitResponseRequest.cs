using System;

namespace Xrmbox.VoC.Portal.Models
{
    public class SubmitResponseRequest
    {
        public Guid SurveyId { get; set; }
        public Guid? ParticipantId { get; set; } // Le ? est important ici
        public Guid? CampagneId { get; set; }    // Et ici
        public string ResponseJson { get; set; }

        // Ajouté pour marquer l'invitation comme utilisée lorsque la réponse est soumise via un token
        public Guid? Token { get; set; }
    }
}