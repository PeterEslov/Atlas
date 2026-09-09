using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Atlas.Api.OpenApi;

/// <summary>
/// Registers a "Bearer" security scheme on the generated OpenAPI document, which
/// is what makes Swagger UI show an <c>Authorize</c> button at all — without at
/// least one entry in <c>components.securitySchemes</c>, Swagger UI has nothing
/// to build that button for. The scheme name here ("Bearer") matches the name
/// <c>AddJwtBearer(JwtBearerDefaults.AuthenticationScheme)</c> registers in
/// Program.cs, which is just a naming convention we're following, not something
/// ASP.NET Core resolves automatically (see the correction below).
///
/// Registering the scheme here is only half the job — see
/// <see cref="AuthorizedOperationTransformer"/> for the other half. Defining a
/// security scheme in <c>components.securitySchemes</c> makes Swagger UI *show*
/// the Authorize button, but it does NOT by itself tell Swagger UI which
/// operations actually need that scheme. Without a per-operation
/// <c>security</c> requirement, Swagger UI never attaches the Authorization
/// header to any request — confirmed the hard way on 2026-09-09: entering a
/// (perfectly valid, independently verified) token into Authorize still
/// produced requests with no Authorization header at all in the browser's
/// Network tab, because no operation declared that it required "Bearer".
/// Earlier docs comments in this project claimed ASP.NET Core does this
/// matching automatically based on the scheme name — that turned out to be
/// wrong; it's a known open gap in Microsoft.AspNetCore.OpenApi as of .NET 10,
/// worked around here exactly the way Microsoft's own "Customize OpenAPI
/// documents" doc recommends (see AuthorizedOperationTransformer).
///
/// This is the Microsoft.AspNetCore.OpenApi-native pattern — deliberately not
/// Swashbuckle's SwaggerGen-era AddSecurityDefinition/AddSecurityRequirement
/// API, which doesn't apply here since only Swashbuckle's UI package is
/// referenced (see the comment on AddOpenApi in Program.cs for why).
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(scheme => scheme.Name == "Bearer"))
        {
            var bearerScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "Paste just the JWT itself here — no \"Bearer \" prefix, Swagger UI adds that for you."
            };

            // AddComponent (rather than assigning document.Components.SecuritySchemes
            // directly) is the API that actually makes the scheme resolvable by name
            // from OpenApiSecuritySchemeReference below — a plain dictionary
            // assignment was found to serialize a security scheme that looked right
            // in the raw JSON but that references couldn't reliably resolve against
            // in this Microsoft.OpenApi 2.x line.
            document.Components ??= new OpenApiComponents();
            document.AddComponent("Bearer", bearerScheme);
        }
    }
}
