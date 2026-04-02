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

    public class SurveyViewController : Controller

    {

        private readonly AppDbContext _dbContext;

        private readonly DataverseService _dataverseService;



        public SurveyViewController(AppDbContext dbContext, DataverseService dataverseService)

        {

            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));

        }



        // GET /Survey/View?token={token}

        [HttpGet]

        public IActionResult Index()

        {

            return View();

        }



        // Nouvelle action Fill : récupère l'invitation par token, vérifie validité,

        // récupère ParticipantDataverseId et SurveyId (via Dataverse) puis retourne la vue Index du survey.

        [HttpGet]

        public async Task<IActionResult> Fill(Guid token)

        {

            if (token == Guid.Empty)

            {

                TempData["ErrorMessage"] = "Lien invalide.";

                return RedirectToAction("Index", "Home");

            }



            try

            {

                var invitation = await _dbContext.SurveyInvitations

                    .AsNoTracking()

                    .SingleOrDefaultAsync(i => i.Token == token);



                if (invitation == null || invitation.IsUsed || invitation.ExpirationDate <= DateTime.Now)

                {

                    TempData["ErrorMessage"] = "Le lien n'est plus valide ou a expiré.";

                    return RedirectToAction("Index", "Home");

                }



                // Récupérer SurveyId via Dataverse en naviguant participant -> campagne -> questionnaire

                var surveyId = _dataverseService.GetSurveyIdForParticipant(invitation.ParticipantDataverseId);



                ViewBag.ParticipantId = invitation.ParticipantDataverseId;

                ViewBag.Token = invitation.Token;

                ViewBag.SurveyId = surveyId?.ToString() ?? string.Empty;



                // Retourner la vue Survey (index) — forcer le chemin pour pointer vers Views/Survey/Index.cshtml

                return View("~/Views/Survey/Index.cshtml");

            }

            catch (Exception ex)

            {

                Console.WriteLine($"[SurveyView Fill] Erreur: {ex.Message}");

                TempData["ErrorMessage"] = "Une erreur est survenue lors du traitement du lien.";

                return RedirectToAction("Index", "Home");

            }

        }

    }

}