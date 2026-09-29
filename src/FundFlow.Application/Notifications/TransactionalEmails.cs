using System.Net;
using FundFlow.Application.Common.Abstractions;

namespace FundFlow.Application.Notifications;

/// <summary>
/// Builds the system emails that authentication flows send. Every interpolated value is HTML-encoded.
/// (Marketing/campaign email uses stored templates from the Communications module; these are fixed system messages.)
/// </summary>
public static class TransactionalEmails
{
    public static EmailMessage EmailVerification(string toAddress, string firstName, string link, string productName, int validForHours)
    {
        var subject = $"Verify your email address for {productName}";
        var text = $"Hi {firstName},\n\nConfirm your email address to finish setting up your {productName} account:\n{link}\n\n" +
                   $"This link is valid for {validForHours} hours. If you did not create an account, you can ignore this email.";
        var html = Layout(
            productName,
            $"Verify your email address",
            $"<p>Hi {Encode(firstName)},</p><p>Confirm your email address to finish setting up your {Encode(productName)} account.</p>",
            "Verify email address",
            link,
            $"This link is valid for {validForHours} hours. If you did not create an account, you can ignore this email.");
        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    public static EmailMessage PasswordReset(string toAddress, string firstName, string link, string productName, int validForMinutes)
    {
        var subject = $"Reset your {productName} password";
        var text = $"Hi {firstName},\n\nUse this link to choose a new password:\n{link}\n\n" +
                   $"It is valid for {validForMinutes} minutes. If you did not ask for this, you can ignore this email; your password will not change.";
        var html = Layout(
            productName,
            "Reset your password",
            $"<p>Hi {Encode(firstName)},</p><p>We received a request to reset your password.</p>",
            "Choose a new password",
            link,
            $"This link is valid for {validForMinutes} minutes. If you did not ask for this, ignore this email; your password will not change.");
        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    public static EmailMessage Invitation(
        string toAddress,
        string firstName,
        string organizationName,
        string link,
        string productName,
        int validForDays)
    {
        var subject = $"You have been invited to {organizationName} on {productName}";
        var text = $"Hi {firstName},\n\nYou have been invited to join {organizationName} on {productName}.\nAccept the invitation and set your password:\n{link}\n\n" +
                   $"This invitation is valid for {validForDays} days.";
        var html = Layout(
            productName,
            $"Join {organizationName}",
            $"<p>Hi {Encode(firstName)},</p><p>You have been invited to join <strong>{Encode(organizationName)}</strong> on {Encode(productName)}.</p>",
            "Accept invitation",
            link,
            $"This invitation is valid for {validForDays} days.");
        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Layout(string productName, string heading, string bodyHtml, string buttonLabel, string buttonUrl, string footer) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <body style="margin:0;padding:0;background:#f4f6f8;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#1b2430;">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 16px;">
            <tr><td align="center">
              <table role="presentation" width="560" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:8px;padding:32px;">
                <tr><td style="font-size:13px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:#4a5b70;">{{Encode(productName)}}</td></tr>
                <tr><td style="font-size:22px;font-weight:600;padding:12px 0 8px;">{{Encode(heading)}}</td></tr>
                <tr><td style="font-size:15px;line-height:1.6;">{{bodyHtml}}</td></tr>
                <tr><td style="padding:16px 0;"><a href="{{WebUtility.HtmlEncode(buttonUrl)}}" style="display:inline-block;background:#1f5eff;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:6px;font-weight:600;">{{Encode(buttonLabel)}}</a></td></tr>
                <tr><td style="font-size:13px;color:#5b6b7f;line-height:1.5;">If the button does not work, copy this address into your browser:<br><span style="word-break:break-all;">{{Encode(buttonUrl)}}</span></td></tr>
                <tr><td style="font-size:12px;color:#7a889a;padding-top:20px;border-top:1px solid #e6ebf0;">{{Encode(footer)}}</td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
