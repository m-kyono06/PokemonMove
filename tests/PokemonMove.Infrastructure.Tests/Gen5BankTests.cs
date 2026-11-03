using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class Gen5BankTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-gen5-" + Guid.NewGuid());
    private LocalBankStore Bank() => new(root, new PokemonCodec(), new DsSaveEditor());
    private static readonly DateOnly TransferDate = new(2026, 11, 3);
    private sealed class Document(string id, byte[] data) : ISaveDocument
    {
        public string Id => id;
        public byte[] Data = data;
        public string Failure = "";
        public int Writes;
        public Task<byte[]> ReadAsync() => Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] bytes)
        {
            Writes++;
            if (Failure == "before") throw new IOException("before");
            if (Failure == "partial") { Data = bytes[..100]; throw new IOException("partial"); }
            Data = bytes.ToArray();
            if (Failure == "after") throw new IOException("lost acknowledgement");
            return Task.CompletedTask;
        }
    }
    private static readonly GameVersion[] Versions = [GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2];
    public static IEnumerable<object[]> Pairs => from source in Versions from target in Versions select new object[] { source, target };
    public static IEnumerable<object[]> ForwardPairs => from source in new[] { GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS }
        from target in Versions select new object[] { source, target };
    private static byte[] Hash(byte[] data) => SHA256.HashData(data);
    private static SaveSnapshot View(byte[] data) => new DsSaveReader().Read(data);

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task MovesBetweenAllFourGamesWithExactDataBackupAndCollectionCounts(GameVersion sourceVersion, GameVersion targetVersion)
    {
        var source = new Document("source", Gen5Fixtures.Create(sourceVersion, dsv: true));
        var target = new Document("target", Gen5Fixtures.Create(targetVersion, dsv: true,
            boxPokemon: new PK5 { Species = 25, CurrentLevel = 10, PID = 876 }));
        var original = source.Data.ToArray(); var targetOriginal = target.Data.ToArray();
        var pokemon = View(original).Boxes[23].Slots[29];
        var bank = Bank();
        await bank.MoveIntoBankAsync(source, new(false, 23, 29), Hash(source.Data));
        Assert.Equal((ushort)0, View(source.Data).Boxes[23].Slots[29].Species);
        Assert.Equal(original, (await bank.LastSaveBackupAsync()).Data);
        var entry = Assert.Single(await Bank().ListAsync()); Assert.Equal("pk5", entry.Entry.FormatKey);
        Assert.Equal(pokemon.StoredData, entry.Pokemon.StoredData);
        var backup = await bank.ExportAsync();
        var dex = new CollectionDex(); var during = dex.BuildAll([View(source.Data), View(target.Data)], await bank.ListAsync());
        Assert.Equal(1, during.Entries[648].BankCount); Assert.Equal(0, during.Entries[648].BoxCount);
        await bank.MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(target.Data));
        Assert.Empty(await Bank().ListAsync());
        Assert.Equal(targetOriginal, (await bank.LastSaveBackupAsync()).Data);
        Assert.Equal(pokemon.StoredData, View(target.Data).Boxes[0].Slots[0].StoredData);
        Assert.Equal(pokemon.Details, View(target.Data).Boxes[0].Slots[0].Details);
        var after = dex.BuildAll([View(source.Data), View(target.Data)], await bank.ListAsync());
        Assert.Equal(during.Owned(DexVariant.All), after.Owned(DexVariant.All));
        Assert.Equal(0, after.Entries[648].BankCount); Assert.Equal(1, after.Entries[648].BoxCount);
        Assert.Equal(0, await bank.ImportAsync(backup));
        Assert.Equal(original[0x80000..], source.Data[0x80000..]);
        Assert.Equal(targetOriginal[0x80000..], target.Data[0x80000..]);
    }

    [Theory]
    [MemberData(nameof(ForwardPairs))]
    public async Task ForwardMoveConsumesPk4AndCreatesOnlyPk5WhichCannotReturn(GameVersion sourceVersion, GameVersion targetVersion)
    {
        var source = new Document("old", Gen4Fixtures.Create(sourceVersion));
        var target = new Document("new", Gen5Fixtures.Create(targetVersion));
        var bank = Bank();
        await bank.MoveIntoBankAsync(source, new(false, 17, 29), Hash(source.Data));
        var entry = Assert.Single(await bank.ListAsync()); Assert.Equal("pk4", entry.Entry.FormatKey);
        var backup = await bank.ExportAsync();
        var plan = new PokemonTransfer().Prepare(entry.Pokemon, View(target.Data).FormatKey, TransferDate);
        await bank.MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(target.Data), TransferDate);
        Assert.Empty(await Bank().ListAsync()); Assert.Equal(0, await Bank().ImportAsync(backup));
        var moved = View(target.Data).Boxes[0].Slots[0]; Assert.Equal("pk5", moved.FormatKey);
        Assert.Equal(plan.Pokemon.StoredData, moved.StoredData);
        await bank.MoveIntoBankAsync(target, new(false, 0, 0), Hash(target.Data));
        var newer = Assert.Single(await Bank().ListAsync()); Assert.Equal("pk5", newer.Entry.FormatKey);
        Assert.NotEqual(entry.Entry.Id, newer.Entry.Id);
        var before = source.Data.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveToSaveAsync(newer.Entry.Id, source, new(false, 17, 29), Hash(source.Data)));
        Assert.Equal(before, source.Data); Assert.Single(await bank.ListAsync());
    }

    [Theory]
    [InlineData("before", false)]
    [InlineData("after", false)]
    [InlineData("partial", false)]
    [InlineData("after", true)]
    public async Task InterruptedForwardConversionRecoversUsingPersistedDateAndPreventsReplay(string failure, bool restore)
    {
        var source = new Document("ss", Gen4Fixtures.Create(GameVersion.SS));
        var target = new Document("bw", Gen5Fixtures.Create(GameVersion.B)); var original = target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source, new(false, 17, 29), Hash(source.Data));
        var entry = Assert.Single(await Bank().ListAsync()); var backup = await Bank().ExportAsync();
        target.Failure = failure;
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(target.Data), TransferDate));
        Assert.True(await Bank().HasPendingMoveAsync());
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "bank.json")))!;
        Assert.Equal("2026-11-03", journal["Pending"]!["TransferDate"]!.GetValue<string>());
        target.Failure = "";
        if (failure == "partial")
        {
            await Assert.ThrowsAsync<SaveRecoveryRequiredException>(() => Bank().RecoverMoveAsync(target));
            await Bank().RecoverMoveAsync(target, restoreOriginal: true);
        }
        else await Bank().RecoverMoveAsync(target, restoreOriginal: restore);
        Assert.False(await Bank().HasPendingMoveAsync());
        // A fully verified destination is committed even if restoreOriginal was requested.
        if (failure != "after")
        {
            Assert.Equal(original, target.Data); Assert.Single(await Bank().ListAsync());
        }
        else
        {
            Assert.Empty(await Bank().ListAsync());
            var moved = View(target.Data).Boxes[0].Slots[0];
            Assert.Equal(TransferDate, new PK5(moved.StoredData.ToArray()).MetDate);
            Assert.Equal(0, await Bank().ImportAsync(backup));
        }
    }

    [Fact]
    public async Task InterruptedGen5DepositRecoversAndRetainsItsFormat()
    {
        var source = new Document("b2", Gen5Fixtures.Create(GameVersion.B2)) { Failure = "after" };
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveIntoBankAsync(source, new(false, 23, 29), Hash(source.Data)));
        source.Failure = ""; await Bank().RecoverMoveAsync(source);
        var item = Assert.Single(await Bank().ListAsync()); Assert.Equal("pk5", item.Entry.FormatKey);
        Assert.Equal((ushort)649, item.Pokemon.Species); Assert.Equal((ushort)0, View(source.Data).Boxes[23].Slots[29].Species);
    }

    [Fact]
    public async Task LegacyBankAndPendingMoveCanBeReadAndMixedWithGen5WithoutLosingEligibility()
    {
        var source = new Document("ss", Gen4Fixtures.Create(GameVersion.SS)) { Failure = "before" };
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveIntoBankAsync(source, new(false, 17, 29), Hash(source.Data)));
        var path = Path.Combine(root, "bank.json"); var journal = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        journal["Version"] = 2; journal["Pending"]!["Entry"]!.AsObject().Remove("FormatKey");
        journal["Pending"]!.AsObject().Remove("TransferDate"); await File.WriteAllTextAsync(path, journal.ToJsonString());
        source.Failure = ""; await Bank().RecoverMoveAsync(source);
        await Bank().MoveIntoBankAsync(source, new(false, 17, 29), Hash(source.Data));
        var entry = Assert.Single(await Bank().ListAsync()); var document = JsonNode.Parse(await Bank().ExportAsync())!;
        document["Version"] = 2; document["Entries"]![0]!.AsObject().Remove("FormatKey");
        await File.WriteAllTextAsync(path, document.ToJsonString());
        Assert.Equal("pk4", Assert.Single(await Bank().ListAsync()).Entry.FormatKey);
        var gen5 = new Document("w2", Gen5Fixtures.Create(GameVersion.W2));
        await Bank().MoveIntoBankAsync(gen5, new(false, 23, 29), Hash(gen5.Data));
        var exported = await Bank().ExportAsync(); var node = JsonNode.Parse(exported)!;
        Assert.Equal(3, node["Version"]!.GetValue<int>());
        Assert.Equal(2, (await Bank().ListAsync()).Count);
        Assert.Contains(await Bank().ListAsync(), item => item.Entry.Id == entry.Entry.Id && item.Entry.TransferEligible);
        var other = new LocalBankStore(Path.Combine(root, "import"), new PokemonCodec(), new DsSaveEditor());
        Assert.Equal(2, await other.ImportAsync(exported)); Assert.Equal(0, await other.ImportAsync(exported));
        Assert.Contains(await other.ListAsync(), item => item.Entry.FormatKey == "pk5" && item.Entry.TransferEligible);
    }

    [Fact]
    public async Task UnknownFormatInBackupIsRejectedWithoutChangingBank()
    {
        var source = new Document("bw", Gen5Fixtures.Create(GameVersion.B));
        await Bank().MoveIntoBankAsync(source, new(false, 23, 29), Hash(source.Data));
        var before = await Bank().ExportAsync(); var node = JsonNode.Parse(before)!;
        node["Entries"]![0]!["FormatKey"] = "pk6";
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().ImportAsync(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.Equal(before, await Bank().ExportAsync());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
