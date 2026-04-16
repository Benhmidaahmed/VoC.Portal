using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Xrmbox.VoC.Portal.Models.Local
{
    public class Campaign
    {
        [Key]
        public Guid DataverseId { get; set; }

        public string Name { get; set; } = string.Empty;

        public int StateCode { get; set; }

        public int StatusCode { get; set; }

        // C'est le Guid provenant de Dataverse
        public Guid SurveyDataverseId { get; set; }

        // On définit la relation manuellement pour pointer sur DataverseId du Survey
        [ForeignKey("SurveyDataverseId")]
        public Survey? Survey { get; set; }

        public DateTime LastSync { get; set; }

        // Templates d'email dynamiques
        public string? InvitationSubject { get; set; }

        public string? InvitationBody { get; set; }

        public string? ReminderSubject { get; set; }

        public string? ReminderBody { get; set; }

        // Personnalisation de l'apparence
        public string? CouleurPrimaire { get; set; }

        // Stocke un HTML complet, taille maximale
        [Column(TypeName = "nvarchar(max)")]
        public string? PageDesignHtml { get; set; }
    }
}