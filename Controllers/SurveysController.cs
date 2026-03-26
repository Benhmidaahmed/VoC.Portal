using System;

using Microsoft.AspNetCore.Mvc;

using Xrmbox.VoC.Portal.Services;



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

        // GET /SurveyView or /SurveyView/Index?id={id}&participantId={participantId}&campagneId={campagneId}

        [HttpGet]

        public IActionResult Index()

        {

            return View();

        }

    }

}