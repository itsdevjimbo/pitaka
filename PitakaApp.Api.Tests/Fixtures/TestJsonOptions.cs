namespace PitakaApp.Api.Tests.Fixtures;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new JsonStringEnumConverter() },

        PropertyNameCaseInsensitive = true
    };
}