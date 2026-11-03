using System.Text;
using System.Text.Json;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PKHeX.Core;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class RegisteredSavesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-library-tests-" + Guid.NewGuid());
    private LocalRegisteredSaves Registry() => new(root);
    private static SaveSnapshot Save(string game = "SS", ushort species = 152)
    {
        var p = new PK4 { Species = species, CurrentLevel = 10 };
        var data = new byte[p.SIZE_STORED]; p.WriteEncryptedDataStored(data);
        var pokemon = new Gen4PokemonCodec().Decode(data);
        return new("TEST", game, [pokemon], []);
    }

    [Fact]
    public async Task SsAndHgRemainRegisteredAfterRestartAndReopeningDoesNotDuplicate()
    {
        var registry = Registry();
        await registry.RegisterAsync("ss", Save());
        await registry.RegisterAsync("hg", Save("HG", 155));
        await registry.RegisterAsync("ss", Save() with { TrainerName = "UPDATED" });
        var saved = await Registry().ListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Equal("UPDATED", Assert.Single(saved, s => s.DocumentId == "ss").Trainer);
        Assert.Equal("HG", Assert.Single(saved, s => s.DocumentId == "hg").Game);
        // Different files with matching game and trainer metadata must remain distinct.
        await registry.RegisterAsync("ss-other", Save());
        Assert.Equal(3, (await Registry().ListAsync()).Count);
    }

    [Fact]
    public async Task RebindMergesExistingReferenceAndRemovalOnlyChangesRegistry()
    {
        var registry = Registry();
        await registry.RegisterAsync("ss", Save()); await registry.RegisterAsync("hg", Save("HG"));
        await registry.RebindAsync("ss", "hg", Save("HG"));
        Assert.Equal("hg", Assert.Single(await Registry().ListAsync()).DocumentId);
        await registry.RemoveAsync("hg");
        Assert.Empty(await Registry().ListAsync());
    }

    [Fact]
    public async Task ExportImportPreservesReferencesAndFormatWithoutDuplicatingOrReplacingNewerMetadata()
    {
        var registry = Registry();
        await registry.RegisterAsync("ss", Save() with { FormatKey = "future-reader" });
        var backup = await registry.ExportAsync();
        var restored = new LocalRegisteredSaves(Path.Combine(root, "restored"));
        Assert.Equal(1, await restored.ImportAsync(backup));
        await restored.RegisterAsync("ss", Save() with { TrainerName = "NEW" });
        Assert.Equal(0, await restored.ImportAsync(backup));
        Assert.Equal("NEW", Assert.Single(await restored.ListAsync()).Trainer);
        Assert.Equal("future-reader", Assert.Single(await registry.ListAsync()).FormatKey);
    }

    [Theory]
    [InlineData("bad json")]
    [InlineData("null")]
    [InlineData("{\"Version\":999,\"Saves\":[]}")]
    [InlineData("{\"Version\":1,\"Saves\":[null]}")]
    public async Task InvalidImportNeverChangesExistingRegistrations(string input)
    {
        var registry = Registry(); await registry.RegisterAsync("ss", Save());
        var before = await registry.ExportAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.ImportAsync(Encoding.UTF8.GetBytes(input)));
        Assert.Equal(before, await registry.ExportAsync());
    }

    [Fact]
    public async Task DuplicateAndExcessiveImportsAreRejectedAtomically()
    {
        var registry = Registry(); await registry.RegisterAsync("ss", Save());
        var before = await registry.ExportAsync();
        var entry = Assert.Single(await registry.ListAsync());
        byte[] Doc(IEnumerable<RegisteredSave> records) => JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Saves = records });
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.ImportAsync(Doc([entry, entry])));
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.ImportAsync(Doc(Enumerable.Range(0, 101).Select(i => entry with { DocumentId = i.ToString() }))));
        Assert.Equal(before, await registry.ExportAsync());
    }

    [Fact]
    public async Task DamagedRegistryIsNotSilentlyReset()
    {
        var registry = Registry(); await registry.RegisterAsync("ss", Save());
        var path = Path.Combine(root, "registered-saves.json"); await File.WriteAllTextAsync(path, "damaged");
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.ListAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RegisterAsync("hg", Save("HG")));
        Assert.Equal("damaged", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConcurrentRegistrationsAreSerialized()
    {
        var registry = Registry();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => registry.RegisterAsync(i.ToString(), Save())));
        Assert.Equal(10, (await Registry().ListAsync()).Count);
    }

    [Fact]
    public async Task LibraryReadsAllFilesFreshWithoutWritingAndKeepsFailedReference()
    {
        var registry = Registry(); var files = new Documents(); var reader = new Reader();
        await registry.RegisterAsync("ss", reader.Read([1])); await registry.RegisterAsync("hg", reader.Read([2]));
        var library = new RegisteredSaveLibraryReader(Registry(), files, reader);
        var dex = new CollectionDex();
        var first = dex.BuildAll(await library.ReadAllAsync(), []);
        Assert.Equal(2, first.SaveCount); Assert.Equal(2, first.Owned(DexVariant.All));
        files.Data["ss"] = [3]; // The game changed; an old ownership cache must not be reused.
        var updated = dex.BuildAll(await library.ReadAllAsync(), []);
        Assert.Equal(0, updated.Entries[151].Count(DexVariant.All));
        Assert.Equal(1, updated.Entries[157].Count(DexVariant.All));
        files.Data.Remove("hg");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => library.ReadAllAsync());
        Assert.Contains("HG", error.Message);
        Assert.Equal(2, (await Registry().ListAsync()).Count);
        Assert.Equal(0, files.Writes);
        files.Data["new-hg"] = [2];
        await registry.RebindAsync("hg", "new-hg", reader.Read([2]));
        Assert.Equal(2, (await library.ReadAllAsync()).Count);
    }

    private sealed class Reader : PokemonMove.Domain.ISaveReader
    {
        public SaveSnapshot Read(byte[] data) => data[0] switch { 1 => Save(), 2 => Save("HG", 155), 3 => Save("SS", 158), _ => throw new InvalidDataException() };
    }
    private sealed class Documents : ISaveDocuments
    {
        public Dictionary<string, byte[]> Data { get; } = new() { ["ss"] = [1], ["hg"] = [2] };
        public int Writes;
        public Task<ISaveDocument?> PickAsync() => Task.FromResult<ISaveDocument?>(null);
        public ISaveDocument Open(string id) => Data.ContainsKey(id) ? new Document(this, id) : throw new IOException("missing file");
        private sealed class Document(Documents owner, string id) : ISaveDocument
        {
            public string Id => id;
            public Task<byte[]> ReadAsync() => Task.FromResult(owner.Data[id].ToArray());
            public Task WriteAsync(byte[] data) { owner.Writes++; throw new InvalidOperationException("Collection must not write"); }
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
