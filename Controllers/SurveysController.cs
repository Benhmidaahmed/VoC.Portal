using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Services;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models.Local;

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
        [HttpGet]
        public async Task<IActionResult> Fill(Guid token)
        {
            if (token == Guid.Empty) return RedirectToAction("Index", "Home");

            try
            {
                var invitation = await _dbContext.SurveyInvitations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(i => i.Token == token);

                if (invitation == null || invitation.IsUsed || invitation.ExpirationDate <= DateTime.Now)
                {
                    return RedirectToAction("Index", "Home");
                }

                var context = _dataverseService.GetSurveyContextInfo(invitation.ParticipantDataverseId);

                // --- AJOUT CRUCIAL ICI ---
                // On va chercher si une réponse partielle existe déjà pour ce token
                var existingResponse = await _dbContext.LocalResponses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Token == token);

                // On passe le JSON à la vue (si null, on passe "null" en string pour le JS)
                ViewBag.SavedData = existingResponse?.ResponseJson;
                // -------------------------

                ViewBag.ParticipantId = invitation.ParticipantDataverseId;
                ViewBag.Token = invitation.Token;
                ViewBag.SurveyId = context.SurveyId?.ToString() ?? string.Empty;
                ViewBag.CampagneId = context.CampagneId?.ToString() ?? string.Empty;

                return View("Index");
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", "Home");
            }
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