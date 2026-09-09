namespace Atlas.Application.Common.Exceptions;

/// <summary>Thrown when a requested entity does not exist. Mapped to HTTP 404 by the API's exception middleware.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with id '{key}' was not found.")
    {
    }
}
