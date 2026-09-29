using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Identity;

public sealed record LockoutPolicy(int MaxFailedAttempts, TimeSpan Duration);

public enum UserCreationKind
{
    /// <summary>Self-service sign-up (organization owner).</summary>
    Registered = 1,

    /// <summary>Invited by an administrator; sets their own password via an invitation link.</summary>
    Invited = 2,

    /// <summary>Provisioned by the platform (super admin bootstrap, seeding). No email flow.</summary>
    Provisioned = 3,
}

public sealed record UserCreatedDomainEvent(Guid UserId, Guid? TenantId, string Email, UserCreationKind Kind) : DomainEvent;

/// <summary>
/// The "requested" events carry everything a handler needs to address an email, because they are handled
/// before the unit of work commits (the user row may not exist in the database yet).
/// </summary>
public sealed record UserInvitedDomainEvent(Guid UserId, Guid? TenantId, string Email, string FirstName) : DomainEvent;

public sealed record EmailVerificationRequestedDomainEvent(Guid UserId, Guid? TenantId, string Email, string FirstName) : DomainEvent;

public sealed record PasswordResetRequestedDomainEvent(Guid UserId, Guid? TenantId, string Email, string FirstName) : DomainEvent;

/// <summary>
/// A person who can sign in. Belongs to exactly one organization (<see cref="TenantId"/>), or to the platform
/// itself when <see cref="TenantId"/> is null (SUPER_ADMIN). Email addresses are unique platform-wide.
/// </summary>
public sealed class User : AuditableEntity, IOptionalTenantEntity
{
    private readonly List<UserRole> _userRoles = [];

    private User()
    {
    }

    public Guid? TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string NormalizedEmail { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Rotates whenever credentials change; lets us invalidate anything derived from the old password.</summary>
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public bool HasPassword => PasswordHash is not null;

    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToUpperInvariant();
    }

    /// <summary>Self-service sign-up. The account starts unverified and a verification email is requested.</summary>
    public static User Register(
        Guid tenantId,
        string email,
        string firstName,
        string lastName,
        string passwordHash)
    {
        var user = Create(tenantId, email, firstName, lastName, passwordHash, emailConfirmed: false);
        user.Raise(new UserCreatedDomainEvent(user.Id, tenantId, user.Email, UserCreationKind.Registered));
        user.Raise(new EmailVerificationRequestedDomainEvent(user.Id, tenantId, user.Email, user.FirstName));
        return user;
    }

    /// <summary>An administrator invites a colleague. No password until the invitation is accepted.</summary>
    public static User Invite(Guid tenantId, string email, string firstName, string lastName, string? phoneNumber)
    {
        var user = Create(tenantId, email, firstName, lastName, passwordHash: null, emailConfirmed: false);
        user.PhoneNumber = Normalize(phoneNumber);
        user.Raise(new UserCreatedDomainEvent(user.Id, tenantId, user.Email, UserCreationKind.Invited));
        user.Raise(new UserInvitedDomainEvent(user.Id, tenantId, user.Email, user.FirstName));
        return user;
    }

    /// <summary>Platform-level account (no tenant) or trusted provisioning: verified immediately, no emails.</summary>
    public static User Provision(
        Guid? tenantId,
        string email,
        string firstName,
        string lastName,
        string passwordHash)
    {
        var user = Create(tenantId, email, firstName, lastName, passwordHash, emailConfirmed: true);
        user.Raise(new UserCreatedDomainEvent(user.Id, tenantId, user.Email, UserCreationKind.Provisioned));
        return user;
    }

    private static User Create(
        Guid? tenantId,
        string email,
        string firstName,
        string lastName,
        string? passwordHash,
        bool emailConfirmed)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainException("user.email_invalid", "A valid email address is required.");
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("user.name_required", "First and last name are required.");
        }

        return new User
        {
            TenantId = tenantId,
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            PasswordHash = passwordHash,
            EmailConfirmed = emailConfirmed,
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    /// <summary>Registers a failed sign-in. Returns true when this attempt triggered a lockout.</summary>
    public bool RegisterFailedLogin(DateTimeOffset now, LockoutPolicy policy)
    {
        AccessFailedCount++;
        if (AccessFailedCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        LockoutEnd = now.Add(policy.Duration);
        AccessFailedCount = 0;
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    /// <summary>Sets a new password. Clears lockout state and rotates the security stamp.</summary>
    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid().ToString("N");
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    public void ConfirmEmail() => EmailConfirmed = true;

    /// <summary>Completes an invitation: the invitee proved control of the mailbox and chose a password.</summary>
    public void AcceptInvitation(string passwordHash, string? firstName, string? lastName)
    {
        SetPassword(passwordHash);
        EmailConfirmed = true;
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            FirstName = firstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(lastName))
        {
            LastName = lastName.Trim();
        }
    }

    public void RequestEmailVerification()
    {
        if (EmailConfirmed)
        {
            throw new DomainException("user.email_already_confirmed", "This email address is already verified.");
        }

        Raise(new EmailVerificationRequestedDomainEvent(Id, TenantId, Email, FirstName));
    }

    public void RequestPasswordReset()
    {
        if (!IsActive)
        {
            throw new DomainException("user.deactivated", "This account has been deactivated.");
        }

        Raise(new PasswordResetRequestedDomainEvent(Id, TenantId, Email, FirstName));
    }

    public void ResendInvitation()
    {
        if (HasPassword)
        {
            throw new DomainException("user.invitation_already_accepted", "This user has already accepted their invitation.");
        }

        if (!IsActive)
        {
            throw new DomainException("user.deactivated", "This account has been deactivated.");
        }

        Raise(new UserInvitedDomainEvent(Id, TenantId, Email, FirstName));
    }

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("user.name_required", "First and last name are required.");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = Normalize(phoneNumber);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public void Unlock()
    {
        LockoutEnd = null;
        AccessFailedCount = 0;
    }

    /// <summary>
    /// Replaces the user's role set. Returns what changed so callers can audit it.
    /// Requires <see cref="UserRoles"/> to be loaded.
    /// </summary>
    public (IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed) SetRoles(
        IReadOnlyCollection<Role> desiredRoles,
        Guid? assignedBy,
        DateTimeOffset now)
    {
        var desiredIds = desiredRoles.Select(r => r.Id).ToHashSet();

        var removed = _userRoles.Where(ur => !desiredIds.Contains(ur.RoleId)).ToList();
        foreach (var userRole in removed)
        {
            _userRoles.Remove(userRole);
        }

        var existingIds = _userRoles.Select(ur => ur.RoleId).ToHashSet();
        var added = desiredRoles.Where(r => !existingIds.Contains(r.Id)).ToList();
        foreach (var role in added)
        {
            _userRoles.Add(new UserRole(Id, role.Id, TenantId, assignedBy, now));
        }

        return (added.Select(r => r.Id).ToArray(), removed.Select(r => r.RoleId).ToArray());
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
