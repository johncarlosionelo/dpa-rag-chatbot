using Google.Protobuf.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Dpa.Rag.Core;

public sealed record VectorHit(string Number, string Title, string Text, double Score);

public sealed class VectorStoreOptions
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 6334;

    public string Collection { get; init; } = "dpa_sections";

    public string KeyEnv { get; init; } = "QDRANT_API_KEY";

    public string ApiKey { get; init; } = string.Empty;
}

public interface IVectorStore : IDisposable
{
    string Backend { get; }

    Task EnsureCollectionAsync(int dimensions, CancellationToken ct);

    Task UpsertAsync(IReadOnlyList<EmbeddedSection> sections, CancellationToken ct);

    Task<IReadOnlyList<VectorHit>> SearchAsync(float[] vector, int take, CancellationToken ct);

    Task<EmbeddedSection?> GetAsync(string number, CancellationToken ct);
}

public sealed class QdrantVectorStore : IVectorStore, IDisposable
{
    private readonly QdrantClient _client;
    private readonly VectorStoreOptions _options;
    private bool _disposed;

    public QdrantVectorStore(VectorStoreOptions options)
    {
        _options = options;
        _client = new QdrantClient(
            options.Host,
            options.Port,
            https: false,
            apiKey: string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey);
    }

    public string Backend => $"qdrant {_options.Host}:{_options.Port} / {_options.Collection}";

    public async Task EnsureCollectionAsync(int dimensions, CancellationToken ct)
    {
        if (await _client.CollectionExistsAsync(_options.Collection, ct))
        {
            return;
        }

        await _client.CreateCollectionAsync(
            _options.Collection,
            new VectorParams
            {
                Size = (uint)dimensions,
                Distance = Distance.Cosine,
            },
            cancellationToken: ct);
    }

    public async Task UpsertAsync(IReadOnlyList<EmbeddedSection> sections, CancellationToken ct)
    {
        var points = new List<PointStruct>(sections.Count);

        foreach (var section in sections)
        {
            points.Add(new PointStruct
            {
                Id = new PointId(PointKey(section.Number)),
                Vectors = section.Vector,
                Payload =
                {
                    ["number"] = section.Number,
                    ["title"] = section.Title,
                    ["text"] = section.Text,
                },
            });
        }

        await _client.UpsertAsync(_options.Collection, points, wait: true, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] vector, int take, CancellationToken ct)
    {
        var result = await _client.QueryAsync(
            _options.Collection,
            query: new Query { Nearest = vector },
            limit: (ulong)take,
            payloadSelector: new WithPayloadSelector { Enable = true },
            cancellationToken: ct);

        var hits = new List<VectorHit>(result.Count);

        foreach (var point in result)
        {
            if (TryRead(point.Payload, out var hit))
            {
                hits.Add(hit with { Score = point.Score });
            }
        }

        return hits;
    }

    public async Task<EmbeddedSection?> GetAsync(string number, CancellationToken ct)
    {
        var found = await _client.RetrieveAsync(
            _options.Collection,
            new PointId(PointKey(number)),
            withPayload: true,
            withVectors: true,
            cancellationToken: ct);

        foreach (var point in found)
        {
            if (TryRead(point.Payload, out var hit))
            {
                return new EmbeddedSection(hit.Number, hit.Title, hit.Text, ToVector(point));
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _client.Dispose();
        _disposed = true;
    }

    private static bool TryRead(MapField<string, Value> payload, out VectorHit hit)
    {
        hit = default!;

        if (payload is null)
        {
            return false;
        }

        if (!payload.TryGetValue("number", out var number) ||
            !payload.TryGetValue("title", out var title) ||
            !payload.TryGetValue("text", out var text))
        {
            return false;
        }

        var value = Read(number);
        var name = Read(title);
        var body = Read(text);

        if (value.Length == 0)
        {
            return false;
        }

        hit = new VectorHit(value, name, body, 0.0);

        return true;
    }

    private static string Read(Value? value) => value switch
    {
        null => string.Empty,
        { KindCase: Value.KindOneofCase.StringValue } => value.StringValue,
        _ => value.ToString(),
    };

    private static float[] ToVector(RetrievedPoint point) =>
        point.Vectors?.Vector?.Dense?.Data?.ToArray() ?? [];

    private static ulong PointKey(string number) =>
        ulong.TryParse(number, out var parsed) ? parsed : StableHash(number);

    private static ulong StableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
