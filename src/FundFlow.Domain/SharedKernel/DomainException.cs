namespace FundFlow.Domain.SharedKernel;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable machine-readable identifier (safe for clients to
/// switch on); <see cref="Exception.Message"/> is a human-readable explanation that is safe to show to users.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
