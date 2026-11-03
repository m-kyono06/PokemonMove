using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class Gen6BankTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"PokemonMove-xy-"+Guid.NewGuid());
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
            Data=bytes.ToArray();
            if(Failure=="after")throw new IOException("lost acknowledgement");
            return Task.CompletedTask;
        }
    }
    public static IEnumerable<object[]> Pairs=>from a in new[]{GameVersion.X,GameVersion.Y} from b in new[]{GameVersion.X,GameVersion.Y} select new object[]{a,b};
    public static IEnumerable<object[]> ForwardPairs=>from a in new[]{GameVersion.B,GameVersion.W,GameVersion.B2,GameVersion.W2}
        from b in new[]{GameVersion.X,GameVersion.Y} select new object[]{a,b};

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task XYBankMovesKeepExactPokemonAndConsumeEntryAfterVerifiedWrite(GameVersion a,GameVersion b)
    {
        var source=new Document("source",Gen6Fixtures.Create(a));
        var target=new Document("target",Gen6Fixtures.Create(b,boxPokemon:new PK6{Species=721,PID=987,EncryptionConstant=123,CurrentLevel=30}));
        var before=source.Data.ToArray();var targetBefore=target.Data.ToArray();
        var pokemon=View(before).Boxes[30].Slots[29];
        await Bank().MoveIntoBankAsync(source,new(false,30,29),Hash(source.Data));
        Assert.Equal((ushort)0,View(source.Data).Boxes[30].Slots[29].Species);
        var entry=Assert.Single(await Bank().ListAsync());Assert.Equal("pk6",entry.Entry.FormatKey);
        Assert.Equal(pokemon.StoredData,entry.Pokemon.StoredData);Assert.Equal(before,(await Bank().LastSaveBackupAsync()).Data);
        Assert.EndsWith(".main",(await Bank().LastSaveBackupAsync()).Name);
        var backup=await Bank().ExportAsync();
        var during=new CollectionDex().BuildAll([View(source.Data),View(target.Data)],await Bank().ListAsync());
        Assert.Equal(1,during.Entries[649].BankCount);Assert.Equal(1,during.Entries[720].BoxCount);
        await Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(0,await Bank().ImportAsync(backup));
        Assert.Equal(pokemon.StoredData,View(target.Data).Boxes[0].Slots[0].StoredData);
        Assert.Equal(pokemon.Details,View(target.Data).Boxes[0].Slots[0].Details);
        Assert.Equal(targetBefore,(await Bank().LastSaveBackupAsync()).Data);
        var after=new CollectionDex().BuildAll([View(source.Data),View(target.Data)],await Bank().ListAsync());
        Assert.Equal(during.Owned(DexVariant.All),after.Owned(DexVariant.All));Assert.Equal(1,after.Entries[649].BoxCount);Assert.Equal(0,after.Entries[649].BankCount);
    }

    [Theory]
    [MemberData(nameof(ForwardPairs))]
    public async Task AllBWGamesCanMoveForwardButPK6CanNeverReturnToDS(GameVersion a,GameVersion b)
    {
        var source=new Document("bw",Gen5Fixtures.Create(a));var target=new Document("xy",Gen6Fixtures.Create(b));
        await Bank().MoveIntoBankAsync(source,new(false,23,29),Hash(source.Data));
        var entry=Assert.Single(await Bank().ListAsync());var backup=await Bank().ExportAsync();
        var plan=new PokemonTransfer().Prepare(entry.Pokemon,"xy",trainer:View(target.Data).TransferTrainer);
        await Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal(0,await Bank().ImportAsync(backup));
        var moved=View(target.Data).Boxes[0].Slots[0];Assert.Equal("pk6",moved.FormatKey);Assert.Equal(plan.Pokemon.StoredData,moved.StoredData);
        await Bank().MoveIntoBankAsync(target,new(false,0,0),Hash(target.Data));
        var newer=Assert.Single(await Bank().ListAsync());var original=source.Data.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().MoveToSaveAsync(newer.Entry.Id,source,new(false,23,29),Hash(source.Data)));
        Assert.Equal(original,source.Data);Assert.Single(await Bank().ListAsync());
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("partial")]
    public async Task InterruptedGenerationConversionReproducesExactOutputAndRecovers(string failure)
    {
        var source=new Document("bw2",Gen5Fixtures.Create(GameVersion.B2));var target=new Document("x",Gen6Fixtures.Create(GameVersion.X));
        var original=target.Data.ToArray();
        await Bank().MoveIntoBankAsync(source,new(false,23,29),Hash(source.Data));var entry=Assert.Single(await Bank().ListAsync());
        var plan=new PokemonTransfer().Prepare(entry.Pokemon,"xy",trainer:View(target.Data).TransferTrainer);
        target.Failure=failure;
        await Assert.ThrowsAsync<IOException>(()=>Bank().MoveToSaveAsync(entry.Entry.Id,target,new(false,0,0),Hash(target.Data)));
        RecentTrainerCache.SetRecentTrainer(new SimpleTrainerInfo(GameVersion.OR){OT="別の親",Gender=1});
        Assert.True(await Bank().HasPendingMoveAsync());target.Failure="";
        if(failure=="partial")
        {
            await Assert.ThrowsAsync<SaveRecoveryRequiredException>(()=>Bank().RecoverMoveAsync(target));
            await Bank().RecoverMoveAsync(target,restoreOriginal:true);
        }
        else await Bank().RecoverMoveAsync(target);
        Assert.False(await Bank().HasPendingMoveAsync());
        if(failure=="after")
        {
            Assert.Empty(await Bank().ListAsync());Assert.Equal(plan.Pokemon.StoredData,View(target.Data).Boxes[0].Slots[0].StoredData);
        }
        else{Assert.Equal(original,target.Data);Assert.Single(await Bank().ListAsync());}
    }

    [Fact]
    public async Task ReimportMixedLegacyBankPreservesFormatsEligibilityAndSpentIds()
    {
        var gen5=new Document("bw",Gen5Fixtures.Create(GameVersion.W));
        await Bank().MoveIntoBankAsync(gen5,new(false,23,29),Hash(gen5.Data));
        var old=JsonNode.Parse(await Bank().ExportAsync())!;old["Version"]=3;
        await File.WriteAllTextAsync(Path.Combine(root,"bank.json"),old.ToJsonString());
        var original=Assert.Single(await Bank().ListAsync());Assert.Equal("pk5",original.Entry.FormatKey);
        var gen6=new Document("xy",Gen6Fixtures.Create(GameVersion.Y));
        await Bank().MoveIntoBankAsync(gen6,new(false,30,29),Hash(gen6.Data));
        var backup=await Bank().ExportAsync();Assert.Equal(4,JsonNode.Parse(backup)!["Version"]!.GetValue<int>());
        var other=new LocalBankStore(Path.Combine(root,"import"),new PokemonCodec(),new GameSaveEditor());
        Assert.Equal(2,await other.ImportAsync(backup));Assert.Equal(0,await other.ImportAsync(backup));
        Assert.Contains(await other.ListAsync(),p=>p.Entry.FormatKey=="pk6"&&p.Entry.TransferEligible);
        Assert.Contains(await other.ListAsync(),p=>p.Entry.Id==original.Entry.Id&&p.Entry.TransferEligible);
    }

    [Fact]
    public async Task SourceSsToB2ToXChainNeverRegainsEarlierGenerationOwnership()
    {
        var ss=new Document("ss",Gen4Fixtures.Create(GameVersion.SS));var b2=new Document("b2",Gen5Fixtures.Create(GameVersion.B2));var x=new Document("x",Gen6Fixtures.Create(GameVersion.X));
        await Bank().MoveIntoBankAsync(ss,new(false,17,29),Hash(ss.Data));var pk4=Assert.Single(await Bank().ListAsync());
        await Bank().MoveToSaveAsync(pk4.Entry.Id,b2,new(false,0,0),Hash(b2.Data),new(2026,11,3));
        await Bank().MoveIntoBankAsync(b2,new(false,0,0),Hash(b2.Data));var pk5=Assert.Single(await Bank().ListAsync());
        await Bank().MoveToSaveAsync(pk5.Entry.Id,x,new(false,0,0),Hash(x.Data));
        Assert.Empty(await Bank().ListAsync());Assert.Equal((ushort)0,View(ss.Data).Boxes[17].Slots[29].Species);Assert.Equal((ushort)0,View(b2.Data).Boxes[0].Slots[0].Species);
        Assert.Equal((ushort)387,View(x.Data).Boxes[0].Slots[0].Species);Assert.Equal("pk6",View(x.Data).Boxes[0].Slots[0].FormatKey);
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
