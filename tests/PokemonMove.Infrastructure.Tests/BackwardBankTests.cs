using System.Security.Cryptography;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class BackwardBankTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 11, 16);
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-reverse-" + Guid.NewGuid());
    private LocalBankStore Bank() => new(root, new PokemonCodec(), new GameSaveEditor());
    private static byte[] Hash(byte[] data) => SHA256.HashData(data);
    private static SaveSnapshot Read(byte[] data) => new GameSaveReader().Read(data);
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
            if (Failure == "after") throw new IOException("acknowledgement");
            return Task.CompletedTask;
        }
    }
    private static readonly GameVersion[][] Games =
    [
        [GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS],
        [GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2],
        [GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS],
        [GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM],
    ];
    public static IEnumerable<object[]> Routes => from a in Enumerable.Range(1, 3)
        from b in Enumerable.Range(0, a) from source in Games[a] from target in Games[b] select new object[] { source, target };
    private static int Generation(GameVersion game) => Array.FindIndex(Games, games => games.Contains(game)) + 4;
    private static PKM Pokemon(GameVersion version)
    {
        PKM pk = Generation(version) switch { 5 => new PK5(), 6 => new PK6(), _ => new PK7() };
        pk.Version = version; pk.Species = 25; pk.Language = 1; pk.CurrentLevel = 50;
        pk.PID = 0xDEADBEEF; pk.ID32 = 1234; pk.Nature = Nature.Jolly; pk.Gender = 0;
        pk.Ability = 9; pk.AbilityNumber = 1; pk.Ball = 4;
        pk.OriginalTrainerName = "テスト"; pk.Nickname = "ピカ"; pk.IsNicknamed = true;
        pk.IV_HP = 31; pk.IV_ATK = 30; pk.EV_ATK = 252; pk.Move1 = 85;
        return pk;
    }
    private static byte[] Save(GameVersion game, PKM? pk = null) => Generation(game) switch
    {
        4 => Gen4Fixtures.Create(game, dsv: true, boxPokemon: (PK4?)pk),
        5 => Gen5Fixtures.Create(game, dsv: true, boxPokemon: (PK5?)pk),
        6 => Gen6Fixtures.Create(game, boxPokemon: (PK6?)pk),
        _ => Gen7Fixtures.Create(game, boxPokemon: (PK7?)pk),
    };
    private static PokemonLocation SourceLocation(GameVersion game) => new(false, Generation(game) switch { 5 => 23, 6 => 30, _ => 31 }, 29);

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AllNewerGamesCanMoveNativePokemonToEveryOlderGameWithSingleOwnership(GameVersion a, GameVersion b)
    {
        var source = new Document("source", Save(a, Pokemon(a))); var target = new Document("target", Save(b));
        var targetBefore = target.Data.ToArray(); var sourceBefore = source.Data.ToArray();
        var location = SourceLocation(a); var beforePokemon = Read(source.Data).Boxes[location.Box].Slots[location.Slot];
        await Bank().MoveIntoBankAsync(source, location, Hash(source.Data));
        Assert.Equal((ushort)0, Read(source.Data).Boxes[location.Box].Slots[location.Slot].Species);
        Assert.Equal(sourceBefore, (await Bank().LastSaveBackupAsync()).Data);
        var entry = Assert.Single(await Bank().ListAsync()); var backup = await Bank().ExportAsync();
        var preview = new PokemonTransfer().Prepare(entry.Pokemon, Read(target.Data).FormatKey, Date);
        await Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(target.Data), Date);
        Assert.Empty(await Bank().ListAsync()); Assert.False(await Bank().HasPendingMoveAsync());
        Assert.Equal(0, await Bank().ImportAsync(backup));
        Assert.Equal(targetBefore, (await Bank().LastSaveBackupAsync()).Data);
        var moved = Read(target.Data).Boxes[0].Slots[0];
        Assert.Equal(preview.Pokemon.StoredData, moved.StoredData);
        Assert.Equal(Generation(b), moved.Generation); Assert.Equal(beforePokemon.Species, moved.Species);
        Assert.Equal(beforePokemon.Details!.IndividualValues, moved.Details!.IndividualValues);
        Assert.Equal(beforePokemon.Details.EffortValues, moved.Details.EffortValues);
        Assert.Equal(beforePokemon.Details.Nature, moved.Details.Nature); Assert.Equal(beforePokemon.IsShiny, moved.IsShiny);
        Assert.Equal(1, target.Writes);
        // A later ordinary same-generation deposit/withdraw remains possible.
        await Bank().MoveIntoBankAsync(target, new(false, 0, 0), Hash(target.Data));
        var older = Assert.Single(await Bank().ListAsync());
        await Bank().MoveToSaveAsync(older.Entry.Id, target, new(false, 0, 0), Hash(target.Data));
        Assert.Empty(await Bank().ListAsync()); Assert.Equal(moved.StoredData, Read(target.Data).Boxes[0].Slots[0].StoredData);
    }

    public static IEnumerable<object[]> Failures => from pair in new[]
    {
        (GameVersion.B2, GameVersion.SS), (GameVersion.AS, GameVersion.Pt), (GameVersion.X, GameVersion.W2),
        (GameVersion.UM, GameVersion.D), (GameVersion.SN, GameVersion.B), (GameVersion.US, GameVersion.Y),
    } from failure in new[] { "before", "partial", "after" } select new object[] { pair.Item1, pair.Item2, failure };
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EveryBackwardDirectionRecoversDeterministicallyWithoutDuplicates(GameVersion a, GameVersion b, string failure)
    {
        var source = new Document("source", Save(a, Pokemon(a))); var target = new Document("target", Save(b));
        var before = target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source, SourceLocation(a), Hash(source.Data));
        var entry = Assert.Single(await Bank().ListAsync()); var export = await Bank().ExportAsync();
        var expected = new PokemonTransfer().Prepare(entry.Pokemon, Read(target.Data).FormatKey, Date).Pokemon;
        target.Failure = failure;
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(before), Date));
        Assert.True(await Bank().HasPendingMoveAsync()); target.Failure = "";
        if (failure == "partial")
        {
            await Assert.ThrowsAsync<SaveRecoveryRequiredException>(() => Bank().RecoverMoveAsync(target));
            await Bank().RecoverMoveAsync(target, restoreOriginal: true);
        }
        else await Bank().RecoverMoveAsync(target);
        Assert.False(await Bank().HasPendingMoveAsync());
        if (failure == "after")
        {
            Assert.Empty(await Bank().ListAsync()); Assert.Equal(0, await Bank().ImportAsync(export));
            Assert.Equal(expected.StoredData, Read(target.Data).Boxes[0].Slots[0].StoredData);
        }
        else { Assert.Equal(before, target.Data); Assert.Equal(entry.Entry.Id, Assert.Single(await Bank().ListAsync()).Entry.Id); }
    }

    [Theory]
    [InlineData("species")][InlineData("move")][InlineData("ability")][InlineData("nickname")]
    public async Task RefusalPreservesBankEntryAndNeverWritesDestination(string kind)
    {
        var pk = Pokemon(GameVersion.UM);
        if (kind == "species") pk.Species = 722;
        if (kind == "move") pk.Move1 = 600;
        if (kind == "ability") pk.Ability = 233;
        if (kind == "nickname") pk.Nickname = "abcdefghijkl";
        var source = new Document("source", Save(GameVersion.UM, pk)); var target = new Document("target", Save(GameVersion.SS));
        var before = target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source, SourceLocation(GameVersion.UM), Hash(source.Data));
        var entry = Assert.Single(await Bank().ListAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 0), Hash(target.Data), Date));
        Assert.Equal(before, target.Data); Assert.Equal(0, target.Writes); Assert.False(await Bank().HasPendingMoveAsync());
        Assert.Equal(entry.Entry.Id, Assert.Single(await Bank().ListAsync()).Entry.Id);
        // Refused conversion does not prevent returning the unchanged individual to its source.
        await Bank().MoveToSaveAsync(entry.Entry.Id, source, SourceLocation(GameVersion.UM), Hash(source.Data));
        Assert.Empty(await Bank().ListAsync());
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
