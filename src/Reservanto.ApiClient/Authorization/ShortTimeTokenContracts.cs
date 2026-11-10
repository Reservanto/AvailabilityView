using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient.Authorization;

/// <summary>Požadavek pro získání ShortTimeTokenu (<c>/v1/Authorize/GetShortTimeToken</c>).</summary>
internal sealed class ShortTimeTokenRequest : ApiRequest
{
    /// <summary>Merchantův LTT.</summary>
    public string? LongTimeToken { get; init; }
}

/// <summary>Odpověď se ShortTimeTokenem.</summary>
internal sealed class ShortTimeTokenResponse : ApiResponse
{
    /// <summary>STT pro hlavičku <c>Authorization</c>; platí 30 minut.</summary>
    public string? ShortTimeToken { get; init; }
}
