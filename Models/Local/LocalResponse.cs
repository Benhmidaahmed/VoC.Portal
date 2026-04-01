using System;

namespace Xrmbox.VoC.Portal.Models.Local
{
    public class LocalResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid SurveyId { get; set; }
        public Guid? ParticipantId { get; set; }
        public Guid? CampagneId { get; set; }
        public string ResponseJson { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public bool IsSynced { get; set; } = false;
        public string? DataverseId { get; set; }
        public string? SyncError { get; set; }
    }
}