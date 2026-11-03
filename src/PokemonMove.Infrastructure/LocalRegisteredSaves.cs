using System.Text.Json;
using PokemonMove.Domain;

namespace PokemonMove.Infrastructure;

/// <summary>Persistent document references. No cached ownership, no writes to the save documents.</summary>
public sealed class LocalRegisteredSaves(string directory) : IRegisteredSaves
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumSaves = 100;
    private readonly string path = Path.Combine(directory, "registered-saves.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Document(int Version, List<RegisteredSave> Saves);

    public async Task<IReadOnlyList<RegisteredSave>> ListAsync()
    {
        await gate.WaitAsync();
        try { return (await ReadAsync()).Saves.ToArray(); }
        finally { gate.Release(); }
    }
    public Task RegisterAsync(string documentId, SaveSnapshot save) => RebindAsync(documentId, documentId, save);

    public async Task RebindAsync(string oldDocumentId, string newDocumentId, SaveSnapshot save)
    {
        if (string.IsNullOrWhiteSpace(newDocumentId) || string.IsNullOrWhiteSpace(save.Game) || string.IsNullOrWhiteSpace(save.FormatKey) || save.TrainerName is null)
            throw new InvalidDataException("セーブの登録情報が不正です。");
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync();
            var existing = doc.Saves.FirstOrDefault(s => s.DocumentId == newDocumentId) ?? doc.Saves.FirstOrDefault(s => s.DocumentId == oldDocumentId);
            doc.Saves.RemoveAll(s => s.DocumentId == oldDocumentId || s.DocumentId == newDocumentId);
            doc.Saves.Add(new(newDocumentId, save.Game, save.TrainerName, existing?.RegisteredAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { FormatKey = save.FormatKey });
            if (doc.Saves.Count > MaximumSaves) throw new InvalidDataException("セーブの登録上限は100件です。");
            doc.Saves.Sort((a, b) => a.RegisteredAt.CompareTo(b.RegisteredAt));
            await WriteAsync(doc);
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(string documentId)
    {
        await gate.WaitAsync();
        try { var doc = await ReadAsync(); if (doc.Saves.RemoveAll(s => s.DocumentId == documentId) > 0) await WriteAsync(doc); }
        finally { gate.Release(); }
    }
    public async Task<byte[]> ExportAsync()
    {
        await gate.WaitAsync();
        try { return JsonSerializer.SerializeToUtf8Bytes(await ReadAsync()); }
        finally { gate.Release(); }
    }
    public async Task<int> ImportAsync(byte[] data)
    {
        var imported = Parse(data);
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync(); var ids = doc.Saves.Select(s => s.DocumentId).ToHashSet(StringComparer.Ordinal);
            var added = 0;
            foreach (var s in imported.Saves) if (ids.Add(s.DocumentId)) { doc.Saves.Add(s); added++; }
            if (doc.Saves.Count > MaximumSaves) throw new InvalidDataException("登録上限を超えています。");
            if (added > 0) await WriteAsync(doc);
            return added;
        }
        finally { gate.Release(); }
    }

    private async Task<Document> ReadAsync()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak")) throw new InvalidDataException("登録一覧が見つかりません。バックアップを確認してください。");
            return new(1, []);
        }
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("登録一覧が大きすぎます。");
        using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        return Parse(memory.ToArray());
    }
    private static Document Parse(byte[] data)
    {
        if (data.Length > MaximumBytes) throw new InvalidDataException("登録一覧が大きすぎます。");
        Document? doc;
        try { doc = JsonSerializer.Deserialize<Document>(data); }
        catch (JsonException ex) { throw new InvalidDataException("登録一覧の形式が不正です。ファイルは保持しています。", ex); }
        if (doc is null || doc.Version != 1 || doc.Saves is null || doc.Saves.Count > MaximumSaves)
            throw new InvalidDataException("対応していない登録一覧です。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in doc.Saves)
            if (s is null || string.IsNullOrWhiteSpace(s.DocumentId) || string.IsNullOrWhiteSpace(s.Game) || s.Trainer is null ||
                string.IsNullOrWhiteSpace(s.FormatKey) || !ids.Add(s.DocumentId))
                throw new InvalidDataException("登録一覧に不正または重複した記録があります。");
        return doc;
    }
    private async Task WriteAsync(Document doc)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("登録一覧の容量上限を超えています。");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await stream.WriteAsync(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
