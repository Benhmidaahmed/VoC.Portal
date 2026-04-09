using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Services;
using Xrmbox.VoC.Portal.Models.Local;
using Xrmbox.VoC.Portal.Data;
using System.Collections.Generic;

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

        // GET: /Admin/Index
        public async Task<IActionResult> Index()
        {
            // 1. Statistiques pour les compteurs (CardBox)
            var totalResponses = await _dbContext.LocalResponses.CountAsync();
            var syncSuccess = await _dbContext.LocalResponses.CountAsync(r => r.IsSynced);
            var syncErrors = await _dbContext.IntegrationLogs.CountAsync(l => l.Status == "Error");

            ViewBag.TotalResponses = totalResponses;
            ViewBag.SyncSuccess = syncSuccess;
            ViewBag.SyncErrors = syncErrors;
            ViewBag.ServiceStatus = "Online";

            // 2. Récupération des logs pour le flux d'activité et l'historique
            // On prend les 50 derniers pour avoir assez de données pour les deux colonnes (Erreurs et Succès)
            var allRecentLogs = await _dbContext.IntegrationLogs
                .AsNoTracking()
                .OrderByDescending(l => l.EventDate)
                .Take(50)
                .ToListAsync();

            // 3. On envoie la liste complète au Model de la vue
            // La vue se chargera de filtrer 'successLogs' et 'combinedLogs' à partir de ce Model
            return View(allRecentLogs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendInvitations(Guid campaignId)
        {
            if (campaignId == Guid.Empty)
            {
                ModelState.AddModelError(string.Empty, "campaignId invalide.");
                return RedirectToAction("Index");
            }

            try
            {
                var participants = _dataverseService.GetParticipantsByCampaign(campaignId).ToList();

                if (!participants.Any())
                {
                    TempData["SuccessMessage"] = "Aucun participant trouvé pour cette campagne.";
                    return RedirectToAction("Index");
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
                    var body = $"Please complete our survey by clicking here: https://localhost:7265/Survey/Fill?token={token}";

                    try
                    {
                        await _emailService.SendEmailAsync(email, subject, body);
                        sentCount++;

                        // Log de succès optionnel
                        await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                        {
                            EventDate = DateTime.UtcNow,
                            EntityName = "Invitation",
                            Action = "EmailSent",
                            Status = "Success",
                            Message = $"Email envoyé avec succès à {email}"
                        });
                    }
                    catch (Exception emailEx)
                    {
                        await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                        {
                            EventDate = DateTime.UtcNow,
                            EntityName = "Invitation",
                            Action = "SendEmail",
                            Status = "Error",
                            Message = $"Erreur envoi email à {email}: {emailEx.Message}"
                        });
                    }
                    await _dbContext.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"Invitations traitées : {sentCount} emails envoyés.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = "Admin.SendInvitations",
                    Action = "Process",
                    Status = "Error",
                    Message = ex.Message
                });
                await _dbContext.SaveChangesAsync();

                TempData["ErrorMessage"] = "Une erreur est survenue lors de l'envoi des invitations.";
                return RedirectToAction("Index");
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

                var surveyId = _dataverseService.GetSurveyContextInfo(invitation.ParticipantDataverseId);

                ViewBag.ParticipantDataverseId = invitation.ParticipantDataverseId;
                ViewBag.Token = invitation.Token;
                ViewBag.SurveyId = surveyId?.ToString() ?? string.Empty;

                return View("~/Views/Survey/Index.cshtml");
            }
            catch (Exception ex)
            {
                await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = "Admin.Fill",
                    Action = "Open",
                    Status = "Error",
                    Message = ex.Message
                });
                await _dbContext.SaveChangesAsync();

                TempData["ErrorMessage"] = "Une erreur est survenue lors du traitement du lien.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}