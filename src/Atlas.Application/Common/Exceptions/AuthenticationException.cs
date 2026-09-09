namespace Atlas.Application.Common.Exceptions;

/// <summary>Thrown when credentials are missing or invalid. Mapped to HTTP 401 by the API's exception middleware.</summary>
public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message)
    {
    }
}
