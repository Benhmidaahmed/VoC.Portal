using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Services;

namespace Xrmbox.VoC.Portal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CampaignController : ControllerBase
    {
        private readonly DataverseService _dataverseService;
        private readonly AppDbContext _dbContext;

        public CampaignController(DataverseService dataverseService, AppDbContext dbContext)
        {
            _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var campaigns = _dataverseService.GetAllCampaigns().ToList();
                return Ok(campaigns);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{id:guid}/participants")]
        public IActionResult GetParticipants(Guid id)
        {
            if (id == Guid.Empty)
            {
                return BadRequest(new { error = "Id de campagne invalide." });
            }

            try
            {
                var participants = _dataverseService.GetParticipantsByCampaign(id).ToList();

                if (!participants.Any())
                {
                    return NotFound(new { error = "Aucun participant trouvé pour cette campagne." });
                }

                return Ok(participants);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{id:guid}/design-sync")]
        public async Task<IActionResult> DesignSync(Guid id)
        {
            if (id == Guid.Empty)
            {
                return BadRequest(new { error = "Id de campagne invalide." });
            }

            try
            {
                var existsLocally = await _dbContext.Campaigns.AnyAsync(c => c.DataverseId == id);
                if (!existsLocally)
                {
                    return NotFound(new { error = "Campagne locale introuvable." });
                }

                _dataverseService.DesignFromDataverse(id);
                return Ok(new { message = "Design synchronisé depuis Dataverse." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("{id:guid}/design")]
        public async Task<IActionResult> UpdateDesign(Guid id, [FromBody] CampaignDesignRequest request)
        {
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(request.Html))
            {
                return BadRequest(new { error = "Données invalides : ID ou HTML manquant." });
            }

            try
            {
                // 1. RECHERCHE DE LA CAMPAGNE LOCALE
                var localCampaign = await _dbContext.Campaigns
                    .FirstOrDefaultAsync(c => c.DataverseId == id);

                if (localCampaign == null)
                {
                    return NotFound(new { error = "La campagne n'existe pas dans la base locale." });
                }

                // 2. ENREGISTREMENT DANS LA BASE LOCALE (SQL)
                // On met à jour le champ HTML localement avant toute chose
                localCampaign.PageDesignHtml = request.Html;

                // On sauvegarde immédiatement dans SQL Server
                await _dbContext.SaveChangesAsync();

                // 3. SYNCHRONISATION VERS DATAVERSE
                // Une fois que c'est sécurisé en local, on l'envoie au cloud
                try
                {
                    await _dataverseService.UpdateCampaignDesignAsync(id, request.Html);

                    return Ok(new
                    {
                        message = "Design sauvegardé localement et synchronisé avec succès sur Dataverse."
                    });
                }
                catch (Exception dataverseEx)
                {
                    // Ici, le local est sauvegardé, mais Dataverse a échoué
                    // On informe l'utilisateur que c'est enregistré mais pas encore synchronisé
                    return Ok(new
                    {
                        message = "Design sauvegardé localement, mais la synchronisation Dataverse a échoué.",
                        warning = dataverseEx.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Erreur lors de la sauvegarde : {ex.Message}" });
            }
        }

        public class CampaignDesignRequest
        {
            public string Html { get; set; } = string.Empty;
        }
    }
}