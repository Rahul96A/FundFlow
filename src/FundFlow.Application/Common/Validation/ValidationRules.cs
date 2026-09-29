using System.Text.RegularExpressions;
using FluentValidation;

namespace FundFlow.Application.Common.Validation;

public static partial class ValidationRules
{
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// Length-first password policy (NIST 800-63B style): 12+ characters with mixed case and a digit.
    /// The upper bound stops absurdly long inputs from being used to burn hashing CPU.
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustBeStrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("A password is required.")
            .MinimumLength(MinPasswordLength).WithMessage($"Use at least {MinPasswordLength} characters.")
            .MaximumLength(MaxPasswordLength).WithMessage($"Use at most {MaxPasswordLength} characters.")
            .Must(p => p is not null && p.Any(char.IsLower)).WithMessage("Include a lowercase letter.")
            .Must(p => p is not null && p.Any(char.IsUpper)).WithMessage("Include an uppercase letter.")
            .Must(p => p is not null && p.Any(char.IsDigit)).WithMessage("Include a number.");

    public static IRuleBuilderOptions<T, string> MustBeEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("A valid email address is required.")
            .MaximumLength(254).WithMessage("The email address is too long.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .Must(HasDottedDomain).WithMessage("A valid email address is required.");

    // FluentValidation's built-in rule accepts "user@host"; a sign-up address must have a real, dotted domain.
    private static bool HasDottedDomain(string? email)
    {
        if (email is null)
        {
            return false;
        }

        var at = email.LastIndexOf('@');
        var domain = at < 0 ? string.Empty : email[(at + 1)..];
        return at > 0 && domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    public static IRuleBuilderOptions<T, string> MustBePersonName<T>(this IRuleBuilder<T, string> rule, string label) =>
        rule
            .NotEmpty().WithMessage($"{label} is required.")
            .MaximumLength(100).WithMessage($"{label} must be 100 characters or fewer.");

    public static IRuleBuilderOptions<T, string?> MustBeOptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .MaximumLength(30).WithMessage("The phone number is too long.")
            .Must(p => string.IsNullOrWhiteSpace(p) || PhonePattern().IsMatch(p))
            .WithMessage("Enter a valid phone number.");

    public static IRuleBuilderOptions<T, string> MustBeToken<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("The link is invalid or has expired.")
            .MaximumLength(200).WithMessage("The link is invalid or has expired.");

    [GeneratedRegex(@"^[+()\-.\s0-9xX]{5,30}$")]
    private static partial Regex PhonePattern();
}
