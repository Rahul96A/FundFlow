using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace FundFlow.Api;

/// <summary>
/// Gives every request a correlation id (honouring a well-formed inbound <c>X-Correlation-Id</c>), returns it in the
/// response, and attaches it to every log line. Malformed inbound values are ignored: they end up in logs.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "FundFlow.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName].ToString();
        var correlationId = !string.IsNullOrEmpty(inbound) && ValidId().IsMatch(inbound)
            ? inbound
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex ValidId();
}
