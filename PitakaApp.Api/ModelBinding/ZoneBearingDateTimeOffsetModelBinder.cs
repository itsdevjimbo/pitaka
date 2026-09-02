using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PitakaApp.Api.ModelBinding;

/// <summary>
/// Binds a <see cref="DateTimeOffset"/>? and refuses a value that does not carry its own zone
/// designator. The default binder reads <c>2026-09-01T00:00:00</c> as a local time in whatever
/// zone the server runs in — a different answer in the container than on a developer's machine,
/// from the same request. The raw text is the only place the designator still exists, so the
/// guard lives here rather than in validation. Two distinct rejections: a value that is not a
/// timestamp at all, and a timestamp with no designator. See ADR 0005.
/// </summary>
public sealed partial class ZoneBearingDateTimeOffsetModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var modelName = bindingContext.ModelName;
        var value = bindingContext.ValueProvider.GetValue(modelName);

        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask; // absent: the bound is optional, leave it null
        }

        bindingContext.ModelState.SetModelValue(modelName, value);

        var raw = value.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.CompletedTask;
        }

        // Lower-cased to read like the query key and match the DataAnnotations messages
        // beside it ("from must be strictly earlier than to").
        var field = bindingContext.FieldName.ToLowerInvariant();

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            bindingContext.ModelState.TryAddModelError(
                modelName,
                $"{field} must be an ISO-8601 timestamp (e.g. '2026-09-01T00:00:00+08:00').");
            return Task.CompletedTask;
        }

        if (!ZoneDesignator().IsMatch(raw))
        {
            bindingContext.ModelState.TryAddModelError(
                modelName,
                $"{field} must carry a zone designator — a trailing 'Z' or '±HH:MM' (e.g. '2026-09-01T00:00:00+08:00').");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(parsed);
        return Task.CompletedTask;
    }

    // A trailing 'Z', or a signed four-digit offset (the colon is optional), at the very end
    // of the string. A bare date or date-time has neither.
    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex ZoneDesignator();
}
