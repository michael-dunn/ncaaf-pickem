using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// What the service worker receives (Feature 11). Serialized as
/// <c>{ "title", "body", "url", "tag" }</c> — the exact shape <c>service-worker.js</c> reads in
/// its <c>push</c> handler, so the property names here are part of the P7-02 contract.
/// </summary>
/// <param name="Title">Notification title.</param>
/// <param name="Body">Notification body. Never contains another member's picks (Feature 11).</param>
/// <param name="Url">Relative app path the notification opens, e.g. <c>/leagues/{id}/picks</c>.</param>
/// <param name="Tag">
/// Collapse key. Two notifications with the same tag replace each other on the device, which is
/// how a second reminder never stacks on top of the first.
/// </param>
public sealed record PushPayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("tag")] string Tag)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The JSON body that is encrypted and sent to the push service.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Reads back what <see cref="ToJson"/> wrote (used when a retry is replayed).</summary>
    /// <param name="json">A payload written by <see cref="ToJson"/>.</param>
    public static PushPayload FromJson(string json) =>
        JsonSerializer.Deserialize<PushPayload>(json, SerializerOptions)
        ?? throw new JsonException("A stored push payload deserialized to null.");
}
