using System.Buffers.Binary;
using System.Text;

namespace LumosPresenter.Data.Importing.Paradox;

/// <summary>Paradox column types we care about; the rest are read as opaque bytes.</summary>
internal enum ParadoxFieldType : byte
{
    Alpha = 0x01,
    Date = 0x02,
    Short = 0x03,
    Long = 0x04,
    Currency = 0x05,
    Number = 0x06,
    Logical = 0x09,
    MemoBlob = 0x0C,
    BinaryBlob = 0x0D,
    FormattedMemoBlob = 0x0E,
    Ole = 0x0F,
    GraphicBlob = 0x10,
    Time = 0x14,
    Timestamp = 0x15,
    AutoIncrement = 0x16,
    Bcd = 0x17,
    Bytes = 0x18,
}

/// <summary>One column: its name, type, declared width, and byte offset within a record.</summary>
internal sealed record ParadoxField(string Name, ParadoxFieldType Type, int Size, int Offset);

/// <summary>
/// Where a memo column's value lives: <see cref="Inline"/> when the record itself held the
/// whole value, otherwise a block offset plus slot index into the .MB file.
/// </summary>
internal sealed record ParadoxBlobRef(uint BlockOffset, byte Index, int Length, byte[]? Inline);

/// <summary>
/// A reader for Paradox tables (.DB), the format EasyWorship 2009 stores its song library in.
/// Only the parts an import needs are implemented: the header, the field descriptors, and a
/// walk of the record block chain. Memo columns come back as <see cref="ParadoxBlobRef"/>
/// pointers that <see cref="ParadoxBlobStore"/> resolves against the sibling .MB file.
///
/// Layout reference (little-endian throughout):
///   0x00 recordSize, 0x02 headerSize, 0x05 blockSizeUnits (x1024), 0x06 recordCount,
///   0x0C fileBlocks, 0x0E firstBlock, 0x21 fieldCount, 0x39 versionId.
/// Field type/size pairs begin at 0x78; a table-name pointer plus one pointer per field
/// follow, then the table name buffer, then the null-terminated field names.
/// </summary>
internal sealed class ParadoxTable : IDisposable
{
    private readonly Stream stream;
    private readonly int headerSize;
    private readonly int blockSize;
    private readonly int fileBlocks;
    private readonly int firstBlock;

    public int RecordSize { get; }
    public int RecordCount { get; }
    public IReadOnlyList<ParadoxField> Fields { get; }

    private ParadoxTable(
        Stream stream, int recordSize, int headerSize, int blockSize, int recordCount,
        int fileBlocks, int firstBlock, IReadOnlyList<ParadoxField> fields)
    {
        this.stream = stream;
        RecordSize = recordSize;
        this.headerSize = headerSize;
        this.blockSize = blockSize;
        RecordCount = recordCount;
        this.fileBlocks = fileBlocks;
        this.firstBlock = firstBlock;
        Fields = fields;
    }

