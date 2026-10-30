namespace SmartPOS.Application.Common;

/// <summary>
/// Expected, user-facing failures. The API's global exception handler maps each type to an HTTP
/// status code and returns <see cref="Exception.Message"/> as the problem detail, so messages here
/// must never contain secrets or internal details.
/// </summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>400: the request is well-formed but breaks a business rule.</summary>
public sealed class BusinessRuleException(string message) : AppException(message);

/// <summary>401: credentials were rejected.</summary>
public sealed class AuthenticationFailedException() : AppException("Invalid email or password.");

/// <summary>403: the caller is authenticated but not allowed to do this.</summary>
public sealed class ForbiddenException(string message) : AppException(message);

/// <summary>404</summary>
public sealed class NotFoundException(string message) : AppException(message)
{
    public static NotFoundException For(string entity, object id) => new($"{entity} {id} was not found.");
}

/// <summary>409: the request conflicts with current state (duplicate SKU, insufficient stock...).</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>429: the caller must wait (for example, their previous AI question is still being answered).</summary>
public sealed class TooManyRequestsException(string message) : AppException(message);
