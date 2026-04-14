using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.Models.Local;
using Xrmbox.VoC.Portal.Services;
using Microsoft.AspNetCore.WebUtilities; // Pour WebEncoders
using System.Text; // Pour Encoding

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
            _dataverse_service_check: _ = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
            _dataverseService = dataverseService;
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
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
                    EmailConfirmed = true
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

                // Générer un token de reset et construire le lien vers la page Razor Identity ResetPassword
                // 1. Générer le token brut
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

                // 2. Encoder le token en Base64Url (C'est la méthode recommandée par Microsoft pour Identity)
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetToken));

                // 3. Construire le lien vers la page Identity
                var resetLink = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code = encodedToken, email = user.Email },
                    protocol: Request.Scheme);

                // 4. Envoyer l'email (Le reste de votre code ne change pas)
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