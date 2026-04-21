using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models.Local;
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
        // Ajoutez cet attribut en haut de la classe ou juste sur l'action


        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Fill(Guid token)
        {
            if (token == Guid.Empty) return View("SurveyError");

            // 1. Trouver l'invitation
            var invitation = await _dbContext.SurveyInvitations
                .SingleOrDefaultAsync(i => i.Token == token);

            if (invitation == null || invitation.IsUsed || invitation.ExpirationDate <= DateTime.Now)
            {
                return View("SurveyError");
            }

            // --- NOUVELLE LOGIQUE : Récupérer le brouillon s'il existe ---
            var existingResponse = await _dbContext.LocalResponses
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Token == token);

            // On stocke le JSON dans ViewBag pour le JavaScript
            // Si pas de réponse, on envoie "null" ou un objet vide "{}"
            ViewBag.SavedData = existingResponse?.ResponseJson ?? "null";
            // -----------------------------------------------------------

            // 2. Récupérer les infos Dataverse
            var surveyContext = _dataverseService.GetSurveyContextInfo(invitation.ParticipantDataverseId);

            if (surveyContext != null)
            {
                Guid campagneId = (Guid)surveyContext.CampagneId;
                if (campagneId != Guid.Empty)
                {
                    var campagne = await _dbContext.Campaigns
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.DataverseId == campagneId);

                    ViewBag.PageDesignHtml = campagne?.PageDesignHtml;
                    ViewBag.CampagneId = campagneId;
                }

                ViewBag.SurveyId = surveyContext.SurveyId.ToString() ?? string.Empty;
            }

            ViewBag.ParticipantId = invitation.ParticipantDataverseId;
            ViewBag.Token = invitation.Token;

            return View("Index");
        }

        // POST: /Survey/SavePartial
        [HttpPost]
        public async Task<IActionResult> SavePartial([FromForm] Guid token, [FromForm] string jsonData)
        {
            if (token == Guid.Empty) return BadRequest(new { error = "Token invalide." });

            try
            {
                var invitation = await _dbContext.SurveyInvitations
                    .SingleOrDefaultAsync(i => i.Token == token);

                if (invitation == null) return NotFound(new { error = "Invitation introuvable." });

                // Mettre à jour la date de dernier brouillon
                invitation.LastPartialSave = DateTime.Now;

                // Chercher une LocalResponse existante liée au token
                var existing = await _dbContext.LocalResponses
                    .SingleOrDefaultAsync(r => r.Token == token);

                if (existing != null)
                {
                    // Mise à jour du JSON et marque comme brouillon (non complété)
                    existing.ResponseJson = jsonData;
                    existing.IsCompleted = false;
                    existing.SubmittedAt = DateTime.Now;
                    _dbContext.LocalResponses.Update(existing);
                }
                else
                {
                    // Tenter de récupérer l'ID du questionnaire et de la campagne via Dataverse
                    Guid surveyId = Guid.Empty;
                    Guid? campagneId = null;
                    try
                    {
                        var ctx = _dataverseService.GetSurveyContextInfo(invitation.ParticipantDataverseId);
                        if (ctx != null)
                        {
                            if (ctx.SurveyId != null)
                            {
                                try { surveyId = (Guid)ctx.SurveyId; } catch { Guid.TryParse(ctx.SurveyId?.ToString(), out surveyId); }
                            }
                            if (ctx.CampagneId != null)
                            {
                                try { campagneId = (Guid)ctx.CampagneId; } catch { Guid tmp; if (Guid.TryParse(ctx.CampagneId?.ToString(), out tmp)) campagneId = tmp; }
                            }
                        }
                    }
                    catch
                    {
                        // Ignorer les erreurs Dataverse, on créera quand même la réponse locale
                    }

                    var local = new LocalResponse
                    {
                        Name = "Réponse Portail - " + DateTime.Now.ToString("g"),
                        SurveyId = surveyId,
                        ParticipantId = invitation.ParticipantDataverseId,
                        CampagneId = campagneId,
                        ResponseJson = jsonData,
                        SubmittedAt = DateTime.Now,
                        IsSynced = false,
                        DataverseId = null,
                        SyncError = null,
                        Token = token,
                        IsCompleted = false
                    };

                    _dbContext.LocalResponses.Add(local);
                }

                _dbContext.SurveyInvitations.Update(invitation);
                await _dbContext.SaveChangesAsync();

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SavePartial] Erreur: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}