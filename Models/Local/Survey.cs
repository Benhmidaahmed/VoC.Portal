namespace Xrmbox.VoC.Portal.Models.Local
{
    public class Survey
    {
        public int Id { get; set; }
        public Guid DataverseId { get; set; } // Pour lier avec xrmbox_questionnairedesatisfactionid
        public string? JsonContent { get; set; } // Contiendra le contenu de xrmbox_json
        public bool IsActive { get; set; }
        public DateTime LastSync { get; set; }
    }
}
