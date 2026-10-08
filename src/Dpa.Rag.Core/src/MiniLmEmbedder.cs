using System.Text;
using Microsoft.ML.Tokenizers;
using BertTokenizer = Microsoft.ML.Tokenizers.BertTokenizer;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Dpa.Rag.Core;

public sealed class MiniLmEmbedder : IDisposable
{
    private const int SequenceLength = 256;
    private const int Dimensions = 384;

    private static readonly int[] InputIds = [0];
    private static readonly int[] AttentionMask = [1];
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    public MiniLmEmbedder(string modelPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(
            Path.Combine(Path.GetDirectoryName(modelPath)!, "vocab.txt"),
            new BertOptions { LowerCaseBeforeTokenization = true });
    }

    public float[] Embed(string text)
    {
        var encoded = _tokenizer.EncodeToIds(text, addSpecialTokens: true);
        var ids = Pad(encoded);
        var mask = BuildMask(encoded.Count);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, new[] { 1, ids.Length })),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, new[] { 1, mask.Length })),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(Zeros(ids.Length), new[] { 1, ids.Length })),
        };

        using var results = _session.Run(inputs);
        var hidden = results.First().AsTensor<float>();
        return MeanPool(hidden, mask);
    }

    private static long[] Pad(IReadOnlyList<int> encoded)
    {
        var ids = new long[SequenceLength];
        for (var i = 0; i < SequenceLength; i++)
        {
            ids[i] = i < encoded.Count ? encoded[i] : 0;
        }

        return ids;
    }

    private static long[] Zeros(int length)
    {
        var values = new long[length];
        return values;
    }

    private static long[] BuildMask(int real)
    {
        var mask = new long[SequenceLength];
        for (var i = 0; i < SequenceLength; i++)
        {
            mask[i] = i < real ? 1 : 0;
        }

        return mask;
    }

    private static float[] MeanPool(Tensor<float> hidden, long[] mask)
    {
        var sum = new float[Dimensions];
        var counted = 0;

        for (var token = 0; token < mask.Length; token++)
        {
            if (mask[token] == 0)
            {
                continue;
            }

            for (var d = 0; d < Dimensions; d++)
            {
                sum[d] += hidden[0, token, d];
            }

            counted++;
        }

        if (counted == 0)
        {
            return sum;
        }

        var norm = 0f;
        for (var d = 0; d < Dimensions; d++)
        {
            sum[d] /= counted;
            norm += sum[d] * sum[d];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 0f)
        {
            for (var d = 0; d < Dimensions; d++)
            {
                sum[d] /= norm;
            }
        }

        return sum;
    }

    public void Dispose() => _session.Dispose();
}

public static class Cosine
{
    public static double Similarity(float[] left, float[] right)
    {
        var dot = 0.0;
        for (var i = 0; i < left.Length && i < right.Length; i++)
        {
            dot += left[i] * right[i];
        }

        return dot;
    }
}

public static class TextShape
{
    public static string Flatten(string title, string body)
    {
        var builder = new StringBuilder(body.Length + title.Length + 8);
        builder.Append(title).Append(". ").Append(body);
        return builder.ToString();
    }
}
