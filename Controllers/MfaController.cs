    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using QRCoder;
    using System;
    using System.Threading.Tasks;
    using Xrmbox.VoC.Portal.Models;
    using Xrmbox.VoC.Portal.ViewModels;

    namespace Xrmbox.VoC.Portal.Controllers
    {
        [Authorize]
        public class MfaController : Controller
        {
            private readonly UserManager<ApplicationUser> _userManager;
            private readonly SignInManager<ApplicationUser> _signInManager;

            public MfaController(
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager)
            {
                _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
                _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
            }

        [HttpPost]
        public async Task<IActionResult> Verify(string code, string? returnUrl = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var isTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

            if (isTokenValid)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, true);
                await _signInManager.RefreshSignInAsync(user);

                // Si le returnUrl est "/Admin" (envoyé par le Login), on y va.
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                // Sinon, par sécurité, on force la redirection vers Admin
                return RedirectToAction("Index", "Admin");
            }

            ModelState.AddModelError(string.Empty, "Code invalide.");
            ViewBag.ReturnUrl = returnUrl;
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
                var otpAuthUrl =
                    $"otpauth://totp/XrmboxVoC:{Uri.EscapeDataString(email)}?secret={Uri.EscapeDataString(key ?? string.Empty)}&issuer=Xrmbox";

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