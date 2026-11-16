using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class RegisteredSaveAccessTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"PokemonMove-open-"+Guid.NewGuid());
    private LocalRegisteredSaves Registry()=>new(root);
    private sealed class Document(string id,byte[] data):ISaveDocument
    {
        public string Id=>id;public byte[] Data=data;public bool Denied;public int Reads;public int Writes;
        public Task<byte[]> ReadAsync(){Reads++;if(Denied)throw new UnauthorizedAccessException();return Task.FromResult(Data.ToArray());}
        public Task WriteAsync(byte[] bytes){Writes++;throw new InvalidOperationException("A reader must not write saves");}
    }
    private sealed class Documents(params Document[] documents):ISaveDocuments
    {
        public ISaveDocument Open(string id)=>documents.Single(d=>d.Id==id);
        public Task<ISaveDocument?> PickAsync()=>throw new InvalidOperationException("An automatic open must not launch a file picker");
    }
    private static byte[] Data(ushort species)=>Gen4Fixtures.Create(GameVersion.SS,boxPokemon:new PK4{Species=species,CurrentLevel=20,PID=100,Version=GameVersion.SS});
    [Fact]
    public async Task ReopeningAlwaysReadsCurrentContentsAndNeverWritesSaveOrDuplicatesRegistration()
    {
        var doc=new Document("ss",Data(152));var docs=new Documents(doc);
        var access=new RegisteredSaveAccess(Registry(),docs,new GameSaveReader());
        var first=await access.OpenAsync("ss");var before=doc.Data.ToArray();
        Assert.Equal(152,first.Save.Boxes[17].Slots[29].Species);Assert.Equal(before,first.Data);Assert.Equal(before,doc.Data);
        doc.Data=Data(155);
        var reopened=await new RegisteredSaveAccess(Registry(),docs,new GameSaveReader()).OpenAsync("ss");
        Assert.Equal(155,reopened.Save.Boxes[17].Slots[29].Species);Assert.Equal(doc.Data,reopened.Data);
        Assert.Equal("ss",reopened.Document.Id);Assert.Equal(2,doc.Reads);Assert.Equal(0,doc.Writes);
        Assert.Single(await Registry().ListAsync());
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task MissingPermissionOrCorruptionNeverChangesRegistrationOrSave(bool denied)
    {
        var doc=new Document("ss",Data(152));var docs=new Documents(doc);
        var access=new RegisteredSaveAccess(Registry(),docs,new GameSaveReader());await access.OpenAsync(doc);
        var registered=await Registry().ExportAsync();doc.Denied=denied;
        if(!denied)doc.Data=new byte[10];var before=doc.Data.ToArray();
        var error=await Record.ExceptionAsync(()=>access.OpenAsync("ss"));Assert.NotNull(error);
        Assert.True(error is UnauthorizedAccessException or InvalidDataException);
        Assert.Equal(registered,await Registry().ExportAsync());Assert.Equal(before,doc.Data);Assert.Equal(0,doc.Writes);
    }
    [Fact]
    public async Task EqualTrainerAndGameNamesDoNotConfuseDocumentIdentity()
    {
        var a=new Document("first-ss",Data(152));var b=new Document("second-ss",Data(155));
        var access=new RegisteredSaveAccess(Registry(),new Documents(a,b),new GameSaveReader());
        await access.OpenAsync(a);await access.OpenAsync(b);
        var opened=await access.OpenAsync("first-ss");
        Assert.Equal(152,opened.Save.Boxes[17].Slots[29].Species);Assert.Equal(2,(await Registry().ListAsync()).Count);
        Assert.Equal(0,a.Writes+b.Writes);
    }
    [Fact]
    public void AutomaticSelectionUsesLastHomeChoiceAndDoesNotFallBackIfItWasRemoved()
    {
        var old=new RegisteredSave("old","SS","同じ親",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(-2));
        var recent=old with{DocumentId="recent",LastOpenedAt=DateTimeOffset.UtcNow};
        Assert.Equal("old",RegisteredSaveSelection.ForAutomaticOpen([old,recent],"old")!.DocumentId);
        Assert.Equal("recent",RegisteredSaveSelection.ForAutomaticOpen([old,recent],null)!.DocumentId);
        Assert.Null(RegisteredSaveSelection.ForAutomaticOpen([recent],"old"));
        Assert.Null(RegisteredSaveSelection.ForAutomaticOpen([],null));
    }
    [Fact]
    public async Task RebindingRetainsListButOldAutomaticReferenceCannotOpenAnUnintendedFile()
    {
        var first=new Document("before",Data(152));var newFile=new Document("after",Data(155));
        var access=new RegisteredSaveAccess(Registry(),new Documents(first,newFile),new GameSaveReader());
        var view=await access.OpenAsync(first);
        await Registry().RebindAsync("before","after",view.Save);
        Assert.Null(RegisteredSaveSelection.ForAutomaticOpen(await Registry().ListAsync(),"before"));
        var reopened=await access.OpenAsync("after");Assert.Equal(155,reopened.Save.Boxes[17].Slots[29].Species);
        Assert.Single(await Registry().ListAsync());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
