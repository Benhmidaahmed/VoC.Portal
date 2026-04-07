using System;
using System.ComponentModel.DataAnnotations;

namespace Xrmbox.VoC.Portal.Models.Local
{
    public class IntegrationLog
    {
        [Key]
        public int Id { get; set; }

        public DateTime EventDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(200)]
        public string EntityName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Message { get; set; }
    }
}