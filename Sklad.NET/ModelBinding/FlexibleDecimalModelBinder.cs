using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace Sklad.ModelBinding;

/// <summary>
/// Culture-independent decimal binding: both '.' and ',' are accepted as the
/// decimal mark, and group separators are rejected. Culture-aware binding is a
/// trap here — under bg-BG a dot fails to parse, while under en-GB "189,99"
/// parses as 18999 (misplaced group separators are tolerated by .NET).
/// </summary>
public class FlexibleDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        var normalized = raw.Replace(',', '.');
        if (normalized.Count(c => c == '.') <= 1 &&
            decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            var l = bindingContext.HttpContext.RequestServices
                .GetRequiredService<IStringLocalizer<SharedResource>>();
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName, l["Enter a valid number (e.g. 189.99)."]);
        }
        return Task.CompletedTask;
    }
}

public class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type == typeof(decimal) ? new FlexibleDecimalModelBinder() : null;
    }
}
