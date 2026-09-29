using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace FundFlow.Api.Controllers;

/// <summary>
/// Controllers are transport adapters only: bind the request, hand a command or query to MediatR, shape the HTTP
/// response. Business rules live in the domain and application layers, never here.
/// <para>
/// Deliberately no class-level <c>[Produces("application/json")]</c>: it would rewrite the content type of every
/// error response from <c>application/problem+json</c> (RFC 7807) to plain JSON.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
public abstract class ApiControllerBase : ControllerBase
{
}
