using System.Security.Cryptography;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class TradeEvolutionBankTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-evolve-" + Guid.NewGuid());
    private LocalBankStore Bank() => new(root, new PokemonCodec(), new GameSaveEditor(), new TradeEvolution());
    private static byte[] Hash(byte[] bytes) => SHA256.HashData(bytes);
    private sealed class Document(string id, byte[] data) : ISaveDocument
    {
        public string Id => id; public byte[] Data = data; public bool FailAfter;
        public Task<byte[]> ReadAsync() => Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] bytes) { Data = bytes.ToArray(); if (FailAfter) throw new IOException("after"); return Task.CompletedTask; }
    }
    private static PKM Pokemon(int generation, ushort species, int item)
    {
        PKM pk = generation switch { 4 => new PK4(), 5 => new PK5(), 6 => new PK6(), _ => new PK7() };
        pk.Species = species; pk.Language = 1; pk.Version = generation switch { 4 => GameVersion.SS, 5 => GameVersion.B2, 6 => GameVersion.AS, _ => GameVersion.UM };
        pk.PID = 0; pk.ID32 = 0; pk.EncryptionConstant = 456; pk.CurrentLevel = 50;
        pk.RefreshAbility(0); pk.HeldItem = item; pk.Move1 = 33; pk.Move1_PP = 1; pk.IV_HP = 31; pk.EV_ATK = 252;
        pk.Nickname = "なまえ"; pk.IsNicknamed = true; pk.OriginalTrainerName = "テスト";
        return pk;
    }
    private static PokemonLocation Location(int g) => new(false, g switch { 4 => 17, 5 => 23, 6 => 30, _ => 31 },29);
    private static Document Save(int generation, ushort species, int item = 0, string id = "source")
    {
        var pk = Pokemon(generation,species,item);
        var data = generation switch
        {
            4 => Gen4Fixtures.Create(GameVersion.SS,boxPokemon:(PK4)pk),
            5 => Gen5Fixtures.Create(GameVersion.B2,boxPokemon:(PK5)pk),
            6 => Gen6Fixtures.Create(GameVersion.AS,boxPokemon:(PK6)pk),
            _ => Gen7Fixtures.Create(GameVersion.UM,boxPokemon:(PK7)pk),
        };
        return new(id,data);
    }
    private async Task<BankItem> Deposit(Document doc,int generation)
    {
        await Bank().MoveIntoBankAsync(doc,Location(generation),Hash(doc.Data));
        return (await Bank().ListAsync()).Last();
    }
    [Theory]
    [InlineData(4,93,94,0)][InlineData(5,93,94,0)][InlineData(6,93,94,0)][InlineData(7,93,94,0)]
    [InlineData(4,95,208,233)][InlineData(5,95,208,233)][InlineData(6,95,208,233)][InlineData(7,95,208,233)]
    [InlineData(4,366,367,226)][InlineData(5,349,350,537)][InlineData(6,682,683,647)][InlineData(7,684,685,646)]
    public async Task EvolutionReplacesOwnerConsumesOnlyRequiredItemAndCanBeWithdrawn(int generation,ushort from,ushort to,int item)
    {
        var source=Save(generation,from,item);var first=await Deposit(source,generation);var sourceAfter=source.Data.ToArray();
        var previous=await Bank().ExportAsync();var originalJson=await File.ReadAllBytesAsync(Path.Combine(root,"bank.json"));
        var plan=new TradeEvolution().Prepare(first.Pokemon);
        var evolved=Assert.Single(await Bank().EvolveTradeAsync(first.Entry.Id,Hash(first.Pokemon.StoredData)));
        Assert.Equal(to,evolved.Pokemon.Species);Assert.NotEqual(first.Entry.Id,evolved.Entry.Id);Assert.True(evolved.Entry.TransferEligible);
        Assert.Equal(first.Entry.SourceGame,evolved.Entry.SourceGame);Assert.Equal(first.Entry.FormatKey,evolved.Entry.FormatKey);
        Assert.Equal(plan.Pokemon.StoredData,evolved.Pokemon.StoredData);Assert.Equal(sourceAfter,source.Data);
        Assert.Equal(originalJson,await File.ReadAllBytesAsync(Path.Combine(root,"bank.json.bak")));
        Assert.False(await Bank().HasPendingMoveAsync());
        Assert.Equal(0,await Bank().ImportAsync(previous));Assert.Equal(evolved.Entry.Id,Assert.Single(await Bank().ListAsync()).Entry.Id);
        var dex=new CollectionDex().BuildAll([new GameSaveReader().Read(source.Data)],await Bank().ListAsync());
        Assert.Equal(1,dex.Entries[to-1].BankCount);Assert.Equal(0,dex.Entries[from-1].BankCount);
        var evolvedBackup=await Bank().ExportAsync();
        await Bank().MoveToSaveAsync(evolved.Entry.Id,source,Location(generation),Hash(source.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(0,await Bank().ImportAsync(evolvedBackup));Assert.Equal(0,await Bank().ImportAsync(previous));
        Assert.Equal(plan.Pokemon.StoredData,new GameSaveReader().Read(source.Data).Boxes[Location(generation).Box].Slots[29].StoredData);
    }
    [Theory]
    [InlineData(5)][InlineData(6)][InlineData(7)]
    public async Task PairIsReplacedAtomicallyAndOldBackupsCannotResurrectEitherPokemon(int generation)
    {
        var a=await Deposit(Save(generation,588,id:"a"),generation);var b=await Deposit(Save(generation,616,id:"b"),generation);
        var backup=await Bank().ExportAsync();
        var evolved=await Bank().EvolveTradeAsync(a.Entry.Id,Hash(a.Pokemon.StoredData),b.Entry.Id,Hash(b.Pokemon.StoredData));
        Assert.Equal(new ushort[]{589,617},evolved.Select(i=>i.Pokemon.Species));
        Assert.DoesNotContain(await Bank().ListAsync(),i=>i.Entry.Id==a.Entry.Id||i.Entry.Id==b.Entry.Id);
        Assert.Equal(0,await Bank().ImportAsync(backup));Assert.Equal(2,(await Bank().ListAsync()).Count);
    }
    [Theory]
    [InlineData("copy")][InlineData("stale")][InlineData("item")][InlineData("same")][InlineData("partnerhash")][InlineData("partnercopy")]
    public async Task InvalidEvolutionLeavesExactBankDataUnchanged(string kind)
    {
        var document=Save(7,kind is "same" or "partnerhash" or "partnercopy" ? (ushort)588 : (ushort)95,kind=="item"?0:233);
        BankItem first;
        if(kind=="copy")
        {
            var p=new GameSaveReader().Read(document.Data).Boxes[31].Slots[29];first=(await Bank().AddAsync(p,"USUM","テスト")).Item;
        }
        else first=await Deposit(document,7);
        BankItem? partner=null;
        if(kind is "partnerhash" or "partnercopy")
        {
            var doc=Save(7,616,id:"other");
            partner=kind=="partnercopy"?(await Bank().AddAsync(new GameSaveReader().Read(doc.Data).Boxes[31].Slots[29],"USUM","テスト")).Item:await Deposit(doc,7);
        }
        var bytes=await File.ReadAllBytesAsync(Path.Combine(root,"bank.json"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().EvolveTradeAsync(first.Entry.Id,
            kind=="stale"?new byte[32]:Hash(first.Pokemon.StoredData),
            kind=="same"?first.Entry.Id:partner?.Entry.Id,
            kind=="partnerhash"?new byte[32]:partner is null?null:Hash(partner.Pokemon.StoredData)));
        Assert.Equal(bytes,await File.ReadAllBytesAsync(Path.Combine(root,"bank.json")));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task FailureToSaveBackupNeverPartiallyEvolvesOrConsumesOwners(bool pair)
    {
        var a=await Deposit(Save(7,pair?(ushort)588:(ushort)93),7);
        BankItem? b=pair?await Deposit(Save(7,616,id:"b"),7):null;
        var before=await File.ReadAllBytesAsync(Path.Combine(root,"bank.json"));
        File.Delete(Path.Combine(root,"bank.json.bak"));Directory.CreateDirectory(Path.Combine(root,"bank.json.bak"));
        var error=await Record.ExceptionAsync(()=>Bank().EvolveTradeAsync(a.Entry.Id,Hash(a.Pokemon.StoredData),b?.Entry.Id,b is null?null:Hash(b.Pokemon.StoredData)));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(before,await File.ReadAllBytesAsync(Path.Combine(root,"bank.json")));
        Assert.Contains(await Bank().ListAsync(),i=>i.Entry.Id==a.Entry.Id);
        if(b is not null)Assert.Contains(await Bank().ListAsync(),i=>i.Entry.Id==b.Entry.Id);
    }
    [Fact]
    public async Task ExistingIdenticalEvolutionResultIsNotDuplicated()
    {
        var first=await Deposit(Save(7,93),7);var plan=new TradeEvolution().Prepare(first.Pokemon);
        await Bank().AddAsync(plan.Pokemon,"USUM","テスト");
        var before=await File.ReadAllBytesAsync(Path.Combine(root,"bank.json"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().EvolveTradeAsync(first.Entry.Id,Hash(first.Pokemon.StoredData)));
        Assert.Equal(before,await File.ReadAllBytesAsync(Path.Combine(root,"bank.json")));
    }

    [Fact]
    public async Task PendingExternalMoveBlocksEvolution()
    {
        var first=await Deposit(Save(7,93),7);var other=Save(7,95,233,"b");other.FailAfter=true;
        await Assert.ThrowsAsync<IOException>(()=>Bank().MoveIntoBankAsync(other,Location(7),Hash(other.Data)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().EvolveTradeAsync(first.Entry.Id,Hash(first.Pokemon.StoredData)));
        Assert.True(await Bank().HasPendingMoveAsync());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
