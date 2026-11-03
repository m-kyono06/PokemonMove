using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class Gen6SaveTests
{
    [Theory]
    [InlineData(GameVersion.X, "X")]
    [InlineData(GameVersion.Y, "Y")]
    [InlineData(GameVersion.OR, "オメガルビー")]
    [InlineData(GameVersion.AS, "アルファサファイア")]
    public void ReadsDecryptedMainAll31BoxesAndStatsWithoutChangingInput(GameVersion version, string title)
    {
        var data = Gen6Fixtures.Create(version); var original = data.ToArray();
        Assert.Equal(version is GameVersion.OR or GameVersion.AS ? 0x76000 : 0x65600, data.Length);
        var view = new GameSaveReader().Read(data);
        Assert.Equal(title, view.Game); Assert.Equal(version is GameVersion.OR or GameVersion.AS ? "oras" : "xy", view.FormatKey); Assert.Equal("カルム", view.TrainerName);
        Assert.Equal(31, view.Boxes.Count); Assert.Equal(31, view.UnlockedBoxCount); Assert.Equal(2, view.Party.Count);
        Assert.All(view.Boxes, b => Assert.Equal(30, b.Slots.Count)); Assert.Equal("さいご", view.Boxes[30].Name);
        var pk = view.Boxes[30].Slots[29]; Assert.Equal("ハリマロン", pk.Name); Assert.Equal("pk6", pk.FormatKey);
        var d = Assert.IsType<PokemonDetails>(pk.Details); Assert.Equal(6, d.PerfectIVCount); Assert.Equal(510, d.TotalEffortValues);
        Assert.Equal("いじっぱり", d.Nature); Assert.Equal((ushort)42, d.OriginalTrainerId);
        Assert.Equal(new PokemonBattleStats(132, 124, 85, 61, 65, 90), d.Stats); Assert.Null(d.CurrentHP);
        Assert.Equal(new TransferTrainerProfile("カルム", 0, 1, 2, 0), view.TransferTrainer);
        Assert.Equal(original, data);
    }

    [Theory]
    [InlineData(GameVersion.X, false)]
    [InlineData(GameVersion.X, true)]
    [InlineData(GameVersion.Y, false)]
    [InlineData(GameVersion.Y, true)]
    [InlineData(GameVersion.OR, false)]
    [InlineData(GameVersion.OR, true)]
    [InlineData(GameVersion.AS, false)]
    [InlineData(GameVersion.AS, true)]
    public void ChangesOnlySelectedBlockAndItsCrcAndRoundTripsExactPokemon(GameVersion version, bool party)
    {
        var data = Gen6Fixtures.Create(version); var original = data.ToArray(); var editor = new GameSaveEditor();
        var location = party ? new PokemonLocation(true, -1, 0) : new(false, 30, 29);
        var removed = editor.Remove(data, location); var view = new GameSaveReader().Read(removed.After);
        if (party) Assert.Equal((ushort)657, Assert.Single(view.Party).Species);
        else Assert.Equal((ushort)0, view.Boxes[30].Slots[29].Species);
        var oras = version is GameVersion.OR or GameVersion.AS;
        var start = party ? 0x14200 : oras ? 0x33000 : 0x22600; var length = party ? 0x61C : 0x34AD0;
        var crc = (oras ? 0x75E00 : 0x65400) + 0x14 + (party ? 18 : oras ? 56 : 53) * 8 + 6;
        for (var i=0;i<original.Length;i++)
            if (!(i>=start && i<start+length) && i!=crc && i!=crc+1) Assert.Equal(original[i], removed.After[i]);
        if (!party)
        {
            var inserted = editor.Insert(removed.After, location, removed.Pokemon.StoredData, "pk6");
            Assert.Equal(original, inserted.After);
        }
        else Assert.Throws<InvalidDataException>(() => editor.Remove(removed.After, location));
        Assert.Equal(original, data);
    }

    [Theory]
    [InlineData(0x14010)] // trainer
    [InlineData(0x14220)] // party
    [InlineData(0x22610)] // boxes
    [InlineData(0x57210)] // photograph: all blocks must validate
    [InlineData(0x65414)] // length metadata
    [InlineData(0x65418)] // block ID
    public void RejectsDamageWithoutRepairOrWrite(int offset)
    {
        var data = Gen6Fixtures.Create(GameVersion.X); data[offset]^=1; var before=data.ToArray();
        Assert.Throws<InvalidDataException>(() => new GameSaveReader().Read(data));
        Assert.Throws<InvalidDataException>(() => new GameSaveEditor().Remove(data,new(false,30,29)));
        Assert.Equal(before,data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x65600)]
    [InlineData(0x76000)] // Uninitialized ORAS-sized data
    [InlineData(0x100000)] // encrypted cartridge container
    public void RejectsUnsupportedAndEncryptedInputs(int size) =>
        Assert.Throws<InvalidDataException>(()=>new GameSaveReader().Read(new byte[size]));

    [Fact]
    public void LockedBoxAndBackwardsGenerationsNeverChangeDestination()
    {
        var data=Gen6Fixtures.Create(GameVersion.X,unlocked:8); var before=data.ToArray();
        var pk=new GameSaveReader().Read(data).Boxes[30].Slots[29];
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(data,new(false,8,0),pk.StoredData,"pk6"));
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(pk,"bw"));
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(pk,"hgss"));
        Assert.Equal(before,data);
    }

    [Theory]
    [InlineData(6,1)] // Mega Charizard X
    [InlineData(646,1)] // Fused Kyurem
    [InlineData(676,1)] // Furfrou trims cannot be preserved through boxing.
    [InlineData(720,1)] // Unbound Hoopa is ORAS-only
    public void UnsupportedFormsDoNotChangeTarget(int species,byte form)
    {
        var pk=new PK6{Species=(ushort)species,Form=form,CurrentLevel=50};
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var data=Gen6Fixtures.Create(GameVersion.Y);var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(data,new(false,0,0),bytes,"pk6"));
        Assert.Equal(before,data);
    }
    [Theory]
    [InlineData("form")]
    [InlineData("move")]
    [InlineData("relearn")]
    [InlineData("item")]
    [InlineData("ability")]
    public void OrasOnlyDataCanBeWrittenToOrasButNeverToXY(string kind)
    {
        var pk=new PK6{Species=25,PID=112,EncryptionConstant=444,CurrentLevel=50,Move1=33,Version=GameVersion.OR};
        if(kind=="form")pk.Form=1; // Cosplay Pikachu.
        if(kind=="move")pk.Move1=620; // Dragon Ascent.
        if(kind=="relearn")pk.RelearnMove1=620;
        if(kind=="item")pk.HeldItem=753;
        if(kind=="ability")pk.Ability=189;
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var accepted=new GameSaveEditor().Insert(Gen6Fixtures.Create(GameVersion.AS),new(false,0,0),bytes,"pk6");
        Assert.Equal(bytes,new GameSaveReader().Read(accepted.After).Boxes[0].Slots[0].StoredData);
        var xy=Gen6Fixtures.Create(GameVersion.X);var before=xy.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(xy,new(false,0,0),bytes,"pk6"));
        Assert.Equal(before,xy);
    }

    [Theory]
    [InlineData(0x14220)]
    [InlineData(0x33010)]
    [InlineData(0x67C10)]
    [InlineData(0x23A10)] // Secret-base data.
    [InlineData(0x75E14)] // Metadata length.
    [InlineData(0x75E18)] // Metadata ID.
    public void DamagedOrasDataIsNeverRepairedOrWritten(int offset)
    {
        var main=Gen6Fixtures.Create(GameVersion.OR);main[offset]^=1;var before=main.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveReader().Read(main));
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Remove(main,new(false,30,29)));
        Assert.Equal(before,main);
    }

}
