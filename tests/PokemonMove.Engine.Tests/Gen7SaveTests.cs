using System.Security.Cryptography;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class Gen7SaveTests
{
    [Theory]
    [InlineData(GameVersion.SN,"サン")]
    [InlineData(GameVersion.MN,"ムーン")]
    public void ReadsSignedMainAll32BoxesSixDigitIdAndStatsWithoutChangingInput(GameVersion version,string title)
    {
        var data=Gen7Fixtures.Create(version);var before=data.ToArray();
        var view=new GameSaveReader().Read(data);Assert.Equal(0x6BE00,data.Length);
        Assert.Equal(title,view.Game);Assert.Equal("sm",view.FormatKey);Assert.Equal("ヨウ",view.TrainerName);
        Assert.Equal(32,view.Boxes.Count);Assert.Equal(32,view.UnlockedBoxCount);Assert.Equal(2,view.Party.Count);
        var pk=view.Boxes[31].Slots[29];Assert.Equal("モクロー",pk.Name);Assert.Equal("pk7",pk.FormatKey);
        var d=Assert.IsType<PokemonDetails>(pk.Details);Assert.Equal("234567",d.OriginalTrainerIdText);
        Assert.Equal(6,d.PerfectIVCount);Assert.Equal(510,d.TotalEffortValues);
        Assert.Equal(new PokemonBattleStats(144,117,75,63,70,94),d.Stats);
        Assert.Equal(before,data);
    }
    [Theory]
    [InlineData(GameVersion.SN,false)]
    [InlineData(GameVersion.SN,true)]
    [InlineData(GameVersion.MN,false)]
    [InlineData(GameVersion.MN,true)]
    public void EditsOnlySelectedBlockCrcAndSignatureAndProducesVerifiableSave(GameVersion version,bool party)
    {
        var data=Gen7Fixtures.Create(version);var before=data.ToArray();var editor=new GameSaveEditor();
        var location=party?new PokemonLocation(true,-1,0):new(false,31,29);
        var edit=editor.Remove(data,location);var view=new GameSaveReader().Read(edit.After);
        if(party)Assert.Equal((ushort)726,Assert.Single(view.Party).Species);
        else Assert.Equal((ushort)0,view.Boxes[31].Slots[29].Species);
        var start=party?0x1400:0x4E00;var length=party?0x61C:0x36600;
        var crc=0x6BC00+0x14+(party?4:14)*8+6;
        for(var i=0;i<before.Length;i++)
            if(!(i>=start&&i<start+length)&&i!=crc&&i!=crc+1&&!(i>=0x6BB00&&i<0x6BB80))Assert.Equal(before[i],edit.After[i]);
        Assert.True(Gen7SaveSignature.VerifyPayload(edit.After.AsSpan(0x6BB00,0x80),out var signed));
        Assert.Equal(SHA256.HashData(edit.After.AsSpan(0x6BC00,0x140)),signed[..32]);
        if(!party)
        {
            var restored=editor.Insert(edit.After,location,edit.Pokemon.StoredData,"pk7");Assert.Equal(before,restored.After);
        }
        else Assert.Throws<InvalidDataException>(()=>editor.Remove(edit.After,location));
        Assert.Equal(before,data);
    }
    [Theory]
    [InlineData(0x1210)] // status
    [InlineData(0x1420)] // party
    [InlineData(0x4E10)] // box
    [InlineData(0x65010)] // unused by app, still CRC-protected
    [InlineData(0x6BB00)] // signature
    [InlineData(0x6BC14)] // metadata
    public void RefusesCorruptionWithoutRepair(int offset)
    {
        var data=Gen7Fixtures.Create(GameVersion.SN);data[offset]^=1;var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveReader().Read(data));
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Remove(data,new(false,31,29)));
        Assert.Equal(before,data);
    }
    [Fact]
    public void HyperTrainingUsesEffectiveStatsButCountsOnlyRaw31Ivs()
    {
        var pk=new PK7{Species=25,CurrentLevel=100,Nature=Nature.Hardy,Version=GameVersion.SN,HT_HP=true,ID32=42};
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var d=Assert.IsType<PokemonDetails>(new Gen7PokemonCodec().Decode(bytes).Details);
        Assert.Equal("000042",d.OriginalTrainerIdText);Assert.Equal(0,d.PerfectIVCount);Assert.Equal(1,d.HyperTrainingCount);
        Assert.Equal(211,d.Stats.HP);Assert.Equal(0,d.IndividualValues.HP);
    }
    [Theory]
    [InlineData("team")]
    [InlineData("resort")]
    public void RegisteredOrResortPokemonAreNotRemoved(string condition)
    {
        var data=Gen7Fixtures.Create(GameVersion.SN,registered:condition=="team",resort:condition=="resort");var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Remove(data,new(false,31,29)));
        Assert.Equal(before,data);
    }
    [Theory]
    [InlineData(0x6CC00)] // USUM
    [InlineData(0x100000)] // encrypted container
    public void UnsupportedFormatsAreRejected(int length)=>Assert.Throws<InvalidDataException>(()=>new GameSaveReader().Read(new byte[length]));
}
