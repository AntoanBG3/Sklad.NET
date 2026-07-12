using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Extensions.Localization;

namespace Sklad.Localization;

/// <summary>
/// Gives the built-in validation attributes stable resource keys when an
/// attribute did not provide its own message. ASP.NET Core only consults its
/// data-annotation localizer when ErrorMessage is set; without this bridge the
/// default framework text stays English even when the display name is Bulgarian.
/// </summary>
public sealed class LocalizedValidationAttributeAdapterProvider : IValidationAttributeAdapterProvider
{
    private readonly ValidationAttributeAdapterProvider _fallback = new();

    public IAttributeAdapter? GetAttributeAdapter(
        ValidationAttribute attribute,
        IStringLocalizer? stringLocalizer)
    {
        if (attribute.ErrorMessage is null &&
            attribute.ErrorMessageResourceName is null &&
            attribute.ErrorMessageResourceType is null)
        {
            attribute.ErrorMessage = attribute switch
            {
                RequiredAttribute => "The {0} field is required.",
                RangeAttribute => "The field {0} must be between {1} and {2}.",
                StringLengthAttribute { MinimumLength: > 0 } =>
                    "The field {0} must be a string with a minimum length of {2} and a maximum length of {1}.",
                StringLengthAttribute =>
                    "The field {0} must be a string with a maximum length of {1}.",
                MinLengthAttribute =>
                    "The field {0} must be a string or array type with a minimum length of '{1}'.",
                EmailAddressAttribute => "The {0} field is not a valid e-mail address.",
                _ => null
            };
        }

        return _fallback.GetAttributeAdapter(attribute, stringLocalizer);
    }
}
