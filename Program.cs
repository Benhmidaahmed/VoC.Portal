using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xrmbox.VoC.Portal.Services; // Assure-toi que c'est le bon namespace pour DataverseService

var builder = WebApplication.CreateBuilder(args);

// --- 1. ENREGISTREMENT DES SERVICES ---

// Activer Razor Pages + MVC Controllers With Views
builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();

// AJOUT CRUCIAL : Enregistrer DataverseService pour l'injection de dépendances
// On utilise AddScoped pour qu'une nouvelle connexion soit créée par requête HTTP
builder.Services.AddScoped<DataverseService>();

// Optionnel : Si ton DataverseService a besoin de HttpClient (souvent le cas)
builder.Services.AddHttpClient();

// --- 2. CONFIGURATION DE L'APPLICATION ---

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Mapper les routes MVC (indispensable pour SurveysController et SurveyViewController)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();