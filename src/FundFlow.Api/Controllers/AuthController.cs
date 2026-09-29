using FundFlow.Api.Configuration;
using FundFlow.Api.Security;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Identity.Auth;
using FundFlow.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FundFlow.Api.Controllers;

/// <summary>Sign-up, sign-in, token refresh and self-service account management.</summary>
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(ISender sender, TimeProvider clock) : ApiControllerBase
{
    /// <summary>Creates a new organization (tenant) and its first administrator.</summary>
    /// <remarks>The administrator receives a verification email and must confirm it before signing in.</remarks>
    [HttpPost("register-organization")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType<RegisterOrganizationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterOrganization(RegisterOrganizationRequest request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new RegisterOrganizationCommand(
                request.OrganizationName,
                request.OrganizationSlug,
                request.FirstName,
                request.LastName,
                request.Email,
                request.Password,
                request.TimeZoneId,
                request.CurrencyCode),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Exchanges credentials for an access token and starts a session (refresh token set as an HttpOnly cookie).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthTokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        RefreshTokenCookie.Append(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(ToResponse(result));
    }

    /// <summary>Rotates the refresh token (cookie) and returns a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [RequireWebClient]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            var token = RefreshTokenCookie.Read(Request)
                        ?? throw new UnauthorizedException("Your session has expired. Sign in again.", "invalid_refresh_token");
            var result = await sender.Send(new RefreshTokenCommand(token), cancellationToken);
            RefreshTokenCookie.Append(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
            return Ok(ToResponse(result));
        }
        catch (UnauthorizedException ex)
        {
            // Answer here (rather than in the global handler) so the response can also clear the dead cookie.
            RefreshTokenCookie.Clear(Response);
            return StatusCode(StatusCodes.Status401Unauthorized, Problems.Simple(StatusCodes.Status401Unauthorized, ex.Message, ex.Code));
        }
    }

    /// <summary>Ends the current session and clears the refresh cookie. Safe to call repeatedly.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [RequireWebClient]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(RefreshTokenCookie.Read(Request)), cancellationToken);
        RefreshTokenCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Emails a password-reset link. Always answers 202, whether or not the address is registered.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        return Accepted();
    }

    /// <summary>Sets a new password using the emailed token and signs the account out everywhere.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        return NoContent();
    }

    /// <summary>Confirms an email address using the emailed token.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new VerifyEmailCommand(request.Token), cancellationToken);
        return NoContent();
    }

    /// <summary>Re-sends the verification email. Always answers 202.</summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ResendVerificationCommand(request.Email), cancellationToken);
        return Accepted();
    }

    /// <summary>Accepts an invitation: chooses a password and activates the account.</summary>
    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new AcceptInvitationCommand(request.Token, request.Password, request.FirstName, request.LastName), cancellationToken);
        return NoContent();
    }

    /// <summary>The signed-in user's profile, roles, effective permissions and organization.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken) =>
        await sender.Send(new GetCurrentUserQuery(), cancellationToken);

    /// <summary>Updates the signed-in user's own name and phone number.</summary>
    [HttpPut("me")]
    public async Task<ActionResult<CurrentUserResponse>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateProfileCommand(request.FirstName, request.LastName, request.PhoneNumber), cancellationToken);

    /// <summary>Changes the password; every other session is signed out.</summary>
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
        return NoContent();
    }

    /// <summary>The caller's active sessions (devices).</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionResponse>>> Sessions(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListMySessionsQuery(), cancellationToken));

    /// <summary>Signs out one of the caller's sessions.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeSessionCommand(sessionId), cancellationToken);
        return NoContent();
    }

    private AuthTokenResponse ToResponse(AuthResult result) =>
        new(
            result.AccessToken,
            "Bearer",
            Math.Max(0, (int)(result.AccessTokenExpiresAt - clock.GetUtcNow()).TotalSeconds),
            result.AccessTokenExpiresAt);
}
