using GameDistributionPlatform.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GameDistributionPlatform.WebApi.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                LogAbortedRequestException(exception);
                return false;
            }

            _logger.LogWarning("The response has already started, GlobalExceptionHandler will not execute.");
            return false;
        }

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            LogAbortedRequestException(exception);
            return true;
        }

        var (statusCode, title, detail, typeUri) = MapException(exception);

        LogException(exception, statusCode, "Unhandled server error occurred.");

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = typeUri
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;

        var written = await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });

        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }

        return true;
    }

    private void LogAbortedRequestException(Exception exception)
    {
        if (exception is OperationCanceledException or JwtUserLookupException)
        {
            return;
        }

        var (statusCode, _, _, _) = MapException(exception);
        LogException(exception, statusCode, "An exception occurred after the request was aborted.");
    }

    private void LogException(Exception exception, int statusCode, string serverErrorMessage)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            if (exception is not JwtUserLookupException)
            {
                _logger.LogError(exception, serverErrorMessage);
            }

            return;
        }

        _logger.LogWarning("Handled business exception [{StatusCode}]: {Message}", statusCode, exception.Message);
    }

    private static (int StatusCode, string Title, string Detail, string TypeUri) MapException(Exception exception)
    {
        return exception switch
        {
            BadRequestException badRequest => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                badRequest.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            ),
            UnauthorizedException unauthorized => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                unauthorized.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            ),
            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                "Conflict",
                conflict.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                "Not Found",
                notFound.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            ),
            InternalServerException => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An internal server error occurred. Please contact support.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            ),
            JwtUserLookupException => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An internal server error occurred. Please contact support.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An internal server error occurred. Please contact support.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            )
        };
    }
}
