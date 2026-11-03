using System.Security.Cryptography;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class CrossGameTransferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-crossgame-" + Guid.NewGuid());
    private LocalBankStore Bank() => new(root, new Gen4PokemonCodec(), new Gen4SaveEditor());
    private static readonly GameVersion[] Versions = [GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS];
    public static IEnumerable<object[]> Pairs => from source in Versions from target in Versions select new object[] { source, target };
    private sealed class Document(string id, byte[] data) : ISaveDocument
    {
        public string Id => id;
        public byte[] Data = data;
        public bool LoseAcknowledgement;
        public int Writes;
        public Task<byte[]> ReadAsync() => Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] bytes)
        {
            Data = bytes.ToArray(); Writes++;
            if (LoseAcknowledgement) throw new IOException("write acknowledged too late");
            return Task.CompletedTask;
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task MovesAcrossAllSupportedGamesWithoutDuplicatingAndAggregatesCollection(GameVersion sourceGame, GameVersion targetGame)
    {
        var source = new Document("source", Gen4Fixtures.Create(sourceGame, dsv: true));
        var target = new Document("target", Gen4Fixtures.Create(targetGame, dsv: true, boxPokemon: new PK4 { Species = 25, PID = 234, CurrentLevel = 10 }));
        var reader = new Gen4SaveReader(); var bank = Bank(); var dex = new CollectionDex();
        var original = source.Data.ToArray(); var targetOriginal = target.Data.ToArray();
        var pokemon = reader.Read(original).Boxes[17].Slots[29];
        var initial = dex.BuildAll([reader.Read(source.Data), reader.Read(target.Data)], await bank.ListAsync());
        await bank.MoveIntoBankAsync(source, new(false, 17, 29), SHA256.HashData(source.Data));
        Assert.Equal((ushort)0, reader.Read(source.Data).Boxes[17].Slots[29].Species);
        Assert.Equal(original, (await bank.LastSaveBackupAsync()).Data);
        var entry = Assert.Single(await Bank().ListAsync());
        Assert.Equal(pokemon.StoredData, entry.Pokemon.StoredData); Assert.True(entry.Entry.TransferEligible);
        var deposit = dex.BuildAll([reader.Read(source.Data), reader.Read(target.Data)], await bank.ListAsync());
        Assert.Equal(initial.Owned(DexVariant.All), deposit.Owned(DexVariant.All));
        Assert.Equal(1, deposit.Entries[386].BankCount); Assert.Equal(0, deposit.Entries[386].BoxCount);
        await bank.MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), SHA256.HashData(target.Data));
        Assert.Empty(await Bank().ListAsync());
        Assert.Equal(targetOriginal, (await bank.LastSaveBackupAsync()).Data);
        var moved = reader.Read(target.Data).Boxes[0].Slots[0];
        Assert.Equal(pokemon.StoredData, moved.StoredData);
        Assert.Equal(pokemon.Details, moved.Details);
        var final = dex.BuildAll([reader.Read(source.Data), reader.Read(target.Data)], await bank.ListAsync());
        Assert.Equal(initial.Owned(DexVariant.All), final.Owned(DexVariant.All));
        Assert.Equal(0, final.Entries[386].BankCount); Assert.Equal(1, final.Entries[386].BoxCount);
        Assert.Equal(reader.Read(target.Data).Game, Assert.Single(final.Entries[386].Saves).Game);
        Assert.Equal(original[0x80000..], source.Data[0x80000..]);
        Assert.Equal(targetOriginal[0x80000..], target.Data[0x80000..]);
    }

    [Theory]
    [InlineData(GameVersion.D)]
    [InlineData(GameVersion.Pt)]
    public async Task CrossGameInterruptedWithdrawalKeepsBankLockedUntilRecovery(GameVersion targetGame)
    {
        var source = new Document("ss", Gen4Fixtures.Create(GameVersion.SS));
        var target = new Document("sinnoh", Gen4Fixtures.Create(targetGame));
        await Bank().MoveIntoBankAsync(source, new(false, 17, 29), SHA256.HashData(source.Data));
        var entry = Assert.Single(await Bank().ListAsync());
        target.LoseAcknowledgement = true;
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), SHA256.HashData(target.Data)));
        Assert.True(await Bank().HasPendingMoveAsync());
        target.LoseAcknowledgement = false;
        await Bank().RecoverMoveAsync(target);
        Assert.False(await Bank().HasPendingMoveAsync()); Assert.Empty(await Bank().ListAsync());
        Assert.Equal(entry.Pokemon.StoredData, new Gen4SaveReader().Read(target.Data).Boxes[0].Slots[0].StoredData);
        Assert.Equal(1, target.Writes);
    }

    [Fact]
    public async Task UnsupportedDestinationNeverWritesAndKeepsPokemonInBank()
    {
        var source = new Document("pt", Gen4Fixtures.Create(GameVersion.Pt,
            boxPokemon: new PK4 { Species = 487, Form = 1, HeldItem = 112, CurrentLevel = 30 }));
        var target = new Document("dp", Gen4Fixtures.Create(GameVersion.D));
        var before = target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source, new(false, 17, 29), SHA256.HashData(source.Data));
        var entry = Assert.Single(await Bank().ListAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), SHA256.HashData(target.Data)));
        Assert.Equal(before, target.Data); Assert.Equal(0, target.Writes);
        Assert.Equal(entry.Entry.Id, Assert.Single(await Bank().ListAsync()).Entry.Id);
        Assert.False(await Bank().HasPendingMoveAsync());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
