using Asp.Versioning;
using FundFlow.Domain.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only endpoints (registered as an application part by <see cref="ApiFactory"/>, never shipped) that throw on
/// demand so the exception-to-HTTP mapping can be verified against the real pipeline.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[AllowAnonymous]
[Route("api/v{version:apiVersion}/_test")]
public sealed class TestFaultsController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Cannot connect: Server=prod-sql;User Id=sa;Password=hunter2");

    [HttpGet("concurrency")]
    public IActionResult Concurrency() => throw new DbUpdateConcurrencyException("row version mismatch");

    [HttpGet("rule")]
    public IActionResult Rule() =>
        throw new DomainException("donation.amount_invalid", "The donation amount must be greater than zero.");
}