    public ParadoxField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Cheap format probe used to tell an EasyWorship 2009 library from a Firebird one. The
    /// header validation is strict — field widths must total the record size exactly — so a
    /// true result is a reliable positive rather than a guess at the extension.
    /// </summary>
    public static bool LooksLikeParadoxTable(string path)
    {
        try
        {
            using var table = Open(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }

    public static ParadoxTable Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return Read(stream, path);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static ParadoxTable Read(Stream stream, string path)
    {
        // The fixed part of the header is enough to learn how long the whole header is.
        var prefix = ReadExactly(stream, 0, 0x80);
        int recordSize = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(0x00));
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(0x02));
        int blockUnits = prefix[0x05];
        int recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(0x06));
        int fileBlocks = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(0x0C));
        int firstBlock = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(0x0E));
        int fieldCount = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(0x21));
        int versionId = prefix[0x39];

        if (recordSize <= 0 || headerSize < 0x78 || blockUnits is < 1 or > 32
            || fieldCount is < 1 or > 255 || recordCount < 0)
        {
            throw new InvalidDataException($"'{path}' is not a readable Paradox table.");
        }

        var header = ReadExactly(stream, 0, headerSize);

        // Field type/size pairs, then the pointer table, then names. Paradox 7+ (versionId
        // 0x0C and up) uses a 261-byte table-name buffer; older files use 79.
        const int fieldInfoStart = 0x78;
        var types = new (ParadoxFieldType Type, int Size)[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            var at = fieldInfoStart + i * 2;
            types[i] = ((ParadoxFieldType)header[at], header[at + 1]);
        }

        var pointers = fieldInfoStart + fieldCount * 2;
        var tableName = pointers + 4 + fieldCount * 4;
        var namesAt = tableName + (versionId >= 0x0C ? 261 : 79);

        var fields = new List<ParadoxField>(fieldCount);
        var offset = 0;
        var cursor = namesAt;
        for (var i = 0; i < fieldCount; i++)
        {
            var name = ReadCString(header, ref cursor);
            fields.Add(new ParadoxField(name, types[i].Type, types[i].Size, offset));
            offset += types[i].Size;
        }

        if (offset != recordSize)
        {
            throw new InvalidDataException(
                $"'{path}': field widths total {offset} bytes but records are {recordSize} bytes.");
        }

        return new ParadoxTable(
            stream, recordSize, headerSize, blockUnits * 1024,
            recordCount, fileBlocks, firstBlock, fields);
    }

    /// <summary>
    /// Yields each live record's bytes by walking the block chain from the header's first
    /// block. Every block states the byte length of the records after its first, so a
    /// partially filled block contributes only its real rows. A visited set guards against a
    /// corrupt chain looping forever.
    /// </summary>
    public IEnumerable<byte[]> Records()
    {
        var visited = new HashSet<int>();
        var block = firstBlock;
        while (block > 0 && block <= fileBlocks && visited.Add(block))
        {
            var at = headerSize + (long)(block - 1) * blockSize;
            var buffer = ReadExactly(stream, at, blockSize);
            var next = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0));
            var addDataSize = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(4));

            if (addDataSize >= 0)
            {
                var count = addDataSize / RecordSize + 1;
                for (var i = 0; i < count; i++)
                {
                    var start = 6 + i * RecordSize;
                    if (start + RecordSize > buffer.Length)
                    {
                        break;
                    }
                    yield return buffer[start..(start + RecordSize)];
                }
            }
            block = next;
        }
    }

    /// <summary>Alpha column to trimmed string, decoded as Windows-1252.</summary>
    public static string GetString(byte[] record, ParadoxField field)
    {
        var span = record.AsSpan(field.Offset, field.Size);
        var end = span.IndexOf((byte)0);
        if (end >= 0)
        {
            span = span[..end];
        }
        return ParadoxText.Decode(span).TrimEnd();
    }

    /// <summary>
    /// Memo column: either the inline leader (short values live entirely in the record) or a
    /// pointer into the .MB file. The last ten bytes of the column hold the pointer, the total
    /// blob length, and a modification counter.
    /// </summary>
    public static ParadoxBlobRef GetBlobRef(byte[] record, ParadoxField field)
    {
        var span = record.AsSpan(field.Offset, field.Size);
        var leader = field.Size - 10;
        var pointer = BinaryPrimitives.ReadUInt32LittleEndian(span[leader..]);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(leader + 4)..]);

        if (length <= 0 || length > 64 * 1024 * 1024)
        {
            return new ParadoxBlobRef(0, 0, 0, []);
        }
        if (length <= leader)
        {
            return new ParadoxBlobRef(0, 0, length, span[..length].ToArray());
        }
        // Low byte indexes a slot inside a shared block; the rest is the block's file offset.
        return new ParadoxBlobRef(pointer & 0xFFFFFF00u, (byte)(pointer & 0xFF), length, null);
    }

    private static string ReadCString(byte[] buffer, ref int cursor)
    {
        var start = cursor;
        while (cursor < buffer.Length && buffer[cursor] != 0)
        {
            cursor++;
        }
        var text = ParadoxText.Decode(buffer.AsSpan(start, cursor - start));
        if (cursor < buffer.Length)
        {
            cursor++; // step over the terminator
        }
        return text;
    }

    internal static byte[] ReadExactly(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer, 0, count);
        return buffer;
    }

    public void Dispose() => stream.Dispose();
}

/// <summary>
/// Windows-1252 decoding without pulling in the encoding-provider package: bytes are Latin-1
/// except 0x80-0x9F, which 1252 maps to typographic punctuation that shows up in song titles
/// and copyright lines (curly quotes, dashes, ellipsis). The five positions 1252 leaves
/// undefined stay at their raw code point.
/// </summary>
internal static class ParadoxText
{
    private const string HighRange =
        "€‚ƒ„…†‡" +
        "ˆ‰Š‹ŒŽ" +
        "‘’“”•–—" +
        "˜™š›œžŸ";

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            builder.Append(b is >= 0x80 and <= 0x9F ? HighRange[b - 0x80] : (char)b);
        }
        return builder.ToString();
    }
}
