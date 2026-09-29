using FluentValidation;
using FundFlow.Application.Common.Behaviors;
using FundFlow.Application.Identity.Auth;
using FundFlow.Application.Identity.Users;
using MediatR;

namespace FundFlow.Application.Tests.Validation;

public class ValidationTests
{
    private static RegisterOrganizationCommand Register(
        string name = "Hope Foundation",
        string? slug = null,
        string email = "amelia@hope.org",
        string password = "Correct-Horse-Battery9") =>
        new(name, slug, "Amelia", "Reyes", email, password, "UTC", "USD");

    [Fact]
    public void A_complete_registration_is_valid()
    {
        new RegisterOrganizationValidator().Validate(Register()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Short1Aa", "12 characters")]
    [InlineData("alllowercase1234", "uppercase")]
    [InlineData("ALLUPPERCASE1234", "lowercase")]
    [InlineData("NoDigitsHereAtAll", "number")]
    public void Weak_passwords_are_rejected_with_a_helpful_message(string password, string expectedHint)
    {
        var result = new RegisterOrganizationValidator().Validate(Register(password: password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password" && e.ErrorMessage.Contains(expectedHint, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Absurdly_long_passwords_are_rejected_to_protect_the_hasher()
    {
        var result = new RegisterOrganizationValidator().Validate(Register(password: "Aa1" + new string('x', 200)));

        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    public void Invalid_emails_are_rejected(string email)
    {
        var result = new RegisterOrganizationValidator().Validate(Register(email: email));

        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("Has Spaces")]
    [InlineData("admin")]
    public void Invalid_or_reserved_slugs_are_rejected(string slug)
    {
        var result = new RegisterOrganizationValidator().Validate(Register(slug: slug));

        result.Errors.Should().Contain(e => e.PropertyName == "OrganizationSlug");
    }

    [Fact]
    public void Login_validation_is_shallow_so_old_weak_passwords_can_still_sign_in()
    {
        new LoginValidator().Validate(new LoginCommand("a@b.org", "weak")).IsValid.Should().BeTrue();
        new LoginValidator().Validate(new LoginCommand("", "x")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Changing_to_the_same_password_is_rejected()
    {
        var result = new ChangePasswordValidator().Validate(new ChangePasswordCommand("Correct-Horse-Battery9", "Correct-Horse-Battery9"));

        result.Errors.Should().Contain(e => e.PropertyName == "NewPassword" && e.ErrorMessage.Contains("different"));
    }

    [Fact]
    public void Inviting_a_user_needs_at_least_one_role()
    {
        var result = new InviteUserValidator().Validate(new InviteUserCommand("a@b.org", "A", "B", null, []));

        result.Errors.Should().Contain(e => e.PropertyName == "RoleIds");
    }

    [Theory]
    [InlineData("+1 (555) 010-0100", true)]
    [InlineData("555.010.0100 x12", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("call me maybe", false)]
    [InlineData("<script>", false)]
    public void Phone_numbers_are_validated_leniently(string? phone, bool valid)
    {
        var result = new UpdateUserValidator().Validate(new UpdateUserCommand(Guid.NewGuid(), "A", "B", phone));

        result.IsValid.Should().Be(valid);
    }

    public sealed record Ping(string Value) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.");
    }

    [Fact]
    public async Task The_pipeline_short_circuits_invalid_requests_before_the_handler_runs()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator()]);
        var handlerRan = false;

        var act = () => behavior.Handle(new Ping(""), _ => { handlerRan = true; return Task.FromResult("done"); }, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Value");
        handlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task The_pipeline_passes_valid_requests_through()
    {
        var behavior = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await behavior.Handle(new Ping("x"), _ => Task.FromResult("done"), CancellationToken.None);

        result.Should().Be("done");
    }

    [Fact]
    public async Task Requests_without_validators_are_not_blocked()
    {
        var behavior = new ValidationBehavior<Ping, string>([]);

        (await behavior.Handle(new Ping(""), _ => Task.FromResult("done"), CancellationToken.None)).Should().Be("done");
    }
}
