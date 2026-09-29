namespace FundFlow.Application.Common.Exceptions;

/// <summary>The requested resource does not exist (or is invisible to the caller's tenant). Maps to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.")
    {
        Resource = resource;
    }

    public string Resource { get; }
}

/// <summary>The request conflicts with current state (duplicates, concurrent edits). Maps to HTTP 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message, string code = "conflict", IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Code = code;
        Errors = errors;
    }

    public string Code { get; }

    /// <summary>Optional field-level detail (property name -> messages) rendered like validation errors.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static ConflictException ForField(string field, string message, string code = "conflict") =>
        new(message, code, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>The caller is authenticated but not allowed to do this. Maps to HTTP 403.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message, string code = "forbidden")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Authentication failed or is required. Maps to HTTP 401.</summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message, string code = "unauthorized")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
