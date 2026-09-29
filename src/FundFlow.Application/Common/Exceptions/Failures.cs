using FluentValidation;
using FluentValidation.Results;

namespace FundFlow.Application.Common.Exceptions;

public static class Failures
{
    /// <summary>A field-level validation failure raised from a handler (surfaces as HTTP 400 with an errors map).</summary>
    public static ValidationException Field(string field, string message) =>
        new([new ValidationFailure(field, message)]);

    public static ValidationException InvalidLink() =>
        Field("token", "This link is invalid or has expired.");
}
