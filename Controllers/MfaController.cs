using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using Xrmbox.VoC.Portal.Models;
using Xrmbox.VoC.Portal.ViewModels;

namespace Xrmbox.VoC.Portal.Controllers
{
    public class MfaController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public MfaController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }
        [HttpPost]
        
        public async Task<IActionResult> Verify(string code)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var isTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

            if (isTokenValid)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, true);

                // CORRECTION : Redirection vers la page Admin après succès
                return RedirectToAction("Index", "Admin");
            }

            ModelState.AddModelError(string.Empty, "Code d'authentification invalide.");
            return View("Setup");
        }

        [HttpGet]
        public async Task<IActionResult> Setup()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(key))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                key = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            var email = user.Email ?? string.Empty;
            var otpAuthUrl = $"otpauth://totp/XrmboxVoC:{Uri.EscapeDataString(email)}?secret={Uri.EscapeDataString(key ?? string.Empty)}&issuer=Xrmbox";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20);
            var qrCodeBase64 = Convert.ToBase64String(qrCodeBytes);

            var viewModel = new MfaSetupViewModel
            {
                QrCodeBase64 = qrCodeBase64
            };

            return View(viewModel);
        }
    }
}