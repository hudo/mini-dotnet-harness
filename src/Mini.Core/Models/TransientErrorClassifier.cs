using Anthropic.Exceptions;

namespace Mini.Core.Models;

/// <summary>Which failures of a stream are worth another try.</summary>
// Python: loop.py transient
public static class TransientErrorClassifier
{
    /// <summary>Worth another try mid-stream: overloaded, rate-limited, a server error, a broken connection or stream;
    /// never a request the API refused (validation, auth), nor our own failure (a closed stdout). Mid-stream, an error
    /// event comes with HTTP 200 and its type (AnthropicSseException). A dropped connection reaches us as the HTTP
    /// library's IOException; the request methods wrap that one as HttpRequestException, so a bare IOException here is
    /// ours and not worth retrying.</summary>
    // Python: loop.py transient
    public static bool IsTransient(Exception e)
    {
        if (OwnFailure.IsOurs(e)) return false;
        if (e is AnthropicApiException { StatusCode: var status } && ((int)status == 429 || (int)status >= 500)) return true;
        // shortcut: ErrorType is [Obsolete] from SDK 13.6 (use Type there), upgrade with the package
        return e switch
        {
            AnthropicServiceException { ErrorType: Anthropic.Models.ErrorType.OverloadedError or Anthropic.Models.ErrorType.RateLimitError
                or Anthropic.Models.ErrorType.ApiError or Anthropic.Models.ErrorType.TimeoutError } => true,
            AnthropicIOException or HttpRequestException or StreamCutException => true,
            OperationCanceledException { InnerException: TimeoutException } => true, // the HTTP client's own timeout
            _ => false,
        };
    }
}
