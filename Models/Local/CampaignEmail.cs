using System;
using System.ComponentModel.DataAnnotations;

namespace Xrmbox.VoC.Portal.Models.Local
{
    public class CampaignEmail
    {
        [Key]
        public Guid DataverseId { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public int Role { get; set; }

        public int? DelayDays { get; set; }

        public Guid CampaignId { get; set; }
    }
}