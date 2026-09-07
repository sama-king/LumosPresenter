using System.Buffers.Binary;

namespace LumosPresenter.Data.Importing.Paradox;

/// <summary>
/// Resolves Paradox memo pointers against the sibling blob file (.MB). Blobs are stored one
/// of three ways, and the pointer in the record says which:
///
///   * short values never reach the .MB at all - they sit inline in the record itself;
///   * a large value gets its own run of 4 KB blocks (block type 2), with a nine-byte header
///     followed by the data;
///   * several small values share one 4 KB block (block type 3), whose slot table starts at
///     byte 12 with a five-byte entry per slot - offset and length are stored in 16-byte
///     units, with the length remainder in the entry's last byte.
///
/// Anything unreadable yields an empty value rather than throwing: one damaged blob in a
/// library of thousands should cost that one song, not the whole import.
/// </summary>
internal sealed class ParadoxBlobStore : IDisposable
{
    private const int BlockSize = 4096;
    private readonly Stream? stream;
    private readonly long length;

    private ParadoxBlobStore(Stream? stream, long length)
    {
        this.stream = stream;
        this.length = length;
    }

    /// <summary>Opens the .MB beside a table, or a store that resolves only inline blobs.</summary>
    public static ParadoxBlobStore Open(string? blobPath)
    {
        if (string.IsNullOrEmpty(blobPath) || !File.Exists(blobPath))
        {
            return new ParadoxBlobStore(null, 0);
        }
        var stream = new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new ParadoxBlobStore(stream, stream.Length);
    }

    public byte[] Read(ParadoxBlobRef reference)
    {
        if (reference.Inline is { } inline)
        {
            return inline;
        }
        if (stream is null || reference.Length <= 0)
        {
            return [];
        }

        try
        {
            var blockStart = (long)reference.BlockOffset;
            if (blockStart <= 0 || blockStart + 12 > length)
            {
                return [];
            }

            var head = ParadoxTable.ReadExactly(stream, blockStart, 12);
            switch (head[0])
            {
                case 2:
                {
                    // Own run of blocks: trust the header's length, bounded by the record's.
                    var stored = (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(3));
                    var size = Math.Min(reference.Length, stored <= 0 ? reference.Length : stored);
                    return ReadClamped(blockStart + 9, size);
                }
                case 3:
                {
                    var entry = blockStart + 12 + reference.Index * 5L;
                    if (entry + 5 > length)
                    {
                        return [];
                    }
                    var slot = ParadoxTable.ReadExactly(stream, entry, 5);
                    var dataAt = blockStart + slot[0] * 16L;
                    var size = slot[1] * 16 + slot[4];
                    if (size <= 0 || dataAt < blockStart || dataAt >= blockStart + BlockSize)
                    {
                        return [];
                    }
                    return ReadClamped(dataAt, size);
                }
                default:
                    return [];
            }
        }
        catch (IOException)
        {
            return [];
        }
    }

    private byte[] ReadClamped(long offset, int size)
    {
        if (offset < 0 || offset >= length || size <= 0)
        {
            return [];
        }
        var available = (int)Math.Min(size, length - offset);
        return ParadoxTable.ReadExactly(stream!, offset, available);
    }

    public void Dispose() => stream?.Dispose();
}
