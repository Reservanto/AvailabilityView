using System.Text.Encodings.Web;
using System.Text.Unicode;
using AvailabilityView.Application.Availability;
using AvailabilityView.Infrastructure;
using AvailabilityView.Web.Availability;
using AvailabilityView.Web.Security;
using AvailabilityView.Web.Setup;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.WebEncoders;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Bez tohoto Razor kóduje českou diakritiku jako číselné entity.
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddLocalization();
builder.Services.AddOptions<RequestLocalizationOptions>().Configure<AvailabilitySettings>((options, settings) =>
{
    options.DefaultRequestCulture = new RequestCulture(VisitorLanguageProvider.SetupCulture);
    options.SupportedCultures = VisitorLanguageProvider.Cultures;
    options.SupportedUICultures = VisitorLanguageProvider.Cultures;
    options.RequestCultureProviders = [new VisitorLanguageProvider(settings)];
});
builder.Services.AddControllersWithViews().AddViewLocalization();
builder.Services.AddAvailabilityViewInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IHttpContextFactory>(services => new CallbackQueryProtection(new DefaultHttpContextFactory(services)));
builder.Services.AddRequestLimits();
builder.Services.AddEmbedding(builder.Configuration);

WebApplication app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
// Skript pro web merchanta má krátkou adresu v kořeni instance, je veřejné rozhraní.
app.UseRewriter(new RewriteOptions().AddRewrite(@"^embed\.js$", "js/embed.js", skipRemainingRules: true));
app.UseStaticFiles();

// Jazyk se určuje až po výběru stránky, průvodce zůstává česky.
app.UseRouting();
app.UseRequestLocalization();

// Za statickými soubory, ty se nepočítají. Za jazykem, aby hláška mluvila jazykem stránky.
app.UseRateLimiter();
app.MapControllers();

await app.RunAsync();

/// <summary>Vstupní bod aplikace, veřejný kvůli testům přes <c>WebApplicationFactory</c>.</summary>
public partial class Program;
