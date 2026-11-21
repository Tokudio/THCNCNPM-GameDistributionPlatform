using System.ComponentModel.DataAnnotations;
using GameDistributionPlatform.Application.Exceptions;

namespace GameDistributionPlatform.Application.DTOs;

public static class CatalogInput
{
    public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    public const string MaximumPrice = "9999999999999999.99";

    public static Uri? WebUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? uri : null;

    public static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
        {
            throw new BadRequestException(string.Join(" ", errors.Select(error => error.ErrorMessage)));
        }
    }
}
