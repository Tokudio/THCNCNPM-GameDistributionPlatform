using System.IdentityModel.Tokens.Jwt;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using GameDistributionPlatform.Application.Interfaces;
using GameDistributionPlatform.Infrastructure.Data;
using GameDistributionPlatform.Infrastructure.Data.Seeding;
using GameDistributionPlatform.Infrastructure.Identity;
using GameDistributionPlatform.Infrastructure.Options;
using GameDistributionPlatform.Infrastructure.Services;
using GameDistributionPlatform.WebApi.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container and configure model validation response
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = context.HttpContext.Request.Path,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        };

        problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        var result = new BadRequestObjectResult(problemDetails);
        result.ContentTypes.Add("application/problem+json");

        return result;
    };
});

// Configure ProblemDetails and GlobalExceptionHandler
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Configure DbContext with SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure ASP.NET Core Identity Core for ApplicationUser & IdentityRole<Guid>
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>();

// Configure JWT Options and ValidationOnStart
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();

// Register Application & Infrastructure Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Configure JWT Authentication
var jwtConfig = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException($"Configuration section '{JwtOptions.SectionName}' is missing.");
var signingKeyBytes = jwtConfig.GetSigningKeyBytes();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = false;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtConfig.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtConfig.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "name",
        RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdString = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                context.Fail("Missing or invalid 'sub' claim.");
                return;
            }

            context.HttpContext.RequestAborted.ThrowIfCancellationRequested();

            var services = context.HttpContext.RequestServices;

            try
            {
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByIdAsync(userId.ToString());
                context.HttpContext.RequestAborted.ThrowIfCancellationRequested();

                if (user == null || !user.IsActive)
                {
                    context.Fail("User account is inactive or not found.");
                    return;
                }

                var roles = await userManager.GetRolesAsync(user);
                context.HttpContext.RequestAborted.ThrowIfCancellationRequested();

                if (context.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
                {
                    throw new InvalidOperationException("The validated JWT principal does not contain an authenticated identity.");
                }

                foreach (var roleClaim in identity.FindAll("role").ToArray())
                {
                    identity.RemoveClaim(roleClaim);
                }

                foreach (var role in roles)
                {
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        identity.AddClaim(new Claim("role", role));
                    }
                }
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new JwtUserLookupException("An infrastructure error occurred while validating user authentication data in database.", ex);
            }
            catch (Exception ex)
            {
                throw new JwtUserLookupException("An infrastructure error occurred while validating user authentication data in database.", ex);
            }
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is JwtUserLookupException)
            {
                ExceptionDispatchInfo.Capture(context.Exception).Throw();
            }

            if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                ExceptionDispatchInfo.Capture(context.Exception).Throw();
            }

            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            if (context.AuthenticateFailure is JwtUserLookupException)
            {
                ExceptionDispatchInfo.Capture(context.AuthenticateFailure).Throw();
            }

            if (context.Response.HasStarted || context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = JwtBearerDefaults.AuthenticationScheme;

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Authentication is required to access this resource. Please provide a valid Bearer token.",
                Instance = context.Request.Path,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            };
            problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            var problemDetailsService = context.HttpContext.RequestServices.GetService<IProblemDetailsService>();
            var written = false;

            if (problemDetailsService != null)
            {
                written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = problemDetails
                });
            }

            if (!written)
            {
                await context.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            }
        },
        OnForbidden = async context =>
        {
            if (context.Response.HasStarted || context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "You do not have permission to access this resource.",
                Instance = context.Request.Path,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            };
            problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            var problemDetailsService = context.HttpContext.RequestServices.GetService<IProblemDetailsService>();
            var written = false;

            if (problemDetailsService != null)
            {
                written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = problemDetails
                });
            }

            if (!written)
            {
                await context.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            }
        }
    };
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Nhập JWT AccessToken nhận được từ Login API."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.Any(item => item is IAllowAnonymous) || !metadata.Any(item => item is IAuthorizeData))
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = JwtBearerDefaults.AuthenticationScheme
                }
            }] = []
        });

        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Validate JWT configuration before executing Database Seeding
var jwtValidator = new JwtOptionsValidator();
var validationResult = jwtValidator.Validate(null, jwtConfig);
if (validationResult.Failed)
{
    throw new OptionsValidationException(
        JwtOptions.SectionName,
        typeof(JwtOptions),
        validationResult.Failures);
}

// Execute Database Seeding within an isolated IServiceScope
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        await DatabaseSeeder.SeedAsync(services, builder.Configuration);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "A critical error occurred during database seeding.");
        throw; // Fail-fast policy: Do not silently continue if seeding fails
    }
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Configure OpenAPI and Swagger UI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "GameDistributionPlatform API v1");

        options.RoutePrefix = "swagger";
        options.DocumentTitle = "GameDistributionPlatform API";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }