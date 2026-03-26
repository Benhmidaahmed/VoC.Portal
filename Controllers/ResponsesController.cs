using System;
using Microsoft.AspNetCore.Mvc;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.Services;

namespace Xrmbox.VoC.Portal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResponsesController : ControllerBase
    {
        private readonly DataverseService _dataverse;

        public ResponsesController(DataverseService dataverse)
        {
            _dataverse = dataverse; 
        }

        // POST api/responses
        [HttpPost]
        public IActionResult Post([FromBody] SubmitResponseRequest req)
        {
            if (req == null)
                return BadRequest(new { error = "Payload requis." });

            try
            {
                var id = _dataverse.SubmitResponse(req);
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