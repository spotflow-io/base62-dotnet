using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Spotflow.Base62.Tests;

[TestClass]
public sealed class Base62Tests
{
    [TestMethod]
    [DataRow("", "")]
    [DataRow("00", "00")]
    [DataRow("01", "10")]
    [DataRow("3D", "z0")]
    [DataRow("3E", "01")]
    [DataRow("0001", "840")]
    [DataRow("FFFF", "13H")]
    [DataRow("FFFFFF", "FWO81")]
    [DataRow("FFFFFFFF", "3CFfg4")]
    [DataRow("FFFFFFFFFF", "VojIAMJ")]
    [DataRow("FFFFFFFFFFFF", "7aANXWvH1")]
    [DataRow("FFFFFFFFFFFFFF", "1BhfnLW1K5")]
    [DataRow("FFFFFFFFFFFFFFFF", "FYHA61aHgyL")]
    [DataRow("010203040506070809", "zipgg0YFjg090")]
    public void Character_And_Utf8_APIs_Should_Match_Known_Vectors(string inputHex, string encoded)
    {
        var input = Convert.FromHexString(inputHex);
        var encodedUtf8 = Encoding.ASCII.GetBytes(encoded);
        var charDestination = Enumerable.Repeat('\uCCCC', encoded.Length + 1).ToArray();
        var utf8Destination = Enumerable.Repeat((byte) 0xCC, encoded.Length + 1).ToArray();

        Base62.EncodeToString(input).Should().Be(encoded);
        Base62.EncodeToUtf8(input).Should().Equal(encodedUtf8);
        Base62.EncodeToChars(input, charDestination).Should().Be(encoded.Length);
        Base62.EncodeToUtf8(input, utf8Destination).Should().Be(encoded.Length);
        charDestination.AsSpan(0, encoded.Length).ToString().Should().Be(encoded);
        charDestination[^1].Should().Be('\uCCCC');
        utf8Destination.AsSpan(0, encoded.Length).ToArray().Should().Equal(encodedUtf8);
        utf8Destination[^1].Should().Be(0xCC);

        Base62.DecodeFromChars(encoded).Should().Equal(input);
        Base62.DecodeFromUtf8(encodedUtf8).Should().Equal(input);

        var charDecoded = Enumerable.Repeat((byte) 0xCC, input.Length + 1).ToArray();
        var utf8Decoded = Enumerable.Repeat((byte) 0xCC, input.Length + 1).ToArray();
        Base62.DecodeFromChars(encoded, charDecoded).Should().Be(input.Length);
        Base62.DecodeFromUtf8(encodedUtf8, utf8Decoded).Should().Be(input.Length);
        charDecoded.AsSpan(0, input.Length).ToArray().Should().Equal(input);
        utf8Decoded.AsSpan(0, input.Length).ToArray().Should().Equal(input);
        charDecoded[^1].Should().Be(0xCC);
        utf8Decoded[^1].Should().Be(0xCC);

        var inPlace = encodedUtf8.ToArray();
        Base62.DecodeFromUtf8InPlace(inPlace).Should().Be(input.Length);
        inPlace.AsSpan(0, input.Length).ToArray().Should().Equal(input);
    }

