using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.Services;
using Xrmbox.VoC.Portal.Data;

namespace Xrmbox.VoC.Portal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResponsesController : ControllerBase
    {
        private readonly DataverseService _dataverse;
        private readonly AppDbContext _dbContext;

        public ResponsesController(DataverseService dataverse, AppDbContext dbContext)
        {
            _dataverse = dataverse;
            _dbContext = dbContext;
        }

        // POST api/responses
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] SubmitResponseRequest req)
        {
            if (req == null)
                return BadRequest(new { error = "Payload requis." });

            try
            {
                var id = _dataverse.SubmitResponse(req);

                // Si un token est fourni, marquer l'invitation comme utilisée
                if (req.Token.HasValue && req.Token.Value != Guid.Empty)
                {
                    try
                    {
                        var invitation = await _dbContext.SurveyInvitations
                            .SingleOrDefaultAsync(i => i.Token == req.Token.Value);

                        if (invitation != null && !invitation.IsUsed)
                        {
                            invitation.IsUsed = true;
                            _dbContext.SurveyInvitations.Update(invitation);
                            await _dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception invEx)
                    {
                        // Log et ne pas échouer la création de la réponse si la mise à jour du token échoue
                        Console.WriteLine($"[Responses] Failed to mark invitation used for token {req.Token}: {invEx.Message}");
                    }
                }

                return Ok(new { id });
            }
            catch (ArgumentException aex)
            {
                return BadRequest(new { error = aex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Erreur lors de la création de la réponse dans Dataverse.", detail = ex.Message });
            }
        }
    }
}