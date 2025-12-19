using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using SemanticPacker.Core.Contracts;

namespace SemanticPacker.Core.Services;

/// <summary>
/// BGE-M3 ONNX embedding service
/// </summary>
public class BgeM3EmbeddingService : IEmbeddingService
{
    private readonly InferenceSession _session;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly ILogger<BgeM3EmbeddingService> _logger;
    private readonly int _maxLength = 8192;

    public BgeM3EmbeddingService(IConfiguration configuration, ILogger<BgeM3EmbeddingService> logger, string modelVariant = "sentence_transformers_quantized.onnx")
    {
        // AOT compatible: use indexer + null check instead of GetValue<T>()
        string? modelPath = configuration["SemanticPacker:BgeM3ModelPath"];
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("SemanticPacker:BgeM3ModelPath configuration is required.");
        
        _logger = logger;

        var onnxPath = Path.Combine(modelPath, "onnx", modelVariant);
        var tokenizerPath = Path.Combine(modelPath, "sentencepiece.bpe.model");

        _logger.LogInformation("Loading ONNX model from: {Path}", onnxPath);
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_PARALLEL
        };
        _session = new InferenceSession(onnxPath, sessionOptions);

        _logger.LogInformation("Loading tokenizer from: {Path}", tokenizerPath);
        using var tokenizerStream = File.OpenRead(tokenizerPath);
        _tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: false, addEndOfSentence: false);

        LogModelInfo();
    }

    private void LogModelInfo()
    {
        _logger.LogDebug("Model inputs: {Inputs}", string.Join(", ", _session.InputMetadata.Keys));
        _logger.LogDebug("Model outputs: {Outputs}", string.Join(", ", _session.OutputMetadata.Keys));
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GenerateEmbedding(text), cancellationToken);
    }

    public Task<float[][]> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => texts.Select(GenerateEmbedding).ToArray(), cancellationToken);
    }

    private float[] GenerateEmbedding(string text)
    {
        var tokenIds = _tokenizer.EncodeToIds(text);
        var inputIds = tokenIds.Select(id => (long)id).ToArray();
        var attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();

        if (inputIds.Length > _maxLength)
        {
            inputIds = inputIds[^_maxLength..];
            attentionMask = attentionMask[^_maxLength..];
        }

        var batchSize = 1;
        var seqLength = inputIds.Length;

        var inputIdsTensor = new DenseTensor<long>(inputIds, new ReadOnlySpan<int>([batchSize, seqLength]));
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, new ReadOnlySpan<int>([batchSize, seqLength]));

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        if (_session.InputMetadata.ContainsKey("token_type_ids"))
        {
            var tokenTypeIds = new long[seqLength];
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new ReadOnlySpan<int>([batchSize, seqLength]));
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor));
        }

        using var results = _session.Run(inputs);

        var output = results.FirstOrDefault(r => r.Name == "sentence_embedding")
                  ?? results.FirstOrDefault(r => r.Name == "last_hidden_state")
                  ?? results.First();

        var tensor = output.AsTensor<float>();

        if (output.Name == "sentence_embedding")
        {
            return tensor.ToArray();
        }
        else
        {
            return MeanPooling(tensor, attentionMask, seqLength);
        }
    }

    private static float[] MeanPooling(Tensor<float> hiddenStates, long[] attentionMask, int seqLength)
    {
        var dims = hiddenStates.Dimensions.ToArray();
        var hiddenSize = dims[^1];
        var embedding = new float[hiddenSize];
        var maskSum = attentionMask.Sum();

        for (int i = 0; i < seqLength; i++)
        {
            if (attentionMask[i] == 0) continue;
            for (int j = 0; j < hiddenSize; j++)
            {
                embedding[j] += hiddenStates[0, i, j];
            }
        }

        for (int j = 0; j < hiddenSize; j++)
        {
            embedding[j] /= maskSum;
        }

        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        for (int j = 0; j < hiddenSize; j++)
        {
            embedding[j] /= norm;
        }

        return embedding;
    }

    public void Dispose()
    {
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}
