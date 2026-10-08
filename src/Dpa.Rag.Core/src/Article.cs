using System.Text.Json.Serialization;

namespace Dpa.Rag.Core;

public sealed record Article(
    [property: JsonPropertyName("number")] string Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text)
{
    [JsonIgnore]
    public string Citation => $"Section {Number}";
}
