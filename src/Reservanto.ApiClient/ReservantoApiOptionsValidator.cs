using Microsoft.Extensions.Options;

namespace Reservanto.ApiClient;

/// <summary>
/// Kontrola konfigurace při startu. Chybějící LongTimeToken chybou není: nepřipojená instance musí nastartovat,
/// aby se dala připojit (<c>docs/SPEC.md</c> sekce 6). Kontroluje jen poprvé, tedy při startu: konfigurace se za běhu
/// načítá znovu a výjimka z <see cref="IOptionsMonitor{TOptions}"/> by shodila každou stránku. Za běhu adresu hlídá
/// <see cref="ReservantoApiOptions.ApiAddress"/> ve chvíli, kdy klient sestavuje spojení.
/// </summary>
internal sealed class ReservantoApiOptionsValidator : IValidateOptions<ReservantoApiOptions>
{
    private int _validated;

    public ValidateOptionsResult Validate(string? name, ReservantoApiOptions options)
    {
        if (Interlocked.Exchange(ref _validated, 1) == 1)
        {
            return ValidateOptionsResult.Skip;
        }

        return ReservantoApiOptions.TryGetApiAddress(options.BaseUrl, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{ReservantoApiOptions.SectionName}:BaseUrl musí být absolutní https adresa.");
    }
}
