using Microsoft.Extensions.Logging;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Všechny logovací zprávy klienta na jednom místě (LoggerMessage). Žádná zpráva nenese token, LTT, heslo ani
/// tělo requestu či odpovědi — jen endpoint, status a názvy chybných parametrů; texty chyb
/// z API jen na úrovni Debug.
/// </summary>
internal static partial class ApiClientLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Reservanto API {Endpoint}: {Reason}, opakuji čtení")]
    public static partial void RetryingRead(ILogger logger, Exception exception, string endpoint, string reason);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Reservanto API {Endpoint} nedostupné")]
    public static partial void Unavailable(ILogger logger, Exception exception, string endpoint);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Reservanto API {Endpoint} vrátilo chybu; parametry: {ErrorParameters}")]
    public static partial void ErrorResponse(ILogger logger, string endpoint, IReadOnlyList<string> errorParameters);

    // Texty chyb z API mohou obsahovat údaje zákazníka (e-mail, jméno) — jen Debug, výchozí log je bez PII.
    // Pro error katalog se zapnou v dev / diagnostice.
    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Reservanto API {Endpoint}: texty chyby {ErrorMessages}")]
    public static partial void ErrorResponseTexts(ILogger logger, string endpoint, IReadOnlyList<string> errorMessages);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Reservanto API {Endpoint} selhalo (status {Status})")]
    public static partial void Failed(ILogger logger, Exception exception, string endpoint, int? status);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Reservanto API: získán nový STT")]
    public static partial void TokenAcquired(ILogger logger);

    // Provozní incident (neplatný LTT, výpadek API).
    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Reservanto API: krátkodobý token se nepodařilo získat (status {Status}, {Failures}. selhání po sobě), další pokus za {Backoff}")]
    public static partial void TokenFailed(ILogger logger, int? status, int failures, TimeSpan backoff);
}
