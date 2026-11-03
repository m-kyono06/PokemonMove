using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class Gen7BankTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"PokemonMove-sm-"+Guid.NewGuid());
    private LocalBankStore Bank()=>new(root,new PokemonCodec(),new GameSaveEditor());
    private static byte[] Hash(byte[] data)=>SHA256.HashData(data);
    private static SaveSnapshot View(byte[] data)=>new GameSaveReader().Read(data);
    private sealed class Document(string id,byte[] data):ISaveDocument
    {
        public string Id=>id;
        public byte[] Data=data;
        public string Failure="";
        public int Writes;
        public Task<byte[]> ReadAsync()=>Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] bytes)
        {
            Writes++;
            if(Failure=="before")throw new IOException("before");
            if(Failure=="partial"){Data=bytes[..100];throw new IOException("partial");}
            Data=bytes.ToArray();if(Failure=="after")throw new IOException("lost acknowledgement");
            return Task.CompletedTask;
        }
    }
    public static IEnumerable<object[]> Pairs=>from a in new[]{GameVersion.SN,GameVersion.MN,GameVersion.US,GameVersion.UM} from b in new[]{GameVersion.SN,GameVersion.MN,GameVersion.US,GameVersion.UM} select new object[]{a,b};
    public static IEnumerable<object[]> ForwardPairs=>from a in new[]{GameVersion.X,GameVersion.Y,GameVersion.OR,GameVersion.AS}
        from b in new[]{GameVersion.SN,GameVersion.MN,GameVersion.US,GameVersion.UM} select new object[]{a,b};

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Gen7MovesConsumeEntryPreserveExactPokemonAndMaintainCollection(GameVersion a,GameVersion b)
    {
        var source=new Document("source",Gen7Fixtures.Create(a));
        var target=new Document("target",Gen7Fixtures.Create(b,boxPokemon:new PK7{Species=802,PID=987,EncryptionConstant=123,CurrentLevel=30}));
        var original=source.Data.ToArray();var targetOriginal=target.Data.ToArray();
        var pokemon=View(original).Boxes[31].Slots[29];
        await Bank().MoveIntoBankAsync(source,new(false,31,29),Hash(source.Data));
        Assert.Equal((ushort)0,View(source.Data).Boxes[31].Slots[29].Species);
        var entry=Assert.Single(await Bank().ListAsync());Assert.Equal("pk7",entry.Entry.FormatKey);
        Assert.Equal(pokemon.StoredData,entry.Pokemon.StoredData);Assert.Equal(original,(await Bank().LastSaveBackupAsync()).Data);
        Assert.EndsWith(".main",(await Bank().LastSaveBackupAsync()).Name);
        var backup=await Bank().ExportAsync();
        var during=new CollectionDex().BuildAll([View(source.Data),View(target.Data)],await Bank().ListAsync());
        Assert.Equal(1,during.Entries[721].BankCount);Assert.Equal(1,during.Entries[801].BoxCount);
        await Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(0,await Bank().ImportAsync(backup));
        Assert.Equal(pokemon.StoredData,View(target.Data).Boxes[0].Slots[0].StoredData);
        Assert.Equal(pokemon.Details,View(target.Data).Boxes[0].Slots[0].Details);
        Assert.Equal(targetOriginal,(await Bank().LastSaveBackupAsync()).Data);
        var after=new CollectionDex().BuildAll([View(source.Data),View(target.Data)],await Bank().ListAsync());
        Assert.Equal(during.Owned(DexVariant.All),after.Owned(DexVariant.All));Assert.Equal(1,after.Entries[721].BoxCount);Assert.Equal(0,after.Entries[721].BankCount);
    }

    [Theory]
    [MemberData(nameof(ForwardPairs))]
    public async Task Gen6MovesToAnyGen7GameAndCannotReturn(GameVersion a,GameVersion b)
    {
        var source=new Document("old",Gen6Fixtures.Create(a));var target=new Document("sm",Gen7Fixtures.Create(b));
        await Bank().MoveIntoBankAsync(source,new(false,30,29),Hash(source.Data));
        var entry=Assert.Single(await Bank().ListAsync());var backup=await Bank().ExportAsync();
        var plan=new PokemonTransfer().Prepare(entry.Pokemon,View(target.Data).FormatKey,trainer:View(target.Data).TransferTrainer);
        await Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(0,await Bank().ImportAsync(backup));
        var moved=View(target.Data).Boxes[0].Slots[0];Assert.Equal("pk7",moved.FormatKey);Assert.Equal(plan.Pokemon.StoredData,moved.StoredData);
        await Bank().MoveIntoBankAsync(target,new(false,0,0),Hash(target.Data));var newer=Assert.Single(await Bank().ListAsync());
        var before=source.Data.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().MoveToSaveAsync(newer.Entry.Id,source,new(false,30,29),Hash(source.Data)));
        Assert.Equal(before,source.Data);Assert.Single(await Bank().ListAsync());
    }

    [Theory]
    [InlineData("before",GameVersion.SN)]
    [InlineData("after",GameVersion.SN)]
    [InlineData("partial",GameVersion.SN)]
    [InlineData("before",GameVersion.US)]
    [InlineData("after",GameVersion.US)]
    [InlineData("partial",GameVersion.US)]
    [InlineData("before",GameVersion.UM)]
    [InlineData("after",GameVersion.UM)]
    [InlineData("partial",GameVersion.UM)]
    public async Task InterruptedForwardMoveCanReproduceSignatureAndRecoverWithoutDuplicate(string failure,GameVersion targetVersion)
    {
        var source=new Document("oras",Gen6Fixtures.Create(GameVersion.OR));var target=new Document("sm",Gen7Fixtures.Create(targetVersion));
        var original=target.Data.ToArray();await Bank().MoveIntoBankAsync(source,new(false,30,29),Hash(source.Data));var entry=Assert.Single(await Bank().ListAsync());
        var plan=new PokemonTransfer().Prepare(entry.Pokemon,View(target.Data).FormatKey,trainer:View(target.Data).TransferTrainer);
        target.Failure=failure;
        await Assert.ThrowsAsync<IOException>(()=>Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data)));
        RecentTrainerCache.SetRecentTrainer(new SimpleTrainerInfo(GameVersion.MN){OT="違うトレーナー",Gender=1});
        Assert.True(await Bank().HasPendingMoveAsync());target.Failure="";
        if(failure=="partial")
        {
            await Assert.ThrowsAsync<SaveRecoveryRequiredException>(()=>Bank().RecoverMoveAsync(target));
            await Bank().RecoverMoveAsync(target,restoreOriginal:true);
        }
        else await Bank().RecoverMoveAsync(target);
        Assert.False(await Bank().HasPendingMoveAsync());
        if(failure=="after") {Assert.Empty(await Bank().ListAsync());Assert.Equal(plan.Pokemon.StoredData,View(target.Data).Boxes[0].Slots[0].StoredData);}
        else {Assert.Equal(original,target.Data);Assert.Single(await Bank().ListAsync());}
    }

    [Fact]
    public async Task LegacyGen6BankCanBeMergedWithGen7AndKeepsItsOwnershipHistory()
    {
        var source=new Document("xy",Gen6Fixtures.Create(GameVersion.X));
        await Bank().MoveIntoBankAsync(source,new(false,30,29),Hash(source.Data));var old=Assert.Single(await Bank().ListAsync());
        var node=JsonNode.Parse(await Bank().ExportAsync())!;node["Version"]=4;
        await File.WriteAllTextAsync(Path.Combine(root,"bank.json"),node.ToJsonString());
        Assert.Equal("pk6",Assert.Single(await Bank().ListAsync()).Entry.FormatKey);
        var sm=new Document("sm",Gen7Fixtures.Create(GameVersion.SN));
        await Bank().MoveIntoBankAsync(sm,new(false,31,29),Hash(sm.Data));
        var backup=await Bank().ExportAsync();Assert.Equal(5,JsonNode.Parse(backup)!["Version"]!.GetValue<int>());
        var other=new LocalBankStore(Path.Combine(root,"other"),new PokemonCodec(),new GameSaveEditor());
        Assert.Equal(2,await other.ImportAsync(backup));Assert.Equal(0,await other.ImportAsync(backup));
        Assert.Contains(await other.ListAsync(),i=>i.Entry.Id==old.Entry.Id&&i.Entry.TransferEligible);
        Assert.Contains(await other.ListAsync(),i=>i.Entry.FormatKey=="pk7"&&i.Entry.TransferEligible);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("resort")]
    public async Task ProtectedPokemonAreNeverRemovedOrAddedToBank(string condition)
    {
        var source=new Document("sm",Gen7Fixtures.Create(GameVersion.SN,registered:condition=="team",resort:condition=="resort"));var before=source.Data.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().MoveIntoBankAsync(source,new(false,31,29),Hash(source.Data)));
        Assert.Equal(0,source.Writes);Assert.Equal(before,source.Data);Assert.Empty(await Bank().ListAsync());Assert.False(await Bank().HasPendingMoveAsync());
    }

    [Fact]
    public async Task UsumExclusivePokemonStaysInBankIfSMRefusesAndCanReturnToOriginal()
    {
        var source=new Document("usum",Gen7Fixtures.Create(GameVersion.US,boxPokemon:new PK7{Species=807,CurrentLevel=50,Version=GameVersion.US}));
        var target=new Document("sm",Gen7Fixtures.Create(GameVersion.SN));var before=target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source,new(false,31,29),Hash(source.Data));var entry=Assert.Single(await Bank().ListAsync());
        var backup=await Bank().ExportAsync();
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data)));
        Assert.Equal(before,target.Data);Assert.Equal(0,target.Writes);Assert.Equal(entry.Entry.Id,Assert.Single(await Bank().ListAsync()).Entry.Id);
        Assert.False(await Bank().HasPendingMoveAsync());
        await Bank().MoveToSaveAsync(entry.Entry.Id,source,new(false,31,29),Hash(source.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(entry.Pokemon.StoredData,View(source.Data).Boxes[31].Slots[29].StoredData);
        Assert.Equal(0,await Bank().ImportAsync(backup));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
