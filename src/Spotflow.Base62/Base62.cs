using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spotflow.Base62;

/// <summary>
/// Encodes binary data to and decodes binary data from the Base62 format used by this library.
/// </summary>
/// <remarks>
/// The format uses the alphabet <c>0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz</c>.
/// It is case-sensitive and is not guaranteed to be compatible with other Base62 implementations.
/// </remarks>
public static class Base62
{
    internal const string Base62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private const byte _unmappedCharacterSentinel = byte.MaxValue;
    private const int _bitsPerByte = 8;
    private const int _encodedBlockCharCount = 11;
    private const int _decodedBlockByteCount = 8;
#if NET8_0
    private const int _stackAllocatedCharThreshold = 1024;
#endif
    private static readonly byte[] _base62DecodingTable = CreateDecodingTable();

    /// <summary>
    /// Gets the exact encoded length for binary data of the specified length.
    /// </summary>
    /// <param name="sourceLength">The number of bytes to encode.</param>
    /// <returns>The encoded length in characters or UTF-8 bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sourceLength"/> is negative or the encoded length would exceed <see cref="int.MaxValue"/>.
    /// </exception>
    public static int GetEncodedLength(int sourceLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);

        if (!TryGetEncodedLength(sourceLength, out var encodedLength))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength), sourceLength, "The encoded length exceeds Int32.MaxValue.");
        }

        return encodedLength;
    }

    /// <summary>
    /// Gets the exact decoded length represented by a structurally valid encoded length.
    /// </summary>
    /// <param name="encodedLength">The number of Base62 characters or UTF-8 bytes.</param>
    /// <returns>The decoded length in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="encodedLength"/> is negative.</exception>
    /// <exception cref="FormatException"><paramref name="encodedLength"/> cannot represent data produced by this library.</exception>
    public static int GetDecodedLength(int encodedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedLength);

        var (fullBlockCount, trailingCharCount) = Math.DivRem(encodedLength, _encodedBlockCharCount);

        if (!TryGetTrailingDecodedLength(trailingCharCount, out var trailingByteCount))
        {
            throw new FormatException("The encoded data has an invalid final block length.");
        }

        return (fullBlockCount * _decodedBlockByteCount) + trailingByteCount;
    }

    /// <summary>
    /// Encodes binary data as a Base62 string.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <returns>The case-sensitive Base62 representation of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is too large to encode into a single result.</exception>
    public static string EncodeToString(ReadOnlySpan<byte> source)
    {
        if (!TryGetEncodedLength(source.Length, out var encodedLength))
        {
            throw new ArgumentException("The source is too large to encode into a single result.", nameof(source));
        }

#if NET9_0_OR_GREATER
        return string.Create(encodedLength, source, static (destination, state) => EncodeToChars(state, destination));
#else
        if (encodedLength <= _stackAllocatedCharThreshold)
        {
            Span<char> encoded = stackalloc char[encodedLength];
            EncodeToChars(source, encoded);
            return new(encoded);
        }

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(source.Length);

        try
        {
            source.CopyTo(rentedBuffer);

            return string.Create(
                encodedLength,
                (Buffer: rentedBuffer, Length: source.Length),
                static (destination, state) => EncodeToChars(state.Buffer.AsSpan(0, state.Length), destination));
        }
        finally
        {
            rentedBuffer.AsSpan(0, source.Length).Clear();
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
#endif
    }

    /// <summary>
    /// Encodes binary data as Base62 characters.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded characters.</param>
    /// <returns>The number of characters written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static int EncodeToChars(ReadOnlySpan<byte> source, Span<char> destination)
    {
        var status = EncodeToChars(source, destination, out _, out var charsWritten);

        if (status == OperationStatus.Done)
        {
            return charsWritten;
        }

        Debug.Assert(status == OperationStatus.DestinationTooSmall);
        throw new ArgumentException("The destination is too small to hold the encoded output.", nameof(destination));
    }

    /// <summary>
    /// Attempts to encode binary data as Base62 characters.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded characters.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <returns><see langword="true"/> if the destination was large enough; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static bool TryEncodeToChars(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
    {
        ThrowIfEncodingBuffersOverlap(source, destination);

        if (!TryGetEncodedLength(source.Length, out var encodedLength) || destination.Length < encodedLength)
        {
            charsWritten = 0;
            return false;
        }

        var status = EncodeCore<char, CharSymbolCodec>(
            source,
            destination,
            out _,
            out charsWritten,
            isFinalBlock: true);
        Debug.Assert(status == OperationStatus.Done);
        return true;
    }

    /// <summary>
    /// Encodes binary data as Base62 characters, optionally as part of a streaming operation.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded characters.</param>
    /// <param name="bytesConsumed">The number of input bytes consumed.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="isFinalBlock"><see langword="true"/> if <paramref name="source"/> contains the final input block.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static OperationStatus EncodeToChars(
        ReadOnlySpan<byte> source,
        Span<char> destination,
        out int bytesConsumed,
        out int charsWritten,
        bool isFinalBlock = true)
    {
        ThrowIfEncodingBuffersOverlap(source, destination);
        return EncodeCore<char, CharSymbolCodec>(
            source,
            destination,
            out bytesConsumed,
            out charsWritten,
            isFinalBlock);
    }

    /// <summary>
    /// Encodes binary data as Base62 UTF-8 bytes.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <returns>The UTF-8 Base62 representation of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is too large to encode into a single result.</exception>
    public static byte[] EncodeToUtf8(ReadOnlySpan<byte> source)
    {
        if (!TryGetEncodedLength(source.Length, out var encodedLength))
        {
            throw new ArgumentException("The source is too large to encode into a single result.", nameof(source));
        }

        var destination = new byte[encodedLength];
        EncodeToUtf8(source, destination);
        return destination;
    }

    /// <summary>
    /// Encodes binary data as Base62 UTF-8 bytes.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded UTF-8 bytes.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static int EncodeToUtf8(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var status = EncodeToUtf8(source, destination, out _, out var bytesWritten);

        if (status == OperationStatus.Done)
        {
            return bytesWritten;
        }

        Debug.Assert(status == OperationStatus.DestinationTooSmall);
        throw new ArgumentException("The destination is too small to hold the encoded output.", nameof(destination));
    }

    /// <summary>
    /// Attempts to encode binary data as Base62 UTF-8 bytes.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded UTF-8 bytes.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <returns><see langword="true"/> if the destination was large enough; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static bool TryEncodeToUtf8(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        ThrowIfEncodingBuffersOverlap(source, destination);

        if (!TryGetEncodedLength(source.Length, out var encodedLength) || destination.Length < encodedLength)
        {
            bytesWritten = 0;
            return false;
        }

        var status = EncodeCore<byte, Utf8SymbolCodec>(
            source,
            destination,
            out _,
            out bytesWritten,
            isFinalBlock: true);
        Debug.Assert(status == OperationStatus.Done);
        return true;
    }

    /// <summary>
    /// Encodes binary data as Base62 UTF-8 bytes, optionally as part of a streaming operation.
    /// </summary>
    /// <param name="source">The binary data to encode.</param>
    /// <param name="destination">The destination for the encoded UTF-8 bytes.</param>
    /// <param name="bytesConsumed">The number of input bytes consumed.</param>
    /// <param name="bytesWritten">The number of UTF-8 bytes written.</param>
    /// <param name="isFinalBlock"><see langword="true"/> if <paramref name="source"/> contains the final input block.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> and <paramref name="destination"/> overlap.</exception>
    public static OperationStatus EncodeToUtf8(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten,
        bool isFinalBlock = true)
    {
        ThrowIfEncodingBuffersOverlap(source, destination);
        return EncodeCore<byte, Utf8SymbolCodec>(
            source,
            destination,
            out bytesConsumed,
            out bytesWritten,
            isFinalBlock);
    }

    /// <summary>
    /// Decodes Base62 characters into a newly allocated byte array.
    /// </summary>
    /// <param name="source">The Base62 characters to decode.</param>
    /// <returns>The decoded binary data.</returns>
    /// <exception cref="FormatException"><paramref name="source"/> is not valid Base62 text produced by this library.</exception>
    public static byte[] DecodeFromChars(ReadOnlySpan<char> source)
    {
        var destination = new byte[GetDecodedLength(source.Length)];
        DecodeFromChars(source, destination);
        return destination;
    }

    /// <summary>
    /// Decodes Base62 characters into binary data.
    /// </summary>
    /// <param name="source">The Base62 characters to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    /// <exception cref="FormatException"><paramref name="source"/> is not valid Base62 text produced by this library.</exception>
    public static int DecodeFromChars(ReadOnlySpan<char> source, Span<byte> destination)
    {
        var status = DecodeFromChars(source, destination, out _, out var bytesWritten);

        if (status == OperationStatus.Done)
        {
            return bytesWritten;
        }

        if (status == OperationStatus.DestinationTooSmall)
        {
            throw new ArgumentException("The destination is too small to hold the decoded output.", nameof(destination));
        }

        Debug.Assert(status == OperationStatus.InvalidData);
        throw new FormatException("The source is not valid Base62 data produced by this library.");
    }

    /// <summary>
    /// Attempts to decode Base62 characters into binary data.
    /// </summary>
    /// <param name="source">The Base62 characters to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <param name="bytesWritten">The number of bytes written before the operation completed or failed.</param>
    /// <returns><see langword="true"/> if decoding succeeded; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    public static bool TryDecodeFromChars(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
    {
        var status = DecodeFromChars(source, destination, out _, out bytesWritten);
        Debug.Assert(status is OperationStatus.Done or OperationStatus.DestinationTooSmall or OperationStatus.InvalidData);
        return status == OperationStatus.Done;
    }

    /// <summary>
    /// Decodes Base62 characters into binary data, optionally as part of a streaming operation.
    /// </summary>
    /// <param name="source">The Base62 characters to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <param name="charsConsumed">The number of input characters consumed.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <param name="isFinalBlock"><see langword="true"/> if <paramref name="source"/> contains the final input block.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    public static OperationStatus DecodeFromChars(
        ReadOnlySpan<char> source,
        Span<byte> destination,
        out int charsConsumed,
        out int bytesWritten,
        bool isFinalBlock = true)
    {
        ThrowIfUnsupportedDecodingOverlap(source, destination);
        return DecodeCore<char, CharSymbolCodec>(
            source,
            destination,
            out charsConsumed,
            out bytesWritten,
            isFinalBlock);
    }

    /// <summary>
    /// Decodes Base62 UTF-8 bytes into a newly allocated byte array.
    /// </summary>
    /// <param name="source">The Base62 UTF-8 bytes to decode.</param>
    /// <returns>The decoded binary data.</returns>
    /// <exception cref="FormatException"><paramref name="source"/> is not valid Base62 text produced by this library.</exception>
    public static byte[] DecodeFromUtf8(ReadOnlySpan<byte> source)
    {
        var destination = new byte[GetDecodedLength(source.Length)];
        DecodeFromUtf8(source, destination);
        return destination;
    }

    /// <summary>
    /// Decodes Base62 UTF-8 bytes into binary data.
    /// </summary>
    /// <param name="source">The Base62 UTF-8 bytes to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    /// <exception cref="FormatException"><paramref name="source"/> is not valid Base62 text produced by this library.</exception>
    public static int DecodeFromUtf8(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var status = DecodeFromUtf8(source, destination, out _, out var bytesWritten);

        if (status == OperationStatus.Done)
        {
            return bytesWritten;
        }

        if (status == OperationStatus.DestinationTooSmall)
        {
            throw new ArgumentException("The destination is too small to hold the decoded output.", nameof(destination));
        }

        Debug.Assert(status == OperationStatus.InvalidData);
        throw new FormatException("The source is not valid Base62 data produced by this library.");
    }

    /// <summary>
    /// Attempts to decode Base62 UTF-8 bytes into binary data.
    /// </summary>
    /// <param name="source">The Base62 UTF-8 bytes to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <param name="bytesWritten">The number of bytes written before the operation completed or failed.</param>
    /// <returns><see langword="true"/> if decoding succeeded; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    public static bool TryDecodeFromUtf8(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        var status = DecodeFromUtf8(source, destination, out _, out bytesWritten);
        Debug.Assert(status is OperationStatus.Done or OperationStatus.DestinationTooSmall or OperationStatus.InvalidData);
        return status == OperationStatus.Done;
    }

    /// <summary>
    /// Decodes Base62 UTF-8 bytes into binary data, optionally as part of a streaming operation.
    /// </summary>
    /// <param name="source">The Base62 UTF-8 bytes to decode.</param>
    /// <param name="destination">The destination for the decoded bytes.</param>
    /// <param name="bytesConsumed">The number of input bytes consumed.</param>
    /// <param name="bytesWritten">The number of decoded bytes written.</param>
    /// <param name="isFinalBlock"><see langword="true"/> if <paramref name="source"/> contains the final input block.</param>
    /// <returns>The status of the operation.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> and <paramref name="destination"/> overlap and <paramref name="destination"/> begins after <paramref name="source"/>.
    /// </exception>
    public static OperationStatus DecodeFromUtf8(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten,
        bool isFinalBlock = true)
    {
        ThrowIfUnsupportedDecodingOverlap(source, destination);
        return DecodeCore<byte, Utf8SymbolCodec>(
            source,
            destination,
            out bytesConsumed,
            out bytesWritten,
            isFinalBlock);
    }

    /// <summary>
    /// Decodes Base62 UTF-8 bytes in place. The decoded bytes are written to the beginning of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer containing Base62 UTF-8 bytes.</param>
    /// <returns>The number of decoded bytes written to <paramref name="buffer"/>.</returns>
    /// <exception cref="FormatException"><paramref name="buffer"/> is not valid Base62 text produced by this library.</exception>
    public static int DecodeFromUtf8InPlace(Span<byte> buffer) => DecodeFromUtf8(buffer, buffer);

    private static OperationStatus EncodeCore<TSymbol, TCodec>(
        ReadOnlySpan<byte> source,
        Span<TSymbol> destination,
        out int bytesConsumed,
        out int symbolsWritten,
        bool isFinalBlock)
        where TCodec : ISymbolCodec<TSymbol>
    {
        bytesConsumed = 0;
        symbolsWritten = 0;

        while (source.Length - bytesConsumed >= _decodedBlockByteCount)
        {
            if (destination.Length - symbolsWritten < _encodedBlockCharCount)
            {
                return OperationStatus.DestinationTooSmall;
            }

            var block = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(bytesConsumed, _decodedBlockByteCount));
            EncodeBlock<TSymbol, TCodec>(block, destination.Slice(symbolsWritten, _encodedBlockCharCount));
            bytesConsumed += _decodedBlockByteCount;
            symbolsWritten += _encodedBlockCharCount;
        }

        var trailingByteCount = source.Length - bytesConsumed;

        if (trailingByteCount == 0)
        {
            return OperationStatus.Done;
        }

        if (!isFinalBlock)
        {
            return OperationStatus.NeedMoreData;
        }

        var trailingSymbolCount = GetTrailingEncodedLength(trailingByteCount);

        if (destination.Length - symbolsWritten < trailingSymbolCount)
        {
            return OperationStatus.DestinationTooSmall;
        }

        var trailingBlock = ReadPartialBlock(source.Slice(bytesConsumed));
        EncodeBlock<TSymbol, TCodec>(trailingBlock, destination.Slice(symbolsWritten, trailingSymbolCount));
        bytesConsumed += trailingByteCount;
        symbolsWritten += trailingSymbolCount;
        return OperationStatus.Done;
    }

    private static OperationStatus DecodeCore<TSymbol, TCodec>(
        ReadOnlySpan<TSymbol> source,
        Span<byte> destination,
        out int symbolsConsumed,
        out int bytesWritten,
        bool isFinalBlock)
        where TCodec : ISymbolCodec<TSymbol>
    {
        symbolsConsumed = 0;
        bytesWritten = 0;

        while (source.Length - symbolsConsumed >= _encodedBlockCharCount)
        {
            if (destination.Length - bytesWritten < _decodedBlockByteCount)
            {
                return OperationStatus.DestinationTooSmall;
            }

            if (!TryDecodeBlock<TSymbol, TCodec>(source.Slice(symbolsConsumed, _encodedBlockCharCount), out var block))
            {
                return OperationStatus.InvalidData;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(bytesWritten, _decodedBlockByteCount), block);
            symbolsConsumed += _encodedBlockCharCount;
            bytesWritten += _decodedBlockByteCount;
        }

        var trailingSymbolCount = source.Length - symbolsConsumed;

        if (trailingSymbolCount == 0)
        {
            return OperationStatus.Done;
        }

        if (!isFinalBlock)
        {
            return OperationStatus.NeedMoreData;
        }

        if (!TryGetTrailingDecodedLength(trailingSymbolCount, out var trailingByteCount))
        {
            return OperationStatus.InvalidData;
        }

        if (destination.Length - bytesWritten < trailingByteCount)
        {
            return OperationStatus.DestinationTooSmall;
        }

        if (!TryDecodeBlock<TSymbol, TCodec>(source.Slice(symbolsConsumed), out var trailingBlock)
            || trailingBlock >= 1UL << (trailingByteCount * _bitsPerByte))
        {
            return OperationStatus.InvalidData;
        }

        WritePartialBlock(destination.Slice(bytesWritten, trailingByteCount), trailingBlock);
        symbolsConsumed += trailingSymbolCount;
        bytesWritten += trailingByteCount;
        return OperationStatus.Done;
    }

    private static byte[] CreateDecodingTable()
    {
        var table = new byte[128];
        Array.Fill(table, _unmappedCharacterSentinel);

        for (var digit = 0; digit < Base62Alphabet.Length; digit++)
        {
            table[Base62Alphabet[digit]] = (byte) digit;
        }

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetEncodedLength(int sourceLength, out int encodedLength)
    {
        var (fullBlockCount, trailingByteCount) = Math.DivRem(sourceLength, _decodedBlockByteCount);
        var length = (fullBlockCount * (long) _encodedBlockCharCount) + GetTrailingEncodedLength(trailingByteCount);
        encodedLength = (int) Math.Min(length, int.MaxValue);
        return length <= int.MaxValue;
    }

    private static void ThrowIfEncodingBuffersOverlap(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Overlaps(destination))
        {
            throw new ArgumentException("The source and destination must not overlap.", nameof(destination));
        }
    }

    private static void ThrowIfEncodingBuffersOverlap(ReadOnlySpan<byte> source, Span<char> destination)
    {
        if (source.Overlaps(MemoryMarshal.AsBytes(destination)))
        {
            throw new ArgumentException("The source and destination must not overlap.", nameof(destination));
        }
    }

    private static void ThrowIfUnsupportedDecodingOverlap(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Overlaps(destination, out var elementOffset) && elementOffset > 0)
        {
            throw new ArgumentException("An overlapping destination must not begin after the source.", nameof(destination));
        }
    }

    private static void ThrowIfUnsupportedDecodingOverlap(ReadOnlySpan<char> source, Span<byte> destination)
    {
        if (MemoryMarshal.AsBytes(source).Overlaps(destination, out var byteOffset) && byteOffset > 0)
        {
            throw new ArgumentException("An overlapping destination must not begin after the source.", nameof(destination));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetTrailingEncodedLength(int byteCount) => byteCount switch
    {
        0 => 0,
        1 => 2,
        2 => 3,
        3 => 5,
        4 => 6,
        5 => 7,
        6 => 9,
        7 => 10,
        _ => throw new UnreachableException(),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetTrailingDecodedLength(int charCount, out int byteCount)
    {
        byteCount = charCount switch
        {
            0 => 0,
            2 => 1,
            3 => 2,
            5 => 3,
            6 => 4,
            7 => 5,
            9 => 6,
            10 => 7,
            _ => -1,
        };

        return byteCount >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadPartialBlock(ReadOnlySpan<byte> source)
    {
        ulong block = 0;

        for (var i = 0; i < source.Length; i++)
        {
            block |= (ulong) source[i] << (i * _bitsPerByte);
        }

        return block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePartialBlock(Span<byte> destination, ulong block)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte) (block >> (i * _bitsPerByte));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EncodeBlock<TSymbol, TCodec>(ulong block, Span<TSymbol> destination)
        where TCodec : ISymbolCodec<TSymbol>
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var (quotient, remainder) = Math.DivRem(block, 62);
            block = quotient;
            destination[i] = TCodec.Encode((byte) remainder);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDecodeBlock<TSymbol, TCodec>(ReadOnlySpan<TSymbol> source, out ulong block)
        where TCodec : ISymbolCodec<TSymbol>
    {
        block = 0;

        for (var i = source.Length - 1; i >= 0; i--)
        {
            if (!TCodec.TryDecode(source[i], out var digit))
            {
                return false;
            }

            if (i == 0 && source.Length == _encodedBlockCharCount && block > (ulong.MaxValue - digit) / 62)
            {
                return false;
            }

            block = (block * 62) + digit;
        }

        return true;
    }

    private static bool TryDecodeSymbol(int symbol, out byte digit)
    {
        if ((uint) symbol >= (uint) _base62DecodingTable.Length)
        {
            digit = 0;
            return false;
        }

        digit = _base62DecodingTable[symbol];
        return digit != _unmappedCharacterSentinel;
    }

    private interface ISymbolCodec<TSymbol>
    {
        public static abstract TSymbol Encode(byte digit);

        public static abstract bool TryDecode(TSymbol symbol, out byte digit);
    }

    private readonly struct CharSymbolCodec : ISymbolCodec<char>
    {
        public static char Encode(byte digit) => Base62Alphabet[digit];

        public static bool TryDecode(char symbol, out byte digit) => TryDecodeSymbol(symbol, out digit);
    }

    private readonly struct Utf8SymbolCodec : ISymbolCodec<byte>
    {
        public static byte Encode(byte digit) => (byte) Base62Alphabet[digit];

        public static bool TryDecode(byte symbol, out byte digit) => TryDecodeSymbol(symbol, out digit);
    }
}
