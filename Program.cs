using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Microsoft.Extensions.Logging;
using Xrmbox.VoC.Portal.Data;
using Xrmbox.VoC.Portal.Models;
using Microsoft.AspNetCore.HttpOverrides;
using Xrmbox.VoC.Portal.Services;

// 1. ON DÉCLARE LE BUILDER EN PREMIER
var builder = WebApplication.CreateBuilder(args);

// --- CONFIGURATION BDD ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' introuvable.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- SERVICES IDENTITY & AUTHENTICATION ---
// Ajout du support des rôles via .AddRoles<IdentityRole>()
builder.Services.AddDefaultIdentity<ApplicationUser>(options => {
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

// --- POLITIQUES D'ACCÈS ---
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "NobodyCanAccess");
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("NobodyCanAccess", policy => policy.RequireAssertion(context => false));
});

// --- AUTRES SERVICES ---
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<DataverseService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHostedService<ReminderWorker>();

// Enregistrement du service EmailSender (IEmailSender) pour l'Identity UI
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.ConfigureApplicationCookie(options =>
{
    // C'est cette ligne qui fait la différence entre local et serveur :
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;

    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});
// 2. ON CONSTRUIT L'APPLICATION
var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// --- SEEDING : création/initialisation des rôles au démarrage ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // Méthode bloquante au démarrage pour s'assurer que les rôles existent
        SeedData.InitializeAsync(services).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Erreur lors de l'initialisation des rôles.");
    }
}

// --- MIDDLEWARE PIPELINE ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// MIDDLEWARE DE REDIRECTION FORCÉE
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/Identity/Account/Login");
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    // On ajoute l'URL ngrok et on retire l'expression [::1] qui cause l'erreur de syntaxe
    context.Response.Headers.Add("Content-Security-Policy",
        "frame-ancestors 'self' https://*.unlayer.com https://hylotheistical-unepauletted-aide.ngrok-free.dev; " +
        "frame-src 'self' https://*.unlayer.com; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://*.unlayer.com https://cdn.jsdelivr.net https://code.jquery.com https://unpkg.com;");

    await next();
});
// --- ROUTES ---
//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(     
    name: "default",
    pattern: "{controller=Admin}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();