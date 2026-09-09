using System.Text;
using Atlas.Api.Middleware;
using Atlas.Api.OpenApi;
using Atlas.Api.Services;
using Atlas.Application.Auth.Services;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Organizations.Services;
using Atlas.Application.Projects.Services;
using Atlas.Application.Teams.Services;
using Atlas.Application.Tickets.Services;
using Atlas.Application.Users.Services;
using Atlas.Domain.Security;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Services -------------------------------------------------------------

builder.Services.AddControllers();

// Generates /openapi/v1.json using ASP.NET Core's built-in OpenAPI support —
// no Swashbuckle SwaggerGen involved. UseSwaggerUI below just needs that URL.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Project Atlas API";
        document.Info.Version = "v1";
        document.Info.Description = "Ticket & operations management platform for Northstar IT.";
        return Task.CompletedTask;
    });

    // Adds the "Bearer" security scheme so Swagger UI shows an Authorize
    // button (Del 5) — see BearerSecuritySchemeTransformer for details.
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

    // Marks every non-anonymous operation as requiring that scheme — without
    // this, Swagger UI's Authorize button exists but never actually attaches
    // the token to requests. See AuthorizedOperationTransformer for the full
    // story (confirmed live via the browser Network tab on 2026-09-09).
    options.AddOperationTransformer<AuthorizedOperationTransformer>();
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ITeamService, TeamService>();

builder.Services.AddCors(options =>
{
    // Permissive policy for local development only (e.g. a future React dev
    // server on http://localhost:5173, see Del 21). Tightened before any real
    // deployment — see Del 8/19 for the Azure App Service configuration.
    options.AddPolicy("AllowLocalDev", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ---- Authentication & authorization (Del 5) --------------------------------
//
// JwtSettings is bound eagerly here (not just via IOptions in DI) because the
// signing key has to be turned into a SymmetricSecurityKey right now, while
// building TokenValidationParameters. AddInfrastructure above also registers
// IOptions<JwtSettings> for JwtTokenGenerator's own use when issuing tokens.
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "Configuration section 'Jwt' was not found. Set it in appsettings.Development.json " +
        "(local signing key) or, in Azure, via Key Vault (Del 20).");

if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey) || Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or too short (must be at least 32 bytes / 256 bits for HS256). " +
        "Set a real value in appsettings.Development.json for local dev, or in Key Vault for Azure (Del 20).");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the JWT handler silently remaps well-known claim types
        // (e.g. "sub" -> ClaimTypes.NameIdentifier) using its default inbound
        // claim map, which would break every FindFirst("sub")/("email") lookup
        // in CurrentUserService and AuthController against the raw claim types
        // JwtTokenGenerator actually issues.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // One policy per permission, each satisfied by a matching "permission"
    // claim (see RolePermissions / JwtTokenGenerator). Controllers apply
    // these with [Authorize(Policy = Permissions.TicketCreate)] etc. rather
    // than the coarser built-in role-based [Authorize(Roles = ...)].
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
    }
});

var app = builder.Build();

// ---- Middleware pipeline ----------------------------------------------------

app.UseAtlasExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Project Atlas API v1"));

    // Convenience only: applies pending EF Core migrations automatically on
    // startup so a fresh clone works with just `dotnet run`. Never do this
    // against Azure SQL in production — deploy migrations explicitly (Del 8/18).
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseCors("AllowLocalDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests (Del 16).
public partial class Program
{
}
