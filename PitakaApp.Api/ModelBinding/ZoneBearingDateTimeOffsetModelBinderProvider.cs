using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PitakaApp.Api.ModelBinding;

/// <summary>
/// Routes every <see cref="DateTimeOffset"/> bound from the query string, route, or a form to
/// <see cref="ZoneBearingDateTimeOffsetModelBinder"/>, which refuses a value with no zone
/// designator instead of quietly reading it in the server's own zone. JSON bodies are
/// unaffected — they are System.Text.Json's, not MVC's. Registered in Program.cs; remove that
/// line and <c>from</c>/<c>to</c> on GET /api/transactions fall back to the permissive default.
/// See ADR 0005.
/// </summary>
public sealed class ZoneBearingDateTimeOffsetModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.ModelType;

        return type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?)
            ? new ZoneBearingDateTimeOffsetModelBinder()
            : null;
    }
}
