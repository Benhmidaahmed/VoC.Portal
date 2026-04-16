using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Xrmbox.VoC.Portal.Models;

namespace Xrmbox.VoC.Portal.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Se souvenir de moi ?")]
            public bool RememberMe { get; set; }
        }

        public void OnGet(string? returnUrl = null)
        {
            // Si aucune destination n'est fournie ou que c'est la Home,
            // on force la zone d'administration comme point d'entrée.
            if (string.IsNullOrEmpty(returnUrl) || returnUrl == "/")
            {
                ReturnUrl = "/Admin";
            }
            else
            {
                ReturnUrl = returnUrl;
            }
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            // Valeur par défaut si rien n'est passé
            ReturnUrl = string.IsNullOrEmpty(returnUrl) || returnUrl == "/"
                ? "/Admin"
                : returnUrl;

            if (!ModelState.IsValid) return Page();

            // Récupération de l'utilisateur
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tentative de connexion invalide.");
                return Page();
            }

            // Vérification initiale du mot de passe
            var passwordValid = await _userManager.CheckPasswordAsync(user, Input.Password);
            if (!passwordValid)
            {
                ModelState.AddModelError(string.Empty, "Tentative de connexion invalide.");
                return Page();
            }

            // Vérification du rôle
            var isPrivileged =
                await _userManager.IsInRoleAsync(user, "Admin") ||
                await _userManager.IsInRoleAsync(user, "SuperAdmin");

            // CAS 1 : Admin / SuperAdmin sans MFA configuré
            if (isPrivileged && !user.TwoFactorEnabled)
            {
                await _signInManager.SignInAsync(user, isPersistent: Input.RememberMe);
                return RedirectToAction("Setup", "Mfa", new { returnUrl = "/Admin" });
            }

            // CAS 2 : tentative de connexion standard
            var result = await _signInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // On re-récupère l'utilisateur pour cohérence potentielle (mais ici déjà chargé)
                var loggedUser = await _userManager.FindByEmailAsync(Input.Email);
                if (loggedUser != null)
                {
                    var isAdmin =
                        await _userManager.IsInRoleAsync(loggedUser, "Admin") ||
                        await _userManager.IsInRoleAsync(loggedUser, "SuperAdmin");

                    if (isAdmin)
                    {
                        // Pour un admin, on force toujours /Admin
                        return RedirectToAction("Index", "Admin");
                    }

                    // Utilisateur non admin : on le déconnecte immédiatement et on le redirige vers AccessDenied
                    await _signInManager.SignOutAsync();
                    return Redirect("/Account/AccessDenied");
                }

                // Par sécurité : si loggedUser est null, on force la déconnexion
                await _signInManager.SignOutAsync();
                return Redirect("/Account/AccessDenied");
            }

            if (result.RequiresTwoFactor)
            {
                // On force également le retour vers /Admin après la validation 2FA
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = "/Admin", RememberMe = Input.RememberMe });
            }

            ModelState.AddModelError(string.Empty, "Tentative de connexion invalide.");
            return Page();
        }
    }
}