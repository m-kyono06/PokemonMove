using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class LocalBankStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PokemonMove-bank-tests-" + Guid.NewGuid());
    private readonly HgssPokemonCodec codec = new();
    private LocalBankStore Store() => new(directory, codec);
    private PokemonSummary Pokemon(ushort species = 152)
    {
        var pk = new PK4 { Species = species, CurrentLevel = 12, Nickname = "なかま", Move1 = 33,
            IV_HP = 31, EV_ATK = 20, PID = 12345, ID32 = 54321, OriginalTrainerName = "TEST" };
        var data = new byte[pk.SIZE_STORED];
        pk.WriteEncryptedDataStored(data);
        return codec.Decode(data);
    }

    [Fact]
    public async Task EmptyBankNeedsNoSaveFile()
    {
        Assert.Empty(await Store().ListAsync());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task PersistsFullPokemonAndSourceAcrossNewStoreInstance()
    {
        var pokemon = Pokemon();
        var before = pokemon.StoredData.ToArray();
        var result = await Store().AddAsync(pokemon, "ソウルシルバー", "コトネ");
        Assert.True(result.Added);
        var read = Assert.Single(await Store().ListAsync());
        Assert.Equal(result.Item.Entry.Id, read.Entry.Id);
        Assert.Equal("ソウルシルバー", read.Entry.SourceGame);
        Assert.Equal("コトネ", read.Entry.SourceTrainer);
        Assert.Equal(before, read.Pokemon.StoredData);
        Assert.Equal(before, pokemon.StoredData);
        var pk = new PK4(read.Entry.Data.ToArray());
        Assert.Equal(31, pk.IV_HP);
        Assert.Equal(20, pk.EV_ATK);
        Assert.Equal(12345u, pk.PID);
        Assert.Equal(54321u, pk.ID32);
        Assert.Equal("TEST", pk.OriginalTrainerName);
        Assert.Equal((ushort)33, pk.Move1);
        Assert.True(pk.ChecksumValid);
    }

    [Fact]
    public async Task ConcurrentCopiesDoNotCreateDuplicates()
    {
        var store = Store();
        var pokemon = Pokemon();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.AddAsync(pokemon, "SS", "TEST")));
        Assert.Single(results, r => r.Added);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task RejectsInvalidEntityWithoutCreatingBank()
    {
        var pokemon = Pokemon();
        pokemon.StoredData[20] ^= 0xFF;
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().AddAsync(pokemon, "SS", "TEST"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ExportImportMergesAndDeduplicatesWithoutReplacingExistingEntries()
    {
        var store = Store();
        await store.AddAsync(Pokemon(), "SS", "TEST");
        var backup = await store.ExportAsync();
        var other = new LocalBankStore(Path.Combine(directory, "other"), codec);
        await other.AddAsync(Pokemon(158), "HG", "OTHER");
        Assert.Equal(1, await other.ImportAsync(backup));
        Assert.Equal(0, await other.ImportAsync(backup));
        Assert.Equal(2, (await other.ListAsync()).Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Entries\":[]}")]
    [InlineData("{\"Version\":1,\"Entries\":[null]}")]
    public async Task InvalidImportPreservesExistingBank(string input)
    {
        var store = Store();
        await store.AddAsync(Pokemon(), "SS", "TEST");
        var before = await store.ExportAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(System.Text.Encoding.UTF8.GetBytes(input)));
        Assert.Equal(before, await store.ExportAsync());
    }

    [Fact]
    public async Task DoesNotSilentlyResetDamagedBankAndCanRestorePreviousState()
    {
        var store = Store();
        await store.AddAsync(Pokemon(), "SS", "TEST");
        await store.AddAsync(Pokemon(158), "SS", "TEST");
        await File.WriteAllTextAsync(Path.Combine(directory, "bank.json"), "damaged");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync(Pokemon(155), "SS", "TEST"));
        Assert.Equal("damaged", await File.ReadAllTextAsync(Path.Combine(directory, "bank.json")));
        await store.RestorePreviousAsync();
        Assert.Equal((ushort)152, Assert.Single(await store.ListAsync()).Pokemon.Species);
    }

    [Fact]
    public async Task FailedWriteLeavesPreviousBankReadable()
    {
        var store = Store();
        await store.AddAsync(Pokemon(), "SS", "TEST");
        // Make the backup destination unusable, forcing a failure before atomic replacement.
        Directory.CreateDirectory(Path.Combine(directory, "bank.json.bak"));
        var error = await Record.ExceptionAsync(() => store.AddAsync(Pokemon(158), "SS", "TEST"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal((ushort)152, Assert.Single(await store.ListAsync()).Pokemon.Species);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task MutatingReturnedDataDoesNotChangeStoredPokemon()
    {
        var store = Store();
        var result = await store.AddAsync(Pokemon(), "SS", "TEST");
        result.Item.Entry.Data[0] ^= 0xFF;
        var read = Assert.Single(await store.ListAsync());
        Assert.Equal((ushort)152, read.Pokemon.Species);
    }

    [Fact]
    public async Task InvalidEntityInImportRejectsWholeBackup()
    {
        var store = Store();
        await store.AddAsync(Pokemon(), "SS", "TEST");
        var backup = await store.ExportAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(backup)!;
        var entries = node["Entries"]!.AsArray();
        entries[0]!["Data"] = Convert.ToBase64String(Pokemon(158).StoredData);
        var invalid = entries[0]!.DeepClone();
        invalid["Id"] = Guid.NewGuid().ToString();
        invalid["Data"] = Convert.ToBase64String(new byte[136]);
        entries.Add(invalid);
        var before = await store.ExportAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.Equal(before, await store.ExportAsync());
    }

    [Fact]
    public async Task DifferentIndividualsOfSameSpeciesAreKept()
    {
        var first = Pokemon();
        var other = new PK4(first.StoredData.ToArray()) { PID = 998877 };
        var bytes = new byte[other.SIZE_STORED];
        other.WriteEncryptedDataStored(bytes);
        var store = Store();
        Assert.True((await store.AddAsync(first, "SS", "TEST")).Added);
        Assert.True((await store.AddAsync(codec.Decode(bytes), "SS", "TEST")).Added);
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
