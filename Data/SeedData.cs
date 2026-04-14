using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Xrmbox.VoC.Portal.Data
{
    public static class SeedData
    {
        private static readonly string[] Roles = new[] { "SuperAdmin", "Admin" };

        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("SeedData");

            foreach (var roleName in Roles)
            {
                try
                {
                    var exists = await roleManager.RoleExistsAsync(roleName);
                    if (!exists)
                    {
                        var result = await roleManager.CreateAsync(new IdentityRole(roleName));
                        if (!result.Succeeded)
                        {
                            var errors = string.Join(", ", result.Errors);
                            logger?.LogWarning("Impossible de créer le rôle {Role}. Erreurs: {Errors}", roleName, errors);
                        }
                        else
                        {
                            logger?.LogInformation("Rôle {Role} créé.", roleName);
                        }
                    }
                    else
                    {
                        logger?.LogDebug("Rôle {Role} existe déjà.", roleName);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Exception lors de la création du rôle {Role}.", roleName);
                }
            }
        }
    }
}