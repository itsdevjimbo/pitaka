using System.Text.RegularExpressions;

namespace PitakaApp.Api.Infra;

public partial class SlugifyParameterTransformer : IOutboundParameterTransformer
{
    [GeneratedRegex(@"([a-z])([A-Z])", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex PascalCaseRegex();
    public string? TransformOutbound(object? value)
    {
        if (value == null) return null;
        return PascalCaseRegex().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();
    }
}