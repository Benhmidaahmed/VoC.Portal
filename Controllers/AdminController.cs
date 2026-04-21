using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities; // Pour WebEncoders
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Filters; // Ajoutez cette ligne
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text; // Pour Encoding
using System.Threading.Tasks;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.Models.Local;
using Xrmbox.VoC.Portal.Services;

namespace Xrmbox.VoC.Portal.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly IEmailService _emailService;
        private readonly DataverseService _dataverseService;
        private readonly AppDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;

        public AdminController(
            IEmailService emailService,
            DataverseService dataverseService,
            AppDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender)
        {
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _ = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
            _dataverseService = dataverseService;
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        }
        // Ajoutez cette méthode ou utilisez un ActionFilter
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = _userManager.GetUserAsync(User).Result;

            // Si c'est un Admin/SuperAdmin et que le 2FA n'est pas activé en base
            if (user != null && !user.TwoFactorEnabled)
            {
                context.Result = RedirectToAction("Setup", "Mfa");
            }

            base.OnActionExecuting(context);
        }
        // GET: /Admin/Index
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Index()
        {
            // 1. Statistiques pour les compteurs
            var totalResponses = await _dbContext.LocalResponses.CountAsync();
            var syncSuccess = await _dbContext.LocalResponses.CountAsync(r => r.IsSynced);
            // On compte les erreurs dans LocalResponses
            var syncErrorsResponse = await _dbContext.LocalResponses.CountAsync(r => !r.IsSynced && r.SyncError != null);
            // Total des erreurs (Logs système + Réponses échouées)
            var syncErrors = await _dbContext.IntegrationLogs.CountAsync(l => l.Status == "Error") + syncErrorsResponse;

            ViewBag.TotalResponses = totalResponses;
            ViewBag.SyncSuccess = syncSuccess;
            ViewBag.SyncErrors = syncErrors;
            ViewBag.ServiceStatus = "Online";

            // 2. Récupération des logs système
            var systemLogs = await _dbContext.IntegrationLogs
                .AsNoTracking()
                .OrderByDescending(l => l.EventDate)
                .Take(50)
                .ToListAsync();

            // 3. Transformation des LocalResponses en "Logs virtuels" pour l'affichage
            var responseLogs = await _dbContext.LocalResponses
                .AsNoTracking()
                .Where(r => r.IsSynced || r.SyncError != null) // On prend les succès et les erreurs réelles
                .OrderByDescending(r => r.SubmittedAt)
                .Take(50)
                .Select(r => new IntegrationLog
                {
                    EventDate = r.SubmittedAt,
                    EntityName = "Response", // IMPORTANT: Pour ton filtre resolveType dans la vue
                    Action = "Synchronisation Dataverse",
                    Status = r.IsSynced ? "Success" : "Error",
                    Message = r.IsSynced ? "Données synchronisées avec succès" : r.SyncError
                })
                .ToListAsync();

            // 4. Fusion des deux listes et tri chronologique global
            var combinedLogs = systemLogs.Concat(responseLogs)
                .OrderByDescending(l => l.EventDate)
                .ToList();

            return View(combinedLogs);
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> CreateAdmin()
        {
            // Fournir la liste des rôles pour le formulaire
            var roles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
            ViewBag.Roles = roles;
            return View();
        }

        // Remplacement de la méthode CreateAdmin (POST) avec TwoFactorEnabled = false et token encodé via WebEncoders
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> CreateAdmin(string email, string roleName)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(roleName))
            {
                TempData["ErrorMessage"] = "Email et rôle requis.";
                return RedirectToAction("CreateAdmin");
            }

            try
            {
                // S'assurer que le rôle existe (crée si manquant)
                if (!await _roleManager.RoleExistsAsync(roleName))
                {
                    var createRoleResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
                    if (!createRoleResult.Succeeded)
                    {
                        var errors = string.Join("; ", createRoleResult.Errors.Select(e => e.Description));
                        await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                        {
                            EventDate = DateTime.UtcNow,
                            EntityName = "Admin.CreateAdmin",
                            Action = "CreateRole",
                            Status = "Error",
                            Message = $"Impossible de créer le rôle {roleName}: {errors}"
                        });
                        await _dbContext.SaveChangesAsync();

                        TempData["ErrorMessage"] = "Échec création rôle.";
                        return RedirectToAction("CreateAdmin");
                    }
                }

                // Création de l'utilisateur avec mot de passe temporaire aléatoire
                var tempPassword = "Admin!" + Guid.NewGuid().ToString().Substring(0, 8);
                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    TwoFactorEnabled = false // Permettre la première connexion sans QR Code configuré
                };

                var createResult = await _userManager.CreateAsync(user, tempPassword);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                    {
                        EventDate = DateTime.UtcNow,
                        EntityName = "Admin.CreateAdmin",
                        Action = "CreateUser",
                        Status = "Error",
                        Message = $"Erreur création utilisateur {email}: {errors}"
                    });
                    await _dbContext.SaveChangesAsync();

                    TempData["ErrorMessage"] = "Échec création utilisateur.";
                    return RedirectToAction("CreateAdmin");
                }

                // Ajout au rôle
                var addToRoleResult = await _userManager.AddToRoleAsync(user, roleName);
                if (!addToRoleResult.Succeeded)
                {
                    var errors = string.Join("; ", addToRoleResult.Errors.Select(e => e.Description));
                    await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                    {
                        EventDate = DateTime.UtcNow,
                        EntityName = "Admin.CreateAdmin",
                        Action = "AddToRole",
                        Status = "Error",
                        Message = $"Erreur ajout au rôle {roleName} pour {email}: {errors}"
                    });
                    await _dbContext.SaveChangesAsync();

                    TempData["ErrorMessage"] = "Échec affectation rôle.";
                    return RedirectToAction("CreateAdmin");
                }

                // Générer un token de reset et construire le lien vers la page Identity ResetPassword
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetToken));
                var resetLink = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code = encodedToken, email = user.Email },
                    protocol: Request.Scheme);

                var subject = "Invitation administrateur - Xrmbox";
                var htmlMessage = $"Bonjour,<br/><br>Un compte administrateur a été créé pour vous.<br/>" +
                                  $"Veuillez définir votre mot de passe en cliquant sur le lien suivant : <a href=\"{resetLink}\">Définir le mot de passe</a><br/><br>Merci.";

                await _emailSender.SendEmailAsync(email, subject, htmlMessage);

                // Log succès
                await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = "Admin.CreateAdmin",
                    Action = "Create",
                    Status = "Success",
                    Message = $"Utilisateur {email} créé et ajouté au rôle {roleName}"
                });
                await _dbContext.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Utilisateur {email} créé avec succès et invité par email.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = "Admin.CreateAdmin",
                    Action = "Process",
                    Status = "Error",
                    Message = ex.Message
                });
                await _dbContext.SaveChangesAsync();

                TempData["ErrorMessage"] = "Une erreur est survenue lors de la création de l'administrateur.";
                return RedirectToAction("CreateAdmin");
            }
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
        // 1. Affiche l'éditeur Unlayer
        [HttpGet]
        public IActionResult DesignCampaign(Guid id)
        {
            var campaign = _dbContext.Campaigns.FirstOrDefault(c => c.DataverseId == id);
            if (campaign == null) return NotFound();

            return View(campaign);
        }

        // 2. Reçoit le HTML d'Unlayer et sauvegarde
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCampaignDesign([FromBody] CampaignDesignDto model)
        {
            if (model == null || model.Id == Guid.Empty) return BadRequest();

            var campaign = await _dbContext.Campaigns
                .FirstOrDefaultAsync(c => c.DataverseId == model.Id);

            if (campaign == null) return NotFound();

            // 1. ✅ Sauvegarde locale : on garde l'HTML ET le JSON (si tu as ajouté la colonne)
            campaign.PageDesignHtml = model.DesignHtml;

            // Si tu as ajouté la colonne PageDesignJson dans ta table, décommente cette ligne :
            // campaign.PageDesignJson = model.DesignJson; 

            await _dbContext.SaveChangesAsync();

            // 2. ✅ Synchronisation vers Dataverse : On envoie l'HTML (model.DesignHtml)
            try
            {
                // C'est ici le changement principal : on passe DesignHtml au lieu de DesignJson
                await _dataverseService.UpdateCampaignDesignAsync(model.Id, model.DesignHtml);
            }
            catch (Exception ex)
            {
                // Local sauvegardé, mais Dataverse a échoué
                return Ok(new { warning = $"Enregistré localement, Dataverse non synchronisé : {ex.Message}" });
            }

            return Ok(new { message = "Design HTML enregistré et synchronisé vers Dataverse avec succès." });
        }

        public class CampaignDesignDto
        {
            public Guid Id { get; set; }
            public string DesignHtml { get; set; }
            public string DesignJson { get; set; }
        }
        // Classe d'aide pour la requête AJAX
        public class DesignSaveRequest
        {
            public Guid CampaignId { get; set; }
            public string Html { get; set; }
        }
        public IActionResult ListCampaignsToDesign()
        {
            // On récupère la liste des campagnes stockées localement
            var campaigns = _dbContext.Campaigns.ToList();
            return View(campaigns);
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