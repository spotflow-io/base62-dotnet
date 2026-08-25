using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

using Ghost1faceBase62 = global::Base62.Base62Converter;
using SimpleBase62 = SimpleBase.Base62;
using SpotflowBase62 = Spotflow.Base62.Base62;

namespace Spotflow.Base62.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class Base62Benchmarks
{
    private readonly Ghost1faceBase62 _ghost1faceBase62 = new();
    private byte[] _data = null!;
    private byte[] _spotflowEncoded = null!;
    private byte[] _spotflowDecoded = null!;
    private string _simpleBaseEncoded = null!;
    private byte[] _simpleBaseDecoded = null!;
    private byte[] _ghost1faceEncoded = null!;

    [Params(64, 128, 1024, 4096)]
    public int ByteCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[ByteCount];
        new Random(42).NextBytes(_data);

        _spotflowEncoded = new byte[SpotflowBase62.GetEncodedLength(_data.Length)];
        _spotflowDecoded = new byte[_data.Length];

        _simpleBaseEncoded = SimpleBase62.Default.Encode(_data);
        _simpleBaseDecoded = new byte[SimpleBase62.Default.GetSafeByteCountForDecoding(_simpleBaseEncoded)];

        _ghost1faceEncoded = _ghost1faceBase62.Encode(_data);

        SpotflowBase62.EncodeToUtf8(_data, _spotflowEncoded);
        SpotflowBase62.DecodeFromUtf8(_spotflowEncoded, _spotflowDecoded);
        var simpleBaseSucceeded = SimpleBase62.Default.TryDecode(_simpleBaseEncoded, _simpleBaseDecoded, out var simpleBaseDecodedCount);
        var ghost1faceDecoded = _ghost1faceBase62.Decode(_ghost1faceEncoded);

        if (!_data.AsSpan().SequenceEqual(_spotflowDecoded)
            || !simpleBaseSucceeded
            || !_data.AsSpan().SequenceEqual(_simpleBaseDecoded.AsSpan(0, simpleBaseDecodedCount))
            || !_data.AsSpan().SequenceEqual(ghost1faceDecoded))
        {
            throw new InvalidOperationException("A benchmark implementation failed to round-trip the input data.");
        }
    }

    [Benchmark(Baseline = true, Description = "Spotflow.Base62")]
    [BenchmarkCategory("Encode")]
    public byte[] SpotflowEncode()
    {
        SpotflowBase62.EncodeToUtf8(_data, _spotflowEncoded);
        return _spotflowEncoded;
    }

    [Benchmark(Description = "SimpleBase")]
    [BenchmarkCategory("Encode")]
    public string SimpleBaseEncode() => SimpleBase62.Default.Encode(_data);

    [Benchmark(Description = "ghost1face/base62")]
    [BenchmarkCategory("Encode")]
    public byte[] Ghost1faceEncode() => _ghost1faceBase62.Encode(_data);

    [Benchmark(Baseline = true, Description = "Spotflow.Base62")]
    [BenchmarkCategory("Decode")]
    public byte[] SpotflowDecode()
    {
        SpotflowBase62.DecodeFromUtf8(_spotflowEncoded, _spotflowDecoded);
        return _spotflowDecoded;
    }

    [Benchmark(Description = "SimpleBase")]
    [BenchmarkCategory("Decode")]
    public byte[] SimpleBaseDecode()
    {
        SimpleBase62.Default.TryDecode(_simpleBaseEncoded, _simpleBaseDecoded, out _);
        return _simpleBaseDecoded;
    }

    [Benchmark(Description = "ghost1face/base62")]
    [BenchmarkCategory("Decode")]
    public byte[] Ghost1faceDecode() => _ghost1faceBase62.Decode(_ghost1faceEncoded);
}