    [TestMethod]
    public void Character_And_Utf8_APIs_Should_Use_The_Published_Alphabet()
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        for (var digit = 0; digit < alphabet.Length; digit++)
        {
            var input = new[] { (byte) digit };
            var expected = $"{alphabet[digit]}0";
            var expectedUtf8 = Encoding.ASCII.GetBytes(expected);

            Base62.EncodeToString(input).Should().Be(expected);
            Base62.EncodeToUtf8(input).Should().Equal(expectedUtf8);
            Base62.DecodeFromChars(expected).Should().Equal(input);
            Base62.DecodeFromUtf8(expectedUtf8).Should().Equal(input);
        }
    }

    [TestMethod]
    public void APIs_Should_Roundtrip_Varied_Data()
    {
        const int samplesPerLength = 100;
        var random = new Random(1);

        for (var length = 0; length <= 32; length++)
        {
            for (var sample = 0; sample < samplesPerLength; sample++)
            {
                var input = new byte[length];
                random.NextBytes(input);

                var encoded = Base62.EncodeToString(input);
                var chars = new char[Base62.GetEncodedLength(length)];
                var utf8 = new byte[chars.Length];
                Base62.TryEncodeToChars(input, chars, out var charsWritten).Should().BeTrue();
                Base62.TryEncodeToUtf8(input, utf8, out var encodedBytesWritten).Should().BeTrue();

                charsWritten.Should().Be(chars.Length);
                encodedBytesWritten.Should().Be(utf8.Length);
                chars.AsSpan().ToString().Should().Be(encoded);
                Encoding.ASCII.GetString(utf8).Should().Be(encoded);

                var decodedFromChars = new byte[Base62.GetDecodedLength(chars.Length)];
                var decodedFromUtf8 = new byte[decodedFromChars.Length];
                Base62.TryDecodeFromChars(chars, decodedFromChars, out var charBytesWritten).Should().BeTrue();
                Base62.TryDecodeFromUtf8(utf8, decodedFromUtf8, out var utf8BytesWritten).Should().BeTrue();

                charBytesWritten.Should().Be(length);
                utf8BytesWritten.Should().Be(length);
                decodedFromChars.Should().Equal(input, $"the input length was {length} and the sample was {sample}");
                decodedFromUtf8.Should().Equal(input, $"the input length was {length} and the sample was {sample}");
            }
        }
    }

    [TestMethod]
    [DataRow(744)]
    [DataRow(745)]
    [DataRow(1023)]
    [DataRow(1024)]
    [DataRow(4096)]
    public void Allocating_APIs_Should_Roundtrip_Large_Data(int length)
    {
        var input = new byte[length];
        new Random(42).NextBytes(input);

        var encoded = Base62.EncodeToString(input);

        Base62.DecodeFromChars(encoded).Should().Equal(input);
        Base62.DecodeFromUtf8(Encoding.ASCII.GetBytes(encoded)).Should().Equal(input);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 2)]
    [DataRow(2, 3)]
    [DataRow(3, 5)]
    [DataRow(4, 6)]
    [DataRow(5, 7)]
    [DataRow(6, 9)]
    [DataRow(7, 10)]
    [DataRow(8, 11)]
    [DataRow(9, 13)]
    [DataRow(16, 22)]
    public void GetEncodedLength_Should_Return_The_Exact_Length(int sourceLength, int expectedEncodedLength)
    {
        Base62.GetEncodedLength(sourceLength).Should().Be(expectedEncodedLength);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(2, 1)]
    [DataRow(3, 2)]
    [DataRow(5, 3)]
    [DataRow(6, 4)]
    [DataRow(7, 5)]
    [DataRow(9, 6)]
    [DataRow(10, 7)]
    [DataRow(11, 8)]
    [DataRow(13, 9)]
    [DataRow(22, 16)]
    public void GetDecodedLength_Should_Return_The_Exact_Length(int encodedLength, int expectedDecodedLength)
    {
        Base62.GetDecodedLength(encodedLength).Should().Be(expectedDecodedLength);
    }

    [TestMethod]
    public void Length_Methods_Should_Reject_Negative_Values()
    {
        var encodeAction = () => Base62.GetEncodedLength(-1);
        var decodeAction = () => Base62.GetDecodedLength(-1);

        encodeAction.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("sourceLength");
        decodeAction.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("encodedLength");
    }

    [TestMethod]
    public void GetEncodedLength_Should_Reject_An_Unrepresentable_Result()
    {
        var action = () => Base62.GetEncodedLength(int.MaxValue);

        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("sourceLength");
    }

    [TestMethod]
    public void Allocating_Encode_APIs_Should_Reject_An_Unrepresentable_Result()
    {
        var stringAction = static () => Base62.EncodeToString(CreateOversizedSource());
        var utf8Action = static () => Base62.EncodeToUtf8(CreateOversizedSource());

        stringAction.Should().ThrowExactly<ArgumentException>().WithParameterName("source");
        utf8Action.Should().ThrowExactly<ArgumentException>().WithParameterName("source");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(12)]
    [DataRow(15)]
    [DataRow(19)]
    public void GetDecodedLength_Should_Reject_Invalid_Final_Block_Lengths(int encodedLength)
    {
        var action = () => Base62.GetDecodedLength(encodedLength);

        action.Should().ThrowExactly<FormatException>();
    }

    [TestMethod]
    public void Encode_APIs_Should_Reject_A_Destination_That_Is_Too_Small()
    {
        var input = new byte[] { 1 };
        var charAction = () => Base62.EncodeToChars(input, new char[1]);
        var utf8Action = () => Base62.EncodeToUtf8(input, new byte[1]);

        charAction.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
        utf8Action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
        Base62.TryEncodeToChars(input, new char[1], out var charsWritten).Should().BeFalse();
        Base62.TryEncodeToUtf8(input, new byte[1], out var bytesWritten).Should().BeFalse();
        charsWritten.Should().Be(0);
        bytesWritten.Should().Be(0);
    }

    [TestMethod]
    public void Decode_APIs_Should_Reject_A_Destination_That_Is_Too_Small()
    {
        var charAction = () => Base62.DecodeFromChars("10", Array.Empty<byte>());
        var utf8Action = () => Base62.DecodeFromUtf8("10"u8, Array.Empty<byte>());

        charAction.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
        utf8Action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
        Base62.TryDecodeFromChars("10", Array.Empty<byte>(), out var charBytesWritten).Should().BeFalse();
        Base62.TryDecodeFromUtf8("10"u8, Array.Empty<byte>(), out var utf8BytesWritten).Should().BeFalse();
        charBytesWritten.Should().Be(0);
        utf8BytesWritten.Should().Be(0);
    }

    [TestMethod]
    [DataRow("!0")]
    [DataRow("0_")]
    [DataRow("?0")]
    [DataRow("0é")]
    [DataRow("\0")]
    [DataRow("00000000000!0")]
    public void Character_Decoders_Should_Reject_Characters_Outside_The_Alphabet(string input)
    {
        var output = new byte[((input.Length / 11) + 1) * 8];
        var action = () => Base62.DecodeFromChars(input, output);

        action.Should().ThrowExactly<FormatException>();
        Base62.TryDecodeFromChars(input, output, out _).Should().BeFalse();
        Base62.DecodeFromChars(input, output, out _, out _).Should().Be(OperationStatus.InvalidData);
    }

    [TestMethod]
    public void Utf8_Decoders_Should_Reject_NonAscii_And_Invalid_Bytes()
    {
        var output = new byte[8];

        foreach (var input in new[] { new byte[] { 0xFF, (byte) '0' }, "0_"u8.ToArray() })
        {
            var action = () => Base62.DecodeFromUtf8(input, output);

            action.Should().ThrowExactly<FormatException>();
            Base62.TryDecodeFromUtf8(input, output, out _).Should().BeFalse();
            Base62.DecodeFromUtf8(input, output, out _, out _).Should().Be(OperationStatus.InvalidData);
        }
    }

    [TestMethod]
    [DataRow("GYHA61aHgyL")]
    [DataRow("84")]
    [DataRow("23H")]
    [DataRow("GWO81")]
    [DataRow("4CFfg4")]
    [DataRow("WojIAMJ")]
    [DataRow("8aANXWvH1")]
    [DataRow("2BhfnLW1K5")]
    public void Decoders_Should_Reject_Values_That_Exceed_Their_Decoded_Size(string input)
    {
        var output = new byte[Base62.GetDecodedLength(input.Length)];
        var utf8 = Encoding.ASCII.GetBytes(input);

        Base62.DecodeFromChars(input, output, out _, out _).Should().Be(OperationStatus.InvalidData);
        Base62.DecodeFromUtf8(utf8, output, out _, out _).Should().Be(OperationStatus.InvalidData);
        Base62.TryDecodeFromChars(input, output, out _).Should().BeFalse();
        Base62.TryDecodeFromUtf8(utf8, output, out _).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("0000")]
    [DataRow("00000000")]
    [DataRow("000000000001")]
    public void Final_Decoders_Should_Reject_Invalid_Final_Block_Lengths(string input)
    {
        var charAction = () => Base62.DecodeFromChars(input, new byte[input.Length]);
        var utf8Action = () => Base62.DecodeFromUtf8(Encoding.ASCII.GetBytes(input), new byte[input.Length]);

        charAction.Should().ThrowExactly<FormatException>();
        utf8Action.Should().ThrowExactly<FormatException>();
    }

    [TestMethod]
    public async Task Streaming_Example_Should_Match_One_Shot_Encoding()
    {
        const int BufferSize = 8 * 1024;
        var input = new byte[BufferSize + 5];
        new Random(42).NextBytes(input);
        var expected = Base62.EncodeToUtf8(input);

        await using var source = new MemoryStream(input);
        await using var destination = new MemoryStream();

        var inputBuffer = new byte[BufferSize];
        var outputBuffer = new byte[Base62.GetEncodedLength(BufferSize)];
        var buffered = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(inputBuffer.AsMemory(buffered));
            var isFinalBlock = bytesRead == 0;
            var available = buffered + bytesRead;

            var status = Base62.EncodeToUtf8(
                inputBuffer.AsSpan(0, available),
                outputBuffer,
                out var bytesConsumed,
                out var bytesWritten,
                isFinalBlock);

            await destination.WriteAsync(outputBuffer.AsMemory(0, bytesWritten));

            inputBuffer.AsSpan(bytesConsumed, available - bytesConsumed)
                .CopyTo(inputBuffer);
            buffered = available - bytesConsumed;

            if (isFinalBlock && status == OperationStatus.Done)
            {
                break;
            }

            if (status is not (OperationStatus.Done or OperationStatus.NeedMoreData))
            {
                throw new InvalidOperationException($"Unexpected status: {status}");
            }
        }

        destination.ToArray().Should().Equal(expected);
    }

    [TestMethod]
    public void Encode_Status_APIs_Should_Process_Complete_Blocks_And_Report_The_Remainder()
    {
        var input = Convert.FromHexString("010203040506070809");
        var chars = Enumerable.Repeat('\uCCCC', 13).ToArray();
        var utf8 = Enumerable.Repeat((byte) 0xCC, 13).ToArray();

        var charStatus = Base62.EncodeToChars(input, chars, out var charBytesConsumed, out var charsWritten, isFinalBlock: false);
        var utf8Status = Base62.EncodeToUtf8(input, utf8, out var utf8BytesConsumed, out var utf8BytesWritten, isFinalBlock: false);

        charStatus.Should().Be(OperationStatus.NeedMoreData);
        utf8Status.Should().Be(OperationStatus.NeedMoreData);
        charBytesConsumed.Should().Be(8);
        utf8BytesConsumed.Should().Be(8);
        charsWritten.Should().Be(11);
        utf8BytesWritten.Should().Be(11);
        chars.AsSpan(0, 11).ToString().Should().Be("zipgg0YFjg0");
        Encoding.ASCII.GetString(utf8, 0, 11).Should().Be("zipgg0YFjg0");
        chars.AsSpan(11).ToArray().Should().OnlyContain(value => value == '\uCCCC');
        utf8.AsSpan(11).ToArray().Should().OnlyContain(value => value == 0xCC);

        Base62.EncodeToChars(input.AsSpan(8), chars.AsSpan(11), out charBytesConsumed, out charsWritten).Should().Be(OperationStatus.Done);
        Base62.EncodeToUtf8(input.AsSpan(8), utf8.AsSpan(11), out utf8BytesConsumed, out utf8BytesWritten).Should().Be(OperationStatus.Done);
        charBytesConsumed.Should().Be(1);
        utf8BytesConsumed.Should().Be(1);
        charsWritten.Should().Be(2);
        utf8BytesWritten.Should().Be(2);
        chars.AsSpan().ToString().Should().Be("zipgg0YFjg090");
        Encoding.ASCII.GetString(utf8).Should().Be("zipgg0YFjg090");
    }

    [TestMethod]
    public void Decode_Status_APIs_Should_Process_Complete_Blocks_And_Report_The_Remainder()
    {
        const string encoded = "zipgg0YFjg090";
        var encodedUtf8 = Encoding.ASCII.GetBytes(encoded);
        var charOutput = Enumerable.Repeat((byte) 0xCC, 9).ToArray();
        var utf8Output = Enumerable.Repeat((byte) 0xCC, 9).ToArray();

        var charStatus = Base62.DecodeFromChars(encoded, charOutput, out var charsConsumed, out var charBytesWritten, isFinalBlock: false);
        var utf8Status = Base62.DecodeFromUtf8(encodedUtf8, utf8Output, out var utf8BytesConsumed, out var utf8BytesWritten, isFinalBlock: false);

        charStatus.Should().Be(OperationStatus.NeedMoreData);
        utf8Status.Should().Be(OperationStatus.NeedMoreData);
        charsConsumed.Should().Be(11);
        utf8BytesConsumed.Should().Be(11);
        charBytesWritten.Should().Be(8);
        utf8BytesWritten.Should().Be(8);
        charOutput.AsSpan(0, 8).ToArray().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        utf8Output.AsSpan(0, 8).ToArray().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        charOutput[^1].Should().Be(0xCC);
        utf8Output[^1].Should().Be(0xCC);

        Base62.DecodeFromChars(encoded.AsSpan(11), charOutput.AsSpan(8), out charsConsumed, out charBytesWritten).Should().Be(OperationStatus.Done);
        Base62.DecodeFromUtf8(encodedUtf8.AsSpan(11), utf8Output.AsSpan(8), out utf8BytesConsumed, out utf8BytesWritten).Should().Be(OperationStatus.Done);
        charsConsumed.Should().Be(2);
        utf8BytesConsumed.Should().Be(2);
        charBytesWritten.Should().Be(1);
        utf8BytesWritten.Should().Be(1);
        charOutput.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        utf8Output.Should().Equal(charOutput);
    }

    [TestMethod]
    public void Status_APIs_Should_Report_Destination_Too_Small_At_Block_Boundaries()
    {
        var input = new byte[9];
        var chars = new char[11];
        var utf8 = new byte[11];

        Base62.EncodeToChars(input, chars, out var encodeBytesConsumed, out var charsWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        Base62.EncodeToUtf8(input, utf8, out var utf8EncodeBytesConsumed, out var utf8BytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        encodeBytesConsumed.Should().Be(8);
        utf8EncodeBytesConsumed.Should().Be(8);
        charsWritten.Should().Be(11);
        utf8BytesWritten.Should().Be(11);

        Base62.DecodeFromChars("0000000000000", new byte[8], out var charsConsumed, out var charBytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        Base62.DecodeFromUtf8("0000000000000"u8, new byte[8], out var utf8BytesConsumed, out var utf8DecodedBytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        charsConsumed.Should().Be(11);
        utf8BytesConsumed.Should().Be(11);
        charBytesWritten.Should().Be(8);
        utf8DecodedBytesWritten.Should().Be(8);
    }

    [TestMethod]
    public void Status_APIs_Should_Not_Consume_A_Complete_Block_When_The_Destination_Is_Too_Small()
    {
        var input = new byte[8];
        var chars = Enumerable.Repeat('\uCCCC', 10).ToArray();
        var utf8 = Enumerable.Repeat((byte) 0xCC, 10).ToArray();

        Base62.EncodeToChars(input, chars, out var charBytesConsumed, out var charsWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        Base62.EncodeToUtf8(input, utf8, out var utf8BytesConsumed, out var utf8BytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        charBytesConsumed.Should().Be(0);
        utf8BytesConsumed.Should().Be(0);
        charsWritten.Should().Be(0);
        utf8BytesWritten.Should().Be(0);
        chars.Should().OnlyContain(value => value == '\uCCCC');
        utf8.Should().OnlyContain(value => value == 0xCC);

        var charOutput = Enumerable.Repeat((byte) 0xCC, 7).ToArray();
        var utf8Output = Enumerable.Repeat((byte) 0xCC, 7).ToArray();
        Base62.DecodeFromChars("00000000000", charOutput, out var charsConsumed, out var charBytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        Base62.DecodeFromUtf8("00000000000"u8, utf8Output, out var encodedBytesConsumed, out var utf8DecodedBytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        charsConsumed.Should().Be(0);
        encodedBytesConsumed.Should().Be(0);
        charBytesWritten.Should().Be(0);
        utf8DecodedBytesWritten.Should().Be(0);
        charOutput.Should().OnlyContain(value => value == 0xCC);
        utf8Output.Should().OnlyContain(value => value == 0xCC);
    }

    [TestMethod]
    public void Status_Decoders_Should_Report_The_Prefix_Written_Before_Invalid_Data()
    {
        const string input = "zipgg0YFjg0!0";
        var inputUtf8 = Encoding.ASCII.GetBytes(input);
        var charOutput = Enumerable.Repeat((byte) 0xCC, 9).ToArray();
        var utf8Output = Enumerable.Repeat((byte) 0xCC, 9).ToArray();

        var charStatus = Base62.DecodeFromChars(input, charOutput, out var charsConsumed, out var charBytesWritten);
        var utf8Status = Base62.DecodeFromUtf8(inputUtf8, utf8Output, out var utf8BytesConsumed, out var utf8BytesWritten);

        charStatus.Should().Be(OperationStatus.InvalidData);
        utf8Status.Should().Be(OperationStatus.InvalidData);
        charsConsumed.Should().Be(11);
        utf8BytesConsumed.Should().Be(11);
        charBytesWritten.Should().Be(8);
        utf8BytesWritten.Should().Be(8);
        charOutput.AsSpan(0, 8).ToArray().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        utf8Output.AsSpan(0, 8).ToArray().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        charOutput[^1].Should().Be(0xCC);
        utf8Output[^1].Should().Be(0xCC);
    }

    [TestMethod]
    public void NonFinal_Decoders_Should_Not_Validate_An_Incomplete_Block()
    {
        Base62.DecodeFromChars("!", Span<byte>.Empty, out var charsConsumed, out var bytesWritten, isFinalBlock: false)
            .Should().Be(OperationStatus.NeedMoreData);
        charsConsumed.Should().Be(0);
        bytesWritten.Should().Be(0);

        Base62.DecodeFromUtf8(new byte[] { 0xFF }, Span<byte>.Empty, out var bytesConsumed, out bytesWritten, isFinalBlock: false)
            .Should().Be(OperationStatus.NeedMoreData);
        bytesConsumed.Should().Be(0);
        bytesWritten.Should().Be(0);
    }

    [TestMethod]
    public void InPlace_Decoder_Should_Throw_For_Invalid_Data()
    {
        var action = () => Base62.DecodeFromUtf8InPlace("0_"u8.ToArray());

        action.Should().ThrowExactly<FormatException>();
    }

    [TestMethod]
    public void Utf8_Encoder_Should_Reject_Overlapping_Buffers()
    {
        var buffer = new byte[11];
        var action = () => Base62.EncodeToUtf8(buffer.AsSpan(0, 8), buffer);

        action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
    }

    [TestMethod]
    public void Character_Encoder_Should_Reject_Overlapping_Buffers()
    {
        var buffer = new byte[44];
        var action = () => Base62.EncodeToChars(buffer.AsSpan(0, 16), MemoryMarshal.Cast<byte, char>(buffer.AsSpan()));

        action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
    }

    [TestMethod]
    public void Utf8_Decoder_Should_Reject_Overlapping_Buffers_With_Different_Starts()
    {
        var buffer = new byte[14];
        Encoding.ASCII.GetBytes("zipgg0YFjg090", buffer);
        var action = () => Base62.DecodeFromUtf8(buffer.AsSpan(0, 13), buffer.AsSpan(1, 9));

        action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
    }

    [TestMethod]
    public void Utf8_Decoder_Should_Allow_InPlace_Continuation_With_Destination_Before_Source()
    {
        var input = Enumerable.Range(1, 16).Select(value => (byte) value).ToArray();
        var buffer = Base62.EncodeToUtf8(input);

        Base62.DecodeFromUtf8(buffer, buffer.AsSpan(0, 8), out var bytesConsumed, out var bytesWritten)
            .Should().Be(OperationStatus.DestinationTooSmall);
        bytesConsumed.Should().Be(11);
        bytesWritten.Should().Be(8);

        Base62.DecodeFromUtf8(buffer.AsSpan(bytesConsumed), buffer.AsSpan(bytesWritten, 8), out bytesConsumed, out bytesWritten)
            .Should().Be(OperationStatus.Done);
        bytesConsumed.Should().Be(11);
        bytesWritten.Should().Be(8);
        buffer.AsSpan(0, input.Length).ToArray().Should().Equal(input);
    }

    [TestMethod]
    public void Character_Decoder_Should_Reject_Overlapping_Buffers_With_Different_Starts()
    {
        var input = Enumerable.Range(1, 16).Select(value => (byte) value).ToArray();
        var encoded = Base62.EncodeToString(input);
        var buffer = new byte[encoded.Length * sizeof(char)];
        encoded.AsSpan().CopyTo(MemoryMarshal.Cast<byte, char>(buffer.AsSpan()));

        var action = () => Base62.DecodeFromChars(
            MemoryMarshal.Cast<byte, char>(buffer),
            buffer.AsSpan(16, input.Length));

        action.Should().ThrowExactly<ArgumentException>().WithParameterName("destination");
    }

    [TestMethod]
    public void Character_Decoder_Should_Allow_Overlapping_Buffers_With_The_Same_Start()
    {
        var input = Enumerable.Range(1, 16).Select(value => (byte) value).ToArray();
        var encoded = Base62.EncodeToString(input);
        var buffer = new byte[encoded.Length * sizeof(char)];
        encoded.AsSpan().CopyTo(MemoryMarshal.Cast<byte, char>(buffer.AsSpan()));

        var bytesWritten = Base62.DecodeFromChars(MemoryMarshal.Cast<byte, char>(buffer.AsSpan()), buffer);

        bytesWritten.Should().Be(input.Length);
        buffer.AsSpan(0, bytesWritten).ToArray().Should().Equal(input);
    }

    [TestMethod]
    public void Character_Decoder_Should_Allow_Destination_Before_Source()
    {
        var input = Enumerable.Range(1, 16).Select(value => (byte) value).ToArray();
        var encoded = Base62.EncodeToString(input);
        var buffer = new byte[8 + (encoded.Length * sizeof(char))];
        var source = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(8));
        encoded.AsSpan().CopyTo(source);

        var bytesWritten = Base62.DecodeFromChars(source, buffer.AsSpan(0, input.Length));

        bytesWritten.Should().Be(input.Length);
        buffer.AsSpan(0, bytesWritten).ToArray().Should().Equal(input);
    }

    private static ReadOnlySpan<byte> CreateOversizedSource()
        => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<byte>(), int.MaxValue);
}
