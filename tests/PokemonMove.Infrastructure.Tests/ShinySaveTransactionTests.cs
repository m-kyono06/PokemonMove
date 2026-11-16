using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class ShinySaveTransactionTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"PokemonMove-shiny-"+Guid.NewGuid());
    private LocalBankStore Bank()=>new(root,new PokemonCodec(),new GameSaveEditor());
    private static byte[] Hash(byte[] data)=>SHA256.HashData(data);
    private sealed class Document(byte[] data):ISaveDocument
    {
        public string Id=>"selected-save";public byte[] Data=data;public string Failure="";public int Writes;
        public Task<byte[]> ReadAsync()=>Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] bytes)
        {
            Writes++;if(Failure=="before")throw new IOException("before");
            if(Failure=="partial"){Data=bytes[..100];throw new IOException("partial");}
            Data=bytes.ToArray();if(Failure=="after")throw new IOException("after");return Task.CompletedTask;
        }
    }
    private static int Generation(GameVersion g)=>g switch{GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS=>4,
        GameVersion.B or GameVersion.W or GameVersion.B2 or GameVersion.W2=>5,GameVersion.X or GameVersion.Y or GameVersion.OR or GameVersion.AS=>6,_=>7};
    private static byte[] Save(GameVersion g)=>Generation(g) switch{4=>Gen4Fixtures.Create(g,dsv:true),5=>Gen5Fixtures.Create(g,dsv:true),6=>Gen6Fixtures.Create(g),_=>Gen7Fixtures.Create(g)};
    private static SaveSnapshot Read(byte[] bytes)=>new GameSaveReader().Read(bytes);
    public static IEnumerable<object[]> Games=>new[]{GameVersion.D,GameVersion.P,GameVersion.Pt,GameVersion.HG,GameVersion.SS,
        GameVersion.B,GameVersion.W,GameVersion.B2,GameVersion.W2,GameVersion.X,GameVersion.Y,GameVersion.OR,GameVersion.AS,
        GameVersion.SN,GameVersion.MN,GameVersion.US,GameVersion.UM}.Select(g=>new object[]{g});
    private async Task<byte[]> Seed(Document doc)
    {
        await Bank().AddAsync(Read(doc.Data).Party[1],"以前のコピー","TEST");return await Bank().ExportAsync();
    }
    [Theory]
    [MemberData(nameof(Games))]
    public async Task SavesOnlyExistingSlotWithBackupAndNeverCreatesBankOwnership(GameVersion game)
    {
        var doc=new Document(Save(game));var original=doc.Data.ToArray();var bankBefore=await Seed(doc);var old=Read(original);
        await Bank().MakeShinyAsync(doc,new(true,0,0),Hash(doc.Data));var view=Read(doc.Data);
        Assert.True(view.Party[0].IsShiny);Assert.Equal(old.Party.Count,view.Party.Count);
        Assert.Equal(old.Party[0].Details!.OriginalTrainerIdText,view.Party[0].Details!.OriginalTrainerIdText);
        Assert.Equal(old.Party[1].StoredData,view.Party[1].StoredData);
        Assert.Equal(original,(await Bank().LastSaveBackupAsync()).Data);Assert.Equal(bankBefore,await Bank().ExportAsync());
        Assert.Single(await Bank().ListAsync());Assert.False(Assert.Single(await Bank().ListAsync()).Entry.TransferEligible);
        Assert.False(await Bank().HasPendingMoveAsync());Assert.Equal(1,doc.Writes);
        var dex=new CollectionDex().BuildAll([view],[]);
        Assert.Equal(1,dex.Entries[view.Party[0].Species-1].ShinyPartyCount);
    }
    public static IEnumerable<object[]> Failures=>from g in new[]{GameVersion.SS,GameVersion.B2,GameVersion.X,GameVersion.SN,GameVersion.UM}
        from failure in new[]{"before","partial","after"} select new object[]{g,failure};
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task InterruptedEditRecoversExactSaveWithoutAddingOrConsumingBankPokemon(GameVersion game,string failure)
    {
        var doc=new Document(Save(game));var original=doc.Data.ToArray();var bankBefore=await Seed(doc);
        var expected=new GameSaveEditor().MakeShiny(original,new(true,0,0)).After;doc.Failure=failure;
        await Assert.ThrowsAsync<IOException>(()=>Bank().MakeShinyAsync(doc,new(true,0,0),Hash(original)));
        Assert.True(await Bank().HasPendingMoveAsync());doc.Failure="";
        if(failure=="partial")
        {
            await Assert.ThrowsAsync<SaveRecoveryRequiredException>(()=>Bank().RecoverMoveAsync(doc));
            await Bank().RecoverMoveAsync(doc,restoreOriginal:true);
        }
        else await Bank().RecoverMoveAsync(doc);
        Assert.Equal(failure=="after"?expected:original,doc.Data);
        Assert.Equal(bankBefore,await Bank().ExportAsync());Assert.False(await Bank().HasPendingMoveAsync());
        Assert.Equal(original,(await Bank().LastSaveBackupAsync()).Data);
    }
    [Theory]
    [InlineData("stale")][InlineData("empty")][InlineData("outside")][InlineData("already")]
    public async Task RefusalCannotGeneratePokemonOrWriteAnyFile(string kind)
    {
        var data=Save(GameVersion.SS);
        if(kind=="already")data=new GameSaveEditor().MakeShiny(data,new(true,0,0)).After;
        var doc=new Document(data);var bankBefore=await Seed(doc);
        var location=kind=="empty"?new PokemonLocation(false,0,0):kind=="outside"?new(true,0,5):new(true,0,0);
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().MakeShinyAsync(doc,location,kind=="stale"?new byte[32]:Hash(data)));
        Assert.Equal(data,doc.Data);Assert.Equal(0,doc.Writes);Assert.Equal(bankBefore,await Bank().ExportAsync());Assert.False(await Bank().HasPendingMoveAsync());
    }
    [Fact]
    public async Task BackupFailureDoesNotWriteSaveAndIntentCanBeCancelledThroughRecovery()
    {
        var doc=new Document(Save(GameVersion.UM));var before=doc.Data.ToArray();var bankBefore=await Seed(doc);
        await File.WriteAllTextAsync(Path.Combine(root,"save-backups"),"block directory");
        var error=await Record.ExceptionAsync(()=>Bank().MakeShinyAsync(doc,new(true,0,0),Hash(doc.Data)));
        Assert.True(error is IOException or UnauthorizedAccessException);Assert.Equal(0,doc.Writes);Assert.Equal(before,doc.Data);
        await Bank().RecoverMoveAsync(doc);Assert.False(await Bank().HasPendingMoveAsync());Assert.Equal(bankBefore,await Bank().ExportAsync());
    }
    [Fact]
    public async Task JournalCannotBeReinterpretedAsDepositOrWithdrawal()
    {
        var doc=new Document(Save(GameVersion.SS));await Seed(doc);doc.Failure="before";
        await Assert.ThrowsAsync<IOException>(()=>Bank().MakeShinyAsync(doc,new(true,0,0),Hash(doc.Data)));
        var file=Path.Combine(root,"bank.json");var json=JsonNode.Parse(await File.ReadAllBytesAsync(file))!;
        json["Pending"]!["Deposit"]=true;await File.WriteAllTextAsync(file,json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(()=>Bank().HasPendingMoveAsync());Assert.Equal(1,doc.Writes);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
