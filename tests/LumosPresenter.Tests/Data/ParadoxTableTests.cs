using System.Buffers.Binary;
using System.Text;
using LumosPresenter.Data.Importing;
using LumosPresenter.Data.Importing.Paradox;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// Covers the Paradox reader behind the EasyWorship 2009 import. The fixtures are synthesised
/// rather than checked in as binaries so the expectations stay readable: each test builds a
/// table whose bytes it fully describes, then asserts the reader recovers the same values.
///
/// The three blob storage modes are the interesting part — Paradox puts a short value inline
/// in the record, packs medium values into shared 4 KB blocks, and gives a large value its own
/// run of blocks — and a reader that handles only one of them silently loses lyrics.
/// </summary>
public sealed class ParadoxTableTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "lumos-paradox-" + Guid.NewGuid().ToString("N"));

    public ParadoxTableTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ReadsFieldsRecordsAndAllThreeBlobStorageModes()
    {
        var path = WriteSongsTable();

        using var table = ParadoxTable.Open(path);

        Assert.Equal(3, table.RecordCount);
        Assert.Equal(50, table.RecordSize);
        Assert.Equal(["Title", "Words", "Author"], table.Fields.Select(f => f.Name));
        Assert.Equal(ParadoxFieldType.MemoBlob, table.Field("Words")!.Type);
        // Offsets must accumulate, since every read indexes into the record by them.
        Assert.Equal([0, 20, 40], table.Fields.Select(f => f.Offset));

        var title = table.Field("Title")!;
        var words = table.Field("Words")!;
        var records = table.Records().ToList();
        Assert.Equal(3, records.Count);

        using var blobs = ParadoxBlobStore.Open(Path.Combine(directory, "Songs.MB"));
        string Lyrics(int i) => Encoding.Latin1.GetString(blobs.Read(ParadoxTable.GetBlobRef(records[i], words)));

        Assert.Equal("Inline", ParadoxTable.GetString(records[0], title));
        Assert.Equal("short", Lyrics(0));

        Assert.Equal("Shared", ParadoxTable.GetString(records[1], title));
        Assert.Equal(new string('a', 30), Lyrics(1));

        Assert.Equal("Own blocks", ParadoxTable.GetString(records[2], title));
        Assert.Equal(new string('b', 5000), Lyrics(2));
    }

    [Fact]
    public void RejectsFilesWhoseFieldWidthsDoNotFillARecord()
    {
        // The strict check is what lets format detection trust a successful parse.
        var path = WriteSongsTable(recordSizeOverride: 64);

        var error = Assert.Throws<InvalidDataException>(() => ParadoxTable.Open(path));
        Assert.Contains("field widths", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(ParadoxTable.LooksLikeParadoxTable(path));
    }

    [Fact]
    public void LooksLikeParadoxTable_IsFalseForUnrelatedFiles()
    {
        var path = Path.Combine(directory, "SongWords.db");
        File.WriteAllText(path, "not a paradox table, just some bytes");

        Assert.False(ParadoxTable.LooksLikeParadoxTable(path));
    }

    [Fact]
    public void DecodesWindows1252PunctuationRatherThanLatin1Controls()
    {
        // 0x92 is a curly apostrophe in 1252 but a control character in Latin-1, and it shows
        // up constantly in song titles ("God's Love").
        Assert.Equal("God’s", ParadoxText.Decode([(byte)'G', (byte)'o', (byte)'d', 0x92, (byte)'s']));
        Assert.Equal("€…—", ParadoxText.Decode([0x80, 0x85, 0x97]));
        // Bytes outside the remapped window stay Latin-1.
        Assert.Equal("é", ParadoxText.Decode([0xE9]));
    }

    [Fact]
    public void ResolvesAParadoxFolderAsAn2009Library()
    {
        WriteSongsTable();

        Assert.True(EasyWorshipSource.TryResolve(directory, out var library, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(EasyWorshipFormat.Paradox, library.Format);
        Assert.Equal(Path.Combine(directory, "Songs.MB"), library.WordsPath);
        Assert.Contains("2009", library.Description);
    }

    [Fact]
    public void ExplainsAMissingSongWordsDatabaseForSixSeven()
    {
        // A Firebird library is two files; pointing at only the first one is a common mistake,
        // so the message has to say what is missing rather than just failing to parse.
        File.WriteAllText(Path.Combine(directory, "Songs.db"), "firebird-ish bytes");

        Assert.False(EasyWorshipSource.TryResolve(directory, out _, out var error));
        Assert.Contains("SongWords.db", error);
    }

    [Fact]
    public void ReportsPathsThatDoNotExist()
    {
        Assert.False(EasyWorshipSource.TryResolve(
            Path.Combine(directory, "nope"), out _, out var error));
        Assert.Contains("does not exist", error);
    }

    /// <summary>
    /// Writes a three-record Songs.DB plus its Songs.MB. Columns are Title (Alpha 20),
    /// Words (Memo 20) and Author (Alpha 10), totalling the 50-byte record the header
    /// declares — unless <paramref name="recordSizeOverride"/> deliberately breaks that.
    /// </summary>
    private string WriteSongsTable(int? recordSizeOverride = null)
    {
        const int headerSize = 2048;
        const int blockSize = 1024;
        const int recordSize = 50;
        string[] names = ["Title", "Words", "Author"];
        (byte Type, byte Size)[] fields =
        [
            ((byte)ParadoxFieldType.Alpha, 20),
            ((byte)ParadoxFieldType.MemoBlob, 20),
            ((byte)ParadoxFieldType.Alpha, 10),
        ];

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x00), (ushort)(recordSizeOverride ?? recordSize));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x02), headerSize);
        header[0x05] = 1; // blockSize = 1 x 1024
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x06), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x0C), 1); // fileBlocks
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x0E), 1); // firstBlock
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x21), (ushort)fields.Length);
        header[0x39] = 0x0C; // Paradox 7+, so the table-name buffer is 261 bytes

        for (var i = 0; i < fields.Length; i++)
        {
            header[0x78 + i * 2] = fields[i].Type;
            header[0x78 + i * 2 + 1] = fields[i].Size;
        }

        var namesAt = 0x78 + fields.Length * 2 + 4 + fields.Length * 4 + 261;
        var cursor = namesAt;
        foreach (var name in names)
        {
            foreach (var b in Encoding.ASCII.GetBytes(name))
            {
                header[cursor++] = b;
            }
            header[cursor++] = 0;
        }

        // One data block holding all three records. addDataSize counts the bytes after the
        // first record, so the reader derives a count of three.
        var block = new byte[blockSize];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0), 0); // no next block
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(4), (short)(recordSize * 2));

        WriteRecord(block, 6 + recordSize * 0, "Inline", "short"u8.ToArray(), pointer: 0);
        WriteRecord(block, 6 + recordSize * 1, "Shared", null, pointer: 4096, blobLength: 30);
        WriteRecord(block, 6 + recordSize * 2, "Own blocks", null, pointer: 8192 | 0xFF, blobLength: 5000);

        var path = Path.Combine(directory, "Songs.DB");
        using (var file = File.Create(path))
        {
            file.Write(header);
            file.Write(block);
        }

        WriteBlobFile();
        return path;

        static void WriteRecord(byte[] block, int at, string title, byte[]? inline, uint pointer, int blobLength = 0)
        {
            Encoding.ASCII.GetBytes(title).CopyTo(block.AsSpan(at));
            var words = at + 20;
            var leader = 20 - 10;
            if (inline is not null)
            {
                inline.CopyTo(block.AsSpan(words));
                blobLength = inline.Length;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(words + leader), pointer);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(words + leader + 4), (uint)blobLength);
        }
    }

    /// <summary>
    /// Songs.MB with a suballocated block at 4096 (type 3, slot 0) and a large blob owning
    /// its blocks at 8192 (type 2).
    /// </summary>
    private void WriteBlobFile()
    {
        var mb = new byte[8192 + 4096 * 2];

        // Shared block: slot 0 points 256 bytes in and is 30 bytes long (1 x 16 + 14).
        mb[4096] = 3;
        mb[4096 + 12 + 0] = 16; // offset / 16
        mb[4096 + 12 + 1] = 1;  // length / 16
        mb[4096 + 12 + 4] = 14; // length % 16
        for (var i = 0; i < 30; i++)
        {
            mb[4096 + 256 + i] = (byte)'a';
        }

        // Own-blocks blob: nine-byte header, then the data.
        mb[8192] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(mb.AsSpan(8192 + 1), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(mb.AsSpan(8192 + 3), 5000);
        for (var i = 0; i < 5000; i++)
        {
            mb[8192 + 9 + i] = (byte)'b';
        }

        File.WriteAllBytes(Path.Combine(directory, "Songs.MB"), mb);
    }
}
