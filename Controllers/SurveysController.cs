using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Services;
using Xrmbox.VoC.Portal.Data;



namespace Xrmbox.VoC.Portal.Controllers

{

    [ApiController]

    [Route("api/[controller]")]

    public class SurveysController : ControllerBase

    {

        private readonly DataverseService _dataverse;



        public SurveysController(DataverseService dataverse)

        {

            _dataverse = dataverse;

        }



        // GET api/surveys/{id}

        [HttpGet("{id}")]

        public IActionResult Get(Guid id)

        {

            try

            {

                var json = _dataverse.GetSurvey(id);

                if (string.IsNullOrWhiteSpace(json))

                {

                    return NotFound(new { error = "Questionnaire introuvable ou JSON absent." });

                }



                return Content(json, "application/json");

            }

            catch (Exception ex)

            {

                return StatusCode(500, new { error = "Erreur lors de la récupération du questionnaire.", detail = ex.Message });

            }

        }



        // Méthode temporaire de debug — retirez-la en production

        [HttpGet("metadata/{entityLogicalName}")]

        public IActionResult Metadata(string entityLogicalName)

        {

            try

            {

                var attributes = _dataverse.GetEntityAttributes(entityLogicalName);

                return Ok(new { entity = entityLogicalName, attributes });

            }

            catch (Exception ex)

            {

                return StatusCode(500, new { error = ex.Message });

            }

        }



        [HttpGet("connectioninfo")]

        public IActionResult ConnectionInfo()

        {

            try

            {

                var info = _dataverse.GetConnectionInfo();

                return Ok(info);

            }

            catch (Exception ex)

            {

                return StatusCode(500, new { error = ex.Message });

            }

        }

    }



    // Contrôleur MVC (hérite de Controller) — rend une vue Razor

    public class SurveyController : Controller
    {
        private readonly AppDbContext _dbContext;
        private readonly DataverseService _dataverseService;

        public SurveyController(AppDbContext dbContext, DataverseService dataverseService)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        }

        // Cette action répondra à : /Survey/Fill?token=...
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> Fill(Guid token)
        {
            if (token == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Invalid link.";
                return RedirectToAction("Index", "Home");
            }

            try
            {
                var invitation = await _dbContext.SurveyInvitations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(i => i.Token == token);

                if (invitation == null || invitation.IsUsed || invitation.ExpirationDate <= DateTime.Now)
                {
                    TempData["ErrorMessage"] = "This link is no longer valid or has expired.";
                    return RedirectToAction("Index", "Home");
                }

                // --- MODIFICATION ICI ---
                // On récupère le contexte complet (Survey + Campagne) depuis Dataverse
                var context = _dataverseService.GetSurveyContextInfo(invitation.ParticipantDataverseId);

                if (context == null)
                {
                    TempData["ErrorMessage"] = "Could not retrieve survey details.";
                    return RedirectToAction("Index", "Home");
                }

                ViewBag.ParticipantId = invitation.ParticipantDataverseId;
                ViewBag.Token = invitation.Token;
                ViewBag.SurveyId = context.SurveyId?.ToString() ?? string.Empty;
                ViewBag.CampagneId = context.CampagneId?.ToString() ?? string.Empty; // On passe l'ID de la campagne à la vue

                return View("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Survey Fill] Error: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while processing the link.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}