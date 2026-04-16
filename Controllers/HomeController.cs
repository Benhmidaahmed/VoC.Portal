using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Xrmbox.VoC.Portal.Models;

namespace Xrmbox.VoC.Portal.Controllers
{
    [Authorize] // Ajoutez ceci pour forcer la connexion sur tout le contrôleur
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            // Si c'est un Admin, on le redirige immédiatement vers l'espace Admin
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            {
                return RedirectToAction("Index", "Admin");
            }

            // Pour les autres utilisateurs connectés (s'il y en a)
            return View();
        }
    }
}
