using System.Security.Cryptography;
using System.Text.Json;
using PokemonMove.Domain;

namespace PokemonMove.Infrastructure;

/// <summary>App-private bank. One instance owns all access; reads never erase damaged files.</summary>
public sealed class LocalBankStore(string directory, IPokemonCodec codec) : IBankStore
{
    public const int MaximumBackupBytes = 8 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    private readonly string path = Path.Combine(directory, "bank.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Document(int Version, List<BankEntry> Entries);

    public async Task<IReadOnlyList<BankItem>> ListAsync()
    {
        await gate.WaitAsync();
        try { return (await ReadAsync()).Entries.Select(ToItem).ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<BankAddResult> AddAsync(PokemonSummary pokemon, string game, string trainer)
    {
        if (string.IsNullOrWhiteSpace(game) || trainer is null) throw new InvalidDataException("コピー元の情報が不正です。");
        if (!pokemon.CanStore) throw new InvalidDataException("このポケモンは保管できません。");
        var data = codec.Decode(pokemon.StoredData.ToArray()).StoredData;
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync();
            var fingerprint = Fingerprint(data);
            var existing = doc.Entries.FirstOrDefault(e => Fingerprint(e.Data) == fingerprint);
            if (existing is not null) return new(ToItem(existing), false);
            if (doc.Entries.Count >= MaximumEntries) throw new InvalidDataException("バンクの保管上限に達しています。");
            var entry = new BankEntry(Guid.NewGuid(), game, trainer, DateTimeOffset.UtcNow, data);
            doc.Entries.Add(entry);
            await WriteAsync(doc);
            return new(ToItem(entry), true);
        }
        finally { gate.Release(); }
    }

    public async Task<byte[]> ExportAsync()
    {
        await gate.WaitAsync();
        try { return Serialize(await ReadAsync()); }
        finally { gate.Release(); }
    }

    public async Task<int> ImportAsync(byte[] backup)
    {
        var imported = Parse(backup);
        await gate.WaitAsync();
        try
        {
            var current = await ReadAsync();
            var hashes = current.Entries.Select(e => Fingerprint(e.Data)).ToHashSet();
            var added = 0;
            foreach (var entry in imported.Entries)
            {
                if (!hashes.Add(Fingerprint(entry.Data))) continue;
                current.Entries.Add(entry with { Id = Guid.NewGuid(), Data = entry.Data.ToArray() });
                added++;
            }
            if (current.Entries.Count > MaximumEntries) throw new InvalidDataException("バンクの保管上限を超えています。");
            if (added > 0) await WriteAsync(current);
            return added;
        }
        finally { gate.Release(); }
    }

    public async Task RestorePreviousAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("直前のバックアップはまだありません。");
            var doc = Parse(await ReadBoundedAsync(path + ".bak"));
            await WriteAsync(doc, keepPrevious: false);
        }
        finally { gate.Release(); }
    }

    private async Task<Document> ReadAsync()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak")) throw new InvalidDataException("バンクが見つかりません。直前のバックアップから復元してください。");
            return new(1, []);
        }
        return Parse(await ReadBoundedAsync(path));
    }

    private static async Task<byte[]> ReadBoundedAsync(string file)
    {
        await using var stream = File.OpenRead(file);
        if (stream.Length > MaximumBackupBytes) throw new InvalidDataException("バンクファイルが大きすぎます。");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private Document Parse(byte[] data)
    {
        if (data.Length > MaximumBackupBytes) throw new InvalidDataException("バックアップが大きすぎます。");
        Document? doc;
        try { doc = JsonSerializer.Deserialize<Document>(data); }
        catch (JsonException ex) { throw new InvalidDataException("バンクの形式が不正です。ファイルは変更していません。", ex); }
        if (doc is null || doc.Version != 1 || doc.Entries is null || doc.Entries.Count > MaximumEntries)
            throw new InvalidDataException("対応していないバンク形式です。");
        var ids = new HashSet<Guid>();
        for (var index = 0; index < doc.Entries.Count; index++)
        {
            var entry = doc.Entries[index];
            if (entry is null || entry.Id == Guid.Empty || !ids.Add(entry.Id) || entry.Data is null ||
                string.IsNullOrWhiteSpace(entry.SourceGame) || entry.SourceTrainer is null)
                throw new InvalidDataException("バンクの記録が不正です。");
            // Canonicalize so decrypted/imported representations cannot bypass deduplication.
            doc.Entries[index] = entry with { Data = codec.Decode(entry.Data).StoredData };
        }
        return doc;
    }

    private async Task WriteAsync(Document doc, bool keepPrevious = true)
    {
        var bytes = Serialize(doc);
        if (bytes.Length > MaximumBackupBytes) throw new InvalidDataException("バンクの容量上限に達しています。");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (keepPrevious && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private BankItem ToItem(BankEntry entry) => new(entry, codec.Decode(entry.Data));
    private static string Fingerprint(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static byte[] Serialize(Document doc) => JsonSerializer.SerializeToUtf8Bytes(doc);
}
