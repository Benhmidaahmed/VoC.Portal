using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Services;
using Xrmbox.VoC.Portal.Models.Local;
using Xrmbox.VoC.Portal.Data;

namespace Xrmbox.VoC.Portal.Controllers
{
    public class AdminController : Controller
    {
        private readonly IEmailService _emailService;
        private readonly DataverseService _dataverseService;
        private readonly AppDbContext _dbContext;

        public AdminController(IEmailService emailService, DataverseService dataverseService, AppDbContext dbContext)
        {
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendInvitations(Guid campaignId)
        {
            if (campaignId == Guid.Empty)
            {
                ModelState.AddModelError(string.Empty, "campaignId invalide.");
                return View("Index");
            }

            try
            {
                var participants = _dataverseService.GetParticipantsByCampaign(campaignId).ToList();

                if (!participants.Any())
                {
                    ViewData["SuccessMessage"] = "Aucun participant trouvé pour cette campagne.";
                    return View("Index");
                }

                var sentCount = 0;
                foreach (var p in participants)
                {
                    var email = p.Email;
                    if (string.IsNullOrWhiteSpace(email)) continue;

                    var token = Guid.NewGuid();
                    var invitation = new SurveyInvitation
                    {
                        Token = token,
                        ParticipantDataverseId = p.Id,
                        ExpirationDate = DateTime.Now.AddDays(7),
                        IsUsed = false
                    };

                    _dbContext.SurveyInvitations.Add(invitation);
                    await _dbContext.SaveChangesAsync();

                    var subject = "Votre avis nous intéresse";
                    var body = $"Cliquez ici pour répondre au sondage : https://localhost:7265/Admin/Fill?token={token}";

                    try
                    {
                        await _emailService.SendEmailAsync(email, subject, body);
                        sentCount++;
                    }
                    catch (Exception emailEx)
                    {
                        Console.WriteLine($"Erreur envoi email à {email} : {emailEx.Message}");
                    }
                }

                ViewData["SuccessMessage"] = $"Invitations traitées : {sentCount} emails envoyés.";
                return View("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin SendInvitations] Erreur: {ex.Message}");
                ModelState.AddModelError(string.Empty, "Une erreur est survenue lors de l'envoi des invitations.");
                return View("Index");
            }
        }

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

                // 1. Récupérer l'ID du questionnaire via le service Dataverse
                var surveyId = _dataverseService.GetSurveyIdForParticipant(invitation.ParticipantDataverseId);
                // 2. DEBUG : Regarde ces logs dans ta console de sortie Visual Studio
                Console.WriteLine($"--- DEBUG PFE ---");
                Console.WriteLine($"Participant ID: {invitation.ParticipantDataverseId}");
                Console.WriteLine($"Survey ID trouvé: {(surveyId.HasValue ? surveyId.Value.ToString() : "NULL")}");
                Console.WriteLine($"-----------------");
                // 2. Préparer les données pour la Vue
                ViewBag.ParticipantDataverseId = invitation.ParticipantDataverseId;
                ViewBag.Token = invitation.Token;
                ViewBag.SurveyId = surveyId?.ToString() ?? string.Empty;

                // 3. ICI : On force le chemin vers la vue qui contient SurveyJS
                return View("~/Views/Survey/Index.cshtml");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin Fill] Erreur: {ex.Message}");
                TempData["ErrorMessage"] = "Une erreur est survenue lors du traitement du lien.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}
