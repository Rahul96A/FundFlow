using FluentValidation;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Telemetry;
using FundFlow.Domain.SharedKernel;
using FundFlow.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Api;

/// <summary>
/// The single place where exceptions become HTTP responses:
/// validation → 400, unauthenticated → 401, forbidden → 403, missing → 404, conflict → 409,
/// broken business rule → 422, anything else → 500 (no stack traces, no internals).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int SqlDuplicateKey = 2627;
    private const int SqlDuplicateIndex = 2601;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499; // client closed the request; nothing to report
            return true;
        }

        var problem = Map(exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        AppMetrics.RecordException(exception.GetType().Name, httpContext.Response.StatusCode);

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {Status} ({ExceptionType}): {Message}", problem.Status, exception.GetType().Name, exception.Message);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => Problems.Validation(
            StatusCodes.Status400BadRequest,
            validation.Errors
                .GroupBy(e => e.PropertyName)
                .Select(g => KeyValuePair.Create(g.Key, g.Select(e => e.ErrorMessage).Distinct().ToArray()))),

        UnauthorizedException unauthorized => Problems.Simple(StatusCodes.Status401Unauthorized, unauthorized.Message, unauthorized.Code),
        ForbiddenException forbidden => Problems.Simple(StatusCodes.Status403Forbidden, forbidden.Message, forbidden.Code),
        NotFoundException notFound => Problems.Simple(StatusCodes.Status404NotFound, notFound.Message, "not_found"),

        ConflictException { Errors: { Count: > 0 } errors } conflict => Problems.Validation(
            StatusCodes.Status409Conflict, errors, conflict.Code, conflict.Message),
        ConflictException conflict => Problems.Simple(StatusCodes.Status409Conflict, conflict.Message, conflict.Code),

        DomainException domain => Problems.Simple(StatusCodes.Status422UnprocessableEntity, domain.Message, domain.Code),

        DbUpdateConcurrencyException => Problems.Simple(
            StatusCodes.Status409Conflict,
            "This record was changed by someone else. Reload it and try again.",
            "concurrency_conflict"),

        DbUpdateException { InnerException: SqlException { Number: SqlDuplicateKey or SqlDuplicateIndex } } => Problems.Simple(
            StatusCodes.Status409Conflict,
            "A record with the same unique value already exists.",
            "duplicate"),

        TenantViolationException => Problems.Simple(
            StatusCodes.Status403Forbidden,
            "You do not have access to this resource.",
            "tenant_violation"),

        BadHttpRequestException or System.Text.Json.JsonException => Problems.Simple(
            StatusCodes.Status400BadRequest,
            "The request body is malformed.",
            "malformed_request",
            "Malformed request"),

        _ => Problems.Simple(
            StatusCodes.Status500InternalServerError,
            environment.IsDevelopment() ? exception.Message : "Something went wrong on our side. Please try again; if the problem persists, contact support and quote the trace id.",
            "internal_error"),
    };
}
