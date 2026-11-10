using System.Text.Json;
using AvailabilityView.Application.Connection;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityView.Web.Setup;

/// <summary>Průvodce připojením k Reservantu (<c>docs/SPEC.md</c> sekce 6).</summary>
public sealed class SetupController(
    IInstanceConnection connection,
    IConnectionSetup setup,
    IConnectionCheck check,
    IWebHostEnvironment environment) : Controller
{
    private const int MaxClientIdLength = 200;
    private const string SetupPage = "~/Setup";
    private const string OccupancyChosen = "1";

    /// <summary>Nepřipojená instance ukáže postup připojení, připojená jen stav.</summary>
    [HttpGet("/Setup")]
    public async Task<IActionResult> IndexAsync(CancellationToken ct)
    {
        if (connection.IsConnected)
        {
            return View("Status", await check.CheckAsync(ct));
        }

        return View("Index", ConnectModel(setup.PrefilledClientId, clientIdMissing: false, occupancy: false));
    }

    /// <summary>
    /// Pošle správce na souhlas. Odkazem, ne přesměrováním: prohlížeč hlídá CSP <c>form-action</c> v celém řetězci
    /// přesměrování a Reservanto z adresy API přesměruje na přihlášení na jiné doméně. Bez ochrany proti CSRF: akce
    /// nic nemění, jen sestaví adresu, a ochrana by nastavila cookie, kterou projekt nemá mít. ClientId se čte
    /// z formuláře ručně, aby se neobjevil ve výpisu argumentů akce.
    /// </summary>
    [HttpPost("/Setup/Connect")]
    public async Task<IActionResult> ConnectAsync(CancellationToken ct)
    {
        if (connection.IsConnected)
        {
            return LocalRedirect(SetupPage);
        }

        IFormCollection? form = Request.HasFormContentType ? await Request.ReadFormAsync(ct) : null;
        string clientId = form?["clientId"].ToString().Trim() ?? string.Empty;

        // Obsazenost je volba správce, bez zaškrtnutí se o rezervace nežádá (SPEC sekce 6).
        bool occupancy = form?["occupancy"].ToString() == OccupancyChosen;
        if (clientId.Length == 0 || clientId.Length > MaxClientIdLength)
        {
            return View("Index", ConnectModel(null, clientIdMissing: true, occupancy));
        }

        return View("Continue", setup.BuildConsentUrl(clientId, CallbackUrl(), occupancy));
    }

    /// <summary>Návrat ze souhlasu. Parametry čte z <see cref="CallbackQuery"/>, adresa je v této chvíli už nemá.</summary>
    [HttpGet(CallbackQueryProtection.CallbackPath)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Callback()
    {
        // Připojená instance token nikdy neukáže, ani ten z adresy (SPEC sekce 6).
        if (connection.IsConnected)
        {
            return LocalRedirect(SetupPage);
        }

        CallbackQuery? query = HttpContext.Features.Get<CallbackQuery>();
        ConsentResult consent = setup.ReadConsent(query?.Accepted, query?.Token);
        return consent.Outcome == ConsentOutcome.Accepted
            ? View("Token", new TokenViewModel(consent.Token!, environment.IsDevelopment()))
            : View("CallbackFailed", consent.Outcome);
    }

    private ConnectViewModel ConnectModel(string? clientId, bool clientIdMissing, bool occupancy) =>
        new(clientId, CallbackUrl(), setup.BaseRights, setup.OccupancyRights, clientIdMissing, occupancy);

    private Uri CallbackUrl() =>
        new(UriHelper.BuildAbsolute(Request.Scheme, Request.Host, Request.PathBase, CallbackQueryProtection.CallbackPath));
}

/// <summary>Model postupu připojení.</summary>
/// <param name="ClientId">Předvyplněné Id aplikace.</param>
/// <param name="CallbackUrl">Návratová adresa této instance.</param>
/// <param name="Rights">Práva, o která se žádá vždy.</param>
/// <param name="OccupancyRights">Práva navíc pro obsazenost z rezervací.</param>
/// <param name="ClientIdMissing">Formulář přišel bez platného Id aplikace.</param>
/// <param name="Occupancy">Volba obsazenosti je zaškrtnutá.</param>
public sealed record ConnectViewModel(
    string? ClientId,
    Uri CallbackUrl,
    IReadOnlyList<string> Rights,
    IReadOnlyList<string> OccupancyRights,
    bool ClientIdMissing,
    bool Occupancy);

/// <summary>Model stránky s tokenem. Záměrně ne record: vygenerovaný <c>ToString()</c> by vypsal token.</summary>
/// <param name="token">Token s ověřeným tvarem.</param>
/// <param name="isDevelopment">Instance běží ve vývoji, token patří do user-secrets.</param>
public sealed class TokenViewModel(string token, bool isDevelopment)
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    /// <summary>Token s ověřeným tvarem.</summary>
    public string Token { get; } = token;

    /// <summary>Instance běží ve vývoji, token patří do user-secrets.</summary>
    public bool IsDevelopment { get; } = isDevelopment;

    /// <summary>Příkaz pro vývoj.</summary>
    public string UserSecretsCommand => $"dotnet user-secrets set \"Reservanto:LongTimeToken\" \"{Token}\" --project src/AvailabilityView.Web";

    /// <summary>Část souboru <c>appsettings.Production.json</c>.</summary>
    public string ProductionSettings => JsonSerializer.Serialize(new { Reservanto = new { LongTimeToken = Token } }, _indented);
}
