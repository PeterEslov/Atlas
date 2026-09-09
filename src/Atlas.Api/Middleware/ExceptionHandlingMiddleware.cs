using System.Net;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Middleware;

/// <summary>
/// Central place that turns exceptions into RFC 7807 ProblemDetails responses,
/// so controllers stay free of try/catch and every error the API returns has a
/// consistent shape. <see cref="DomainException"/> (broken business rule) maps to
/// 400, <see cref="NotFoundException"/> maps to 404,
/// <see cref="AuthenticationException"/> (bad credentials, Del 5) maps to 401,
/// everything else is logged as an unexpected error and returned as a 500
/// without leaking internals.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.BadRequest, "Business rule violation", ex.Message);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.NotFound, "Not found", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            await WriteProblemAsync(context, HttpStatusCode.Unauthorized, "Authentication failed", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred", "Please try again or contact support if the problem persists.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, HttpStatusCode statusCode, string title, string detail)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseAtlasExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
