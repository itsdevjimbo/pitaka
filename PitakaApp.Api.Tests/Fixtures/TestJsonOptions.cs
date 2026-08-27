using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitakaApp.Api.Tests.Fixtures;

public static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new JsonStringEnumConverter() },

        PropertyNameCaseInsensitive = true
    };
}