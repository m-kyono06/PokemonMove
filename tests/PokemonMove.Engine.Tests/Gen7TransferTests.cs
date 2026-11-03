using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class Gen7TransferTests
{
    [Theory]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.Y)]
    [InlineData(GameVersion.OR)]
    [InlineData(GameVersion.AS)]
    public void ForwardConversionPreservesIdentityAndIsReproducibleAfterOtherTrainersAreLoaded(GameVersion version)
    {
        var source=new GameSaveReader().Read(Gen6Fixtures.Create(version)).Boxes[30].Slots[29];
        var before=source.StoredData.ToArray();var profile=new GameSaveReader().Read(Gen7Fixtures.Create(GameVersion.SN)).TransferTrainer!;
        var plan=new PokemonTransfer().Prepare(source,"sm",trainer:profile);Assert.True(plan.IsGenerationTransfer);
        var original=new PK6(before.ToArray());var moved=new PK7(plan.Pokemon.StoredData.ToArray());
        Assert.Equal("pk7",plan.Pokemon.FormatKey);Assert.Equal(original.PID,moved.PID);Assert.Equal(original.EncryptionConstant,moved.EncryptionConstant);
        Assert.Equal(original.ID32,moved.ID32);Assert.Equal(original.Nature,moved.Nature);Assert.Equal(original.IsShiny,moved.IsShiny);
        Assert.Equal(original.IV32,moved.IV32);Assert.Equal(original.EV_ATK,moved.EV_ATK);Assert.Equal(original.EV_SPE,moved.EV_SPE);
        Assert.Equal(original.MetDate,moved.MetDate);Assert.Equal(original.MetLocation,moved.MetLocation);Assert.Equal(original.Move1,moved.Move1);
        Assert.Equal(0,moved.HyperTrainFlags);Assert.Equal(profile.Name,moved.HandlingTrainerName);Assert.True(moved.ChecksumValid);
        Assert.Equal("00042",plan.Pokemon.Details!.OriginalTrainerIdText); // origin trainer remains Gen6
        for(var i=0;i<5;i++)
        {
            RecentTrainerCache.SetRecentTrainer(new SimpleTrainerInfo(GameVersion.SN){OT="別の長いトレーナー名",Gender=1});
            Assert.Equal(plan.Pokemon.StoredData,new PokemonTransfer().Prepare(source,"sm",trainer:profile).Pokemon.StoredData);
        }
        Assert.Equal(before,source.StoredData);
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(plan.Pokemon,"oras"));
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(plan.Pokemon,"b2w2"));
    }
    [Theory]
    [InlineData("egg")]
    [InlineData("item")]
    [InlineData("fusion")]
    public void DisallowedConversionNeverChangesMain(string condition)
    {
        var pk=new PK6{Species=25,CurrentLevel=20,Version=GameVersion.X};
        if(condition=="egg")pk.IsEgg=true;
        if(condition=="item")pk.HeldItem=234;
        if(condition=="fusion"){pk.Species=646;pk.Form=1;}
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var data=Gen7Fixtures.Create(GameVersion.SN);var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(data,new(false,0,0),bytes,"pk6"));
        Assert.Equal(before,data);
    }
}
