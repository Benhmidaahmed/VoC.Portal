using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.Models.Local;

namespace Xrmbox.VoC.Portal.Areas.Identity.Pages.Account.Manage
{
    public class EnableAuthenticatorModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<EnableAuthenticatorModel> _logger;

        public EnableAuthenticatorModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            AppDbContext dbContext,
            ILogger<EnableAuthenticatorModel> logger)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string SharedKey { get; private set; } = string.Empty;

        public string AuthenticatorUri { get; private set; } = string.Empty;

        public string? StatusMessage { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(7, MinimumLength = 6)]
            [Display(Name = "Code de validation (6 chiffres)")]
            public string Code { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Utilisateur introuvable.");

            // Ensure user has an authenticator key
            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(unformattedKey))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            // Expose shared key in a user-friendly format
            SharedKey = FormatKey(unformattedKey);

            // Build otpauth:// URI for QR code generation by client or server
            var email = user.Email ?? user.UserName ?? "user";
            AuthenticatorUri = GenerateQrCodeUri("Xrmbox", $"XrmboxVoC:{email}", unformattedKey);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                await PopulateKeyAndUriAsync();
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Utilisateur introuvable.");

            // Normalize the code (remove spaces/hyphens)
            var verificationCode = Input.Code?.Replace(" ", string.Empty).Replace("-", string.Empty) ?? string.Empty;

            // Verify the token using the authenticator provider
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                verificationCode);

            if (!isValid)
            {
                ModelState.AddModelError(string.Empty, "Code invalide. Vérifiez le code de votre application d'authentification.");
                await PopulateKeyAndUriAsync();
                return Page();
            }

            // Activate 2FA permanently
            var enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enableResult.Succeeded)
            {
                // Log into IntegrationLogs for traceability
                await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
                {
                    EventDate = DateTime.UtcNow,
                    EntityName = "EnableAuthenticator",
                    Action = "Enable2FA",
                    Status = "Error",
                    Message = $"Échec activation 2FA pour {user.Email}: {string.Join("; ", enableResult.Errors.Select(e => e.Description))}"
                });
                await _dbContext.SaveChangesAsync();

                ModelState.AddModelError(string.Empty, "Impossible d'activer l'authentification à deux facteurs.");
                await PopulateKeyAndUriAsync();
                return Page();
            }

            // Refresh sign-in so the new security stamp / 2FA state is applied
            await _signInManager.RefreshSignInAsync(user);

            // Log success
            await _dbContext.IntegrationLogs.AddAsync(new IntegrationLog
            {
                EventDate = DateTime.UtcNow,
                EntityName = "EnableAuthenticator",
                Action = "Enable2FA",
                Status = "Success",
                Message = $"2FA activé pour {user.Email}"
            });
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Utilisateur {UserId} a activé l'authentificateur.", user.Id);

            // Optionally you can generate recovery codes and expose them to the user.
            // Redirect to manage index or requested returnUrl
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            TempData["SuccessMessage"] = "Authentification à deux facteurs activée.";
            return RedirectToPage("/Account/Manage/Index", new { area = "Identity" });
        }

        private static string FormatKey(string unformattedKey)
        {
            if (string.IsNullOrEmpty(unformattedKey)) return string.Empty;

            var result = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition));
            }
            return result.ToString().ToLowerInvariant();
        }

        private static string GenerateQrCodeUri(string issuer, string accountTitle, string unformattedKey)
        {
            // Ensure values are URL-encoded
            var label = Uri.EscapeDataString(accountTitle);
            var issuerEncoded = Uri.EscapeDataString(issuer);
            var secret = Uri.EscapeDataString(unformattedKey);

            // Standard Key Uri format for Google Authenticator
            return $"otpauth://totp/{label}?secret={secret}&issuer={issuerEncoded}&digits=6";
        }

        private async Task PopulateKeyAndUriAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(unformattedKey))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            SharedKey = FormatKey(unformattedKey);
            var email = user.Email ?? user.UserName ?? "user";
            AuthenticatorUri = GenerateQrCodeUri("Xrmbox", $"XrmboxVoC:{email}", unformattedKey);
        }
    }
}
