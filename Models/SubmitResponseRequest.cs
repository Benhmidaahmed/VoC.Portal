using System;

namespace Xrmbox.VoC.Portal.Models
{
    public class SubmitResponseRequest
    {
        public Guid SurveyId { get; set; }
        public Guid? ParticipantId { get; set; } // Le ? est important ici
        public Guid? CampagneId { get; set; }    // Et ici
        public string ResponseJson { get; set; }
    }
}