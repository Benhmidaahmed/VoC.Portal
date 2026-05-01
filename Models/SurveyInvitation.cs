using System;
using System.ComponentModel.DataAnnotations;

namespace Xrmbox.VoC.Portal.Models.Local
{
    public class SurveyInvitation
    {
        [Key]
        public int Id { get; set; }
        public Guid Token { get; set; }
        public Guid ParticipantDataverseId { get; set; }
        public DateTime ExpirationDate { get; set; }
        public bool IsUsed { get; set; }
        public DateTime? LastPartialSave { get; set; }
        public DateTime? LastReminderSent { get; set; }
        public int ReminderCount { get; set; } = 0;
        public Guid CampaignDataverseId { get; set; }
        // Nouveautés demandées
        public bool IsActive { get; set; } = true;
        public string? SyncStatus { get; set; }
        public string? ParticipantEmail { get; set; }
        public string? ParticipantName { get; set; }
    }
}