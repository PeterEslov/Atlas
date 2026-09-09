using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Atlas.Api.OpenApi;

/// <summary>
/// Marks every OpenAPI operation whose endpoint does NOT carry
/// <c>[AllowAnonymous]</c> as requiring the "Bearer" security scheme
/// registered by <see cref="BearerSecuritySchemeTransformer"/>.
///
/// Why this exists as a separate step: registering a security scheme in
/// <c>components.securitySchemes</c> only makes it available for operations to
/// reference — it does not, by itself, apply it to anything. Per the OpenAPI
/// spec, an operation with no <c>security</c> array is simply undocumented as
/// needing auth, and Swagger UI honors that literally: it will NOT attach an
/// Authorization header to a request unless that request's operation lists
/// the scheme. So without this transformer, [Authorize] genuinely protects the
/// endpoint at runtime, but Swagger UI has no way to know that from the
/// document alone, and silently sends every request unauthenticated — which is
/// exactly the "401 with no Authorization header at all" symptom this fixes.
///
/// <c>AllowAnonymousAttribute</c> is used as the exclusion signal (rather than
/// looking for <c>[Authorize]</c> as the inclusion signal) so a controller-wide
/// <c>[Authorize]</c> like the one on TicketsController is respected without
/// having to enumerate every action; only AuthController's two endpoints
/// (register, login) carry <c>[AllowAnonymous]</c> today and are the only ones
/// this transformer leaves unmarked.
/// </summary>
internal sealed class AuthorizedOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var isAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<AllowAnonymousAttribute>()
            .Any();

        if (isAnonymous)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
