using FundFlow.Application.Common.Options;
using FundFlow.Application.Notifications;

namespace FundFlow.Application.Tests.Notifications;

public class TransactionalEmailsTests
{
    private const string Link = "https://app.fundflow.test/verify-email?token=abc123";

    [Fact]
    public void Verification_email_contains_the_link_in_both_html_and_text()
    {
        var email = TransactionalEmails.EmailVerification("ada@example.org", "Ada", Link, "FundFlow", 48);

        email.ToAddress.Should().Be("ada@example.org");
        email.Subject.Should().Contain("Verify");
        email.TextBody.Should().Contain(Link).And.Contain("48 hours");
        email.HtmlBody.Should().Contain($"href=\"{Link}\"");
    }

    [Fact]
    public void Untrusted_values_are_html_encoded()
    {
        var email = TransactionalEmails.Invitation(
            "ada@example.org",
            "<img src=x onerror=alert(1)>",
            "<script>alert('org')</script>",
            "https://app.fundflow.test/accept-invitation?token=a&b=<c>",
            "FundFlow",
            7);

        email.HtmlBody.Should().NotContain("<script>").And.NotContain("<img src=x");
        email.HtmlBody.Should().Contain("&lt;script&gt;").And.Contain("&lt;img");
        email.HtmlBody.Should().Contain("token=a&amp;b=&lt;c&gt;");
    }

    [Fact]
    public void Password_reset_states_its_short_lifetime()
    {
        var email = TransactionalEmails.PasswordReset("ada@example.org", "Ada", Link, "FundFlow", 60);

        email.TextBody.Should().Contain("60 minutes").And.Contain("will not change");
    }

    [Fact]
    public void Links_are_built_from_the_public_base_url_with_encoded_query_values()
    {
        var options = new AppOptions { PublicBaseUrl = "https://app.fundflow.test/" };

        options.BuildLink("/reset-password", ("token", "a b+c/d=")).Should().Be("https://app.fundflow.test/reset-password?token=a%20b%2Bc%2Fd%3D");
        options.BuildLink("verify-email").Should().Be("https://app.fundflow.test/verify-email");
    }
}
