namespace Reservanto.ApiClient.Models;

// Model 1:1 podle OpenAPI. Kontakty, IČO a adresy aplikace nepoužívá, model je má kvůli přísnému čtení.

/// <summary>Informace o obchodníkovi, se kterým se aktuálně pracuje.</summary>
public sealed class MerchantInfoApiModel
{
    /// <summary>Vnitřní Id.</summary>
    public int Id { get; init; }

    /// <summary>IČO.</summary>
    public string? PersonalIdentifier { get; init; }

    /// <summary>IČ DPH.</summary>
    public string? VatId { get; init; }

    /// <summary>Kontaktní e-mail.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Kontaktní telefon.</summary>
    public string? ContactPhone { get; init; }

    /// <summary>Fakturační adresa.</summary>
    public AddressApiModel? InvoiceAddress { get; init; }

    /// <summary>Plátce DPH.</summary>
    public bool IsVatPayer { get; init; }

    /// <summary>Korespondenční adresa.</summary>
    public AddressApiModel? MailingAddress { get; init; }

    /// <summary>Název.</summary>
    public string? Name { get; init; }

    /// <summary>DIČ.</summary>
    public string? VatNumber { get; init; }

    /// <summary>Webové stránky.</summary>
    public string? Web { get; init; }
}
