using System.Security.Cryptography;
using System.Text.Json;
using PokemonMove.Domain;

namespace PokemonMove.Infrastructure;

/// <summary>App-private bank. One instance owns all access; reads never erase damaged files.</summary>
public sealed class LocalBankStore(string directory, IPokemonCodec codec, ISaveEditor? editor = null) : IBankStore
{
    public const int MaximumBackupBytes = 8 * 1024 * 1024;
    public const int MaximumEntries = 10000;
    private readonly string path = Path.Combine(directory, "bank.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record PendingMove(Guid Id, string DocumentId, bool Deposit, PokemonLocation Location,
        BankEntry Entry, byte[] Before, byte[] After);
    private sealed record Document(int Version, List<BankEntry> Entries, List<Guid>? Spent = null, PendingMove? Pending = null);

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
            EnsureIdle(doc);
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
        try { var doc = await ReadAsync(); EnsureIdle(doc); return Serialize(doc); }
        finally { gate.Release(); }
    }

    public async Task<int> ImportAsync(byte[] backup)
    {
        var imported = Parse(backup);
        await gate.WaitAsync();
        try
        {
            var current = await ReadAsync();
            EnsureIdle(current);
            EnsureIdle(imported);
            var spent = current.Spent!.Union(imported.Spent!).ToList();
            current.Entries.RemoveAll(e => spent.Contains(e.Id));
            var hashes = current.Entries.Select(e => Fingerprint(e.Data)).ToHashSet();
            var added = 0;
            foreach (var entry in imported.Entries)
            {
                if (spent.Contains(entry.Id) || !hashes.Add(Fingerprint(entry.Data))) continue;
                if (current.Entries.Any(e => e.Id == entry.Id)) throw new InvalidDataException("バックアップ内の識別情報が重複しています。");
                current.Entries.Add(entry with { Data = entry.Data.ToArray() });
                added++;
            }
            if (current.Entries.Count > MaximumEntries) throw new InvalidDataException("バンクの保管上限を超えています。");
            if (spent.Count != 0 || current.Entries.Any(e => e.TransferEligible)) await MarkTransfersAsync();
            await WriteAsync(current with { Spent = spent });
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
            Document current;
            try { current = await ReadAsync(); }
            catch (InvalidDataException) when (!File.Exists(Path.Combine(directory, "moves.lock")) && doc.Spent!.Count == 0 && doc.Pending is null && !doc.Entries.Any(e => e.TransferEligible))
            { current = doc; }
            EnsureIdle(current);
            EnsureIdle(doc);
            if (current.Spent!.Count != 0 || current.Entries.Any(e => e.TransferEligible) || doc.Entries.Any(e => e.TransferEligible))
                throw new InvalidDataException("移動を含むバンクは巻き戻せません。バックアップの取り込みを使用してください。");
            await WriteAsync(doc, keepPrevious: false);
        }
        finally { gate.Release(); }
    }

    public async Task MoveIntoBankAsync(ISaveDocument source, PokemonLocation location, byte[] expectedHash)
    {
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync(); EnsureIdle(doc);
            var before = await source.ReadAsync(); CheckExpected(before, expectedHash);
            var edit = RequireEditor().Remove(before, location);
            var fingerprint = Fingerprint(edit.Pokemon.StoredData);
            var matching = doc.Entries.FirstOrDefault(e => Fingerprint(e.Data) == fingerprint);
            if (matching?.TransferEligible == true) throw new InvalidDataException("同じデータはすでに移動保管されています。");
            if (matching is null && doc.Entries.Count >= MaximumEntries) throw new InvalidDataException("バンクが満杯です。");
            var entry = new BankEntry(matching?.Id ?? Guid.NewGuid(), edit.Game, edit.Trainer, DateTimeOffset.UtcNow, edit.Pokemon.StoredData) { TransferEligible = true };
            await ExecuteAsync(doc, new(Guid.NewGuid(), source.Id, true, location, entry, before, edit.After), source);
        }
        finally { gate.Release(); }
    }

    public async Task MoveToSaveAsync(Guid entryId, ISaveDocument target, PokemonLocation location, byte[] expectedHash)
    {
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync(); EnsureIdle(doc);
            var entry = doc.Entries.SingleOrDefault(e => e.Id == entryId) ?? throw new InvalidDataException("バンクに見つかりません。");
            if (!entry.TransferEligible) throw new InvalidDataException("旧版のコピー保管データです。元セーブの同じ個体を移動保管し直してください。");
            var before = await target.ReadAsync(); CheckExpected(before, expectedHash);
            var edit = RequireEditor().Insert(before, location, entry.Data);
            await ExecuteAsync(doc, new(Guid.NewGuid(), target.Id, false, location, entry, before, edit.After), target);
        }
        finally { gate.Release(); }
    }

    private async Task ExecuteAsync(Document doc, PendingMove pending, ISaveDocument file)
    {
        // Persist ownership intent and both exact byte images before touching the external document.
        doc = doc with { Pending = pending };
        await WriteAsync(doc);
        await MarkTransfersAsync();
        await BackupAsync(pending);
        CheckExpected(await file.ReadAsync(), SHA256.HashData(pending.Before));
        await file.WriteAsync(pending.After);
        CheckExpected(await file.ReadAsync(), SHA256.HashData(pending.After));
        await CommitAsync(doc);
    }

    public async Task RecoverMoveAsync(ISaveDocument document, bool restoreOriginal = false)
    {
        await gate.WaitAsync();
        try
        {
            var doc = await ReadAsync();
            var pending = doc.Pending ?? throw new InvalidDataException("中断した移動はありません。");
            if (document.Id != pending.DocumentId) throw new InvalidDataException("中断した移動で選択した同じセーブを選んでください。");
            byte[]? current;
            try { current = await document.ReadAsync(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                if (!restoreOriginal) throw new SaveRecoveryRequiredException("セーブを読み取れません。権限と保存場所を確認してください。必要なら移動前のバックアップへの復旧を選択してください。");
                current = null;
            }
            if (current is not null && current.AsSpan().SequenceEqual(pending.After)) { await CommitAsync(doc); return; }
            if (current is null || !current.AsSpan().SequenceEqual(pending.Before))
            {
                if (!restoreOriginal) throw new SaveRecoveryRequiredException("セーブが中間状態または変更済みです。移動前のバックアップからの復旧を選択してください。");
                await BackupAsync(pending);
                await document.WriteAsync(pending.Before);
                CheckExpected(await document.ReadAsync(), SHA256.HashData(pending.Before));
            }
            await WriteAsync(doc with { Pending = null }); // External source unchanged/restored: bank ownership unchanged.
        }
        finally { gate.Release(); }
    }

    private async Task CommitAsync(Document doc)
    {
        var pending = doc.Pending!;
        if (pending.Deposit)
        {
            var hash = Fingerprint(pending.Entry.Data);
            doc.Entries.RemoveAll(e => e.Id == pending.Entry.Id || Fingerprint(e.Data) == hash);
            doc.Entries.Add(pending.Entry);
        }
        else
        {
            doc.Entries.RemoveAll(e => e.Id == pending.Entry.Id);
            doc.Spent!.Add(pending.Entry.Id);
        }
        await WriteAsync(doc with { Pending = null });
    }

    private async Task BackupAsync(PendingMove pending)
    {
        var root = Path.Combine(directory, "save-backups");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, pending.Id.ToString("N") + (pending.Before.Length == 0x80000 ? ".sav" : ".dsv"));
        if (File.Exists(file))
        {
            if (!(await File.ReadAllBytesAsync(file)).AsSpan().SequenceEqual(pending.Before)) throw new InvalidDataException("セーブのバックアップが一致しません。");
            return;
        }
        var temporary = file + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(pending.Before);
                stream.Flush(true);
            }
            File.Move(temporary, file, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<bool> HasPendingMoveAsync()
    {
        await gate.WaitAsync();
        try { return (await ReadAsync()).Pending is not null; }
        finally { gate.Release(); }
    }

    public async Task<SaveBackup> LastSaveBackupAsync()
    {
        await gate.WaitAsync();
        try
        {
            var root = Path.Combine(directory, "save-backups");
            var file = Directory.Exists(root) ? Directory.GetFiles(root).Where(f => f.EndsWith(".sav") || f.EndsWith(".dsv")).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (file is null) throw new InvalidDataException("セーブのバックアップはまだありません。");
            return new("PokemonMove-before-" + Path.GetFileName(file), await File.ReadAllBytesAsync(file));
        }
        finally { gate.Release(); }
    }

    private async Task MarkTransfersAsync()
    {
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "moves.lock");
        await using var file = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        await file.WriteAsync(new byte[] { 1 });
        file.Flush(true);
    }

    private ISaveEditor RequireEditor() => editor ?? throw new InvalidDataException("移動用のエンジンが設定されていません。");
    private static void CheckExpected(byte[] data, byte[] expected)
    {
        if (!SHA256.HashData(data).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("セーブの内容が変わったか書き込みを確認できません。開き直してください。中断した移動がある場合は、バンク画面から先に復旧してください。");
    }
    private static void EnsureIdle(Document doc)
    {
        if (doc.Pending is not null) throw new InvalidDataException("中断した移動があります。先にバンク画面で復旧してください。");
    }

    private async Task<Document> ReadAsync()
    {
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak") || File.Exists(Path.Combine(directory, "moves.lock"))) throw new InvalidDataException("バンクが見つかりません。移動履歴を保護するため自動初期化しません。ファイルとバックアップを保持してください。");
            return new(2, [], []);
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
        if (doc is null || doc.Version is not (1 or 2) || doc.Entries is null || doc.Entries.Count > MaximumEntries)
            throw new InvalidDataException("対応していないバンク形式です。");
        var ids = new HashSet<Guid>();
        for (var index = 0; index < doc.Entries.Count; index++)
        {
            var entry = doc.Entries[index];
            if (entry is null || entry.Id == Guid.Empty || !ids.Add(entry.Id) || entry.Data is null ||
                string.IsNullOrWhiteSpace(entry.SourceGame) || entry.SourceTrainer is null)
                throw new InvalidDataException("バンクの記録が不正です。");
            // Canonicalize so decrypted/imported representations cannot bypass deduplication.
            doc.Entries[index] = entry with { Data = codec.Decode(entry.Data).StoredData, TransferEligible = doc.Version == 2 && entry.TransferEligible };
        }
        doc = doc with { Version = 2, Spent = doc.Spent ?? [] };
        if (doc.Spent.Any(id => id == Guid.Empty) || doc.Entries.Any(e => doc.Spent.Contains(e.Id)))
            throw new InvalidDataException("バンクの移動履歴が不正です。");
        if (doc.Pending is { } pending)
        {
            if (editor is null || pending.Id == Guid.Empty || string.IsNullOrWhiteSpace(pending.DocumentId) ||
                pending.Before is null || pending.After is null || pending.Entry is null || !pending.Entry.TransferEligible)
                throw new InvalidDataException("中断した移動の情報を確認できません。");
            var edit = pending.Deposit ? editor.Remove(pending.Before, pending.Location) : editor.Insert(pending.Before, pending.Location, pending.Entry.Data);
            if (!edit.After.AsSpan().SequenceEqual(pending.After) || !edit.Pokemon.StoredData.AsSpan().SequenceEqual(pending.Entry.Data))
                throw new InvalidDataException("中断した移動の整合性を確認できません。");
            if (!pending.Deposit && !doc.Entries.Any(e => e.Id == pending.Entry.Id && e.TransferEligible && e.Data.AsSpan().SequenceEqual(pending.Entry.Data)))
                throw new InvalidDataException("中断した引き出しの保管データが見つかりません。");
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
