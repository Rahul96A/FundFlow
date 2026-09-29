using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FundFlow.Application.Common.Behaviors;

/// <summary>
/// Logs the request <em>type</em> and duration. The request payload is deliberately never logged: commands carry
/// passwords, tokens and personal data.
/// </summary>
public sealed class RequestLoggingBehavior<TRequest, TResponse>(ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const long SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception)
        {
            logger.LogDebug("Request {RequestName} failed after {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
