using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class Gen6TransferTests
{
    private static PokemonSummary Pokemon(PK5 pk)
    {
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        return new Gen5PokemonCodec().Decode(bytes);
    }
    [Theory]
    [InlineData(0u)]
    [InlineData(7u)]
    [InlineData(8u)]
    [InlineData(15u)]
    [InlineData(16u)]
    public void ForwardConversionPreservesShininessAndIdentityAndDisclosesEvAndPidChanges(uint pid)
    {
        var pk=new PK5{Species=25,PID=pid,ID32=0,CurrentLevel=50,Version=GameVersion.B2,Language=1,
            Nature=Nature.Jolly,IV_HP=31,IV_ATK=30,IV_SPE=15,EV_HP=255,EV_ATK=255,Move1=85,Move1_PP=1,
            Nickname="ピカ",IsNicknamed=true,OriginalTrainerName="メイ",OriginalTrainerGender=1,HiddenAbility=true,Ability=31,
            MetDate=new(2012,8,3),MetLocation=5,MetLevel=10};
        var original=Pokemon(pk);var before=original.StoredData.ToArray();
        var profile=new GameSaveReader().Read(Gen6Fixtures.Create(GameVersion.X)).TransferTrainer!;
        var plan=new PokemonTransfer().Prepare(original,"xy",trainer:profile);
        Assert.True(plan.IsGenerationTransfer);Assert.Equal("pk6",plan.Pokemon.FormatKey);
        var converted=new PK6(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(pk.PID,converted.EncryptionConstant);Assert.Equal(PK5.GetTransferPID(pk.PID,pk.ID32,out _),converted.PID);
        Assert.Equal(pk.IsShiny,converted.IsShiny);Assert.Equal(pk.ID32,converted.ID32);
        Assert.Equal(pk.Nature,converted.Nature);Assert.Equal(pk.IV_HP,converted.IV_HP);Assert.Equal(pk.IV_ATK,converted.IV_ATK);Assert.Equal(pk.IV_SPE,converted.IV_SPE);
        Assert.Equal(252,converted.EV_HP);Assert.Equal(252,converted.EV_ATK);Assert.Contains(plan.Notices,n=>n.Contains("努力値"));
        if(pid is 8 or 15) Assert.Contains(plan.Notices,n=>n.Contains("PID"));
        Assert.Equal(pk.Nickname,converted.Nickname);Assert.Equal(pk.OriginalTrainerName,converted.OriginalTrainerName);
        Assert.Equal(pk.OriginalTrainerGender,converted.OriginalTrainerGender);Assert.Equal(pk.MetDate,converted.MetDate);Assert.Equal(pk.MetLocation,converted.MetLocation);
        Assert.Equal(pk.Move1,converted.Move1);Assert.True(converted.Move1_PP>1);Assert.Equal(31,converted.Ability);
        Assert.Equal(profile.Name,converted.HandlingTrainerName);Assert.Equal(profile.Country,converted.Country);Assert.Equal(profile.Region,converted.Geo1_Region);
        Assert.Equal(1,converted.CurrentHandler);Assert.True(converted.ChecksumValid);
        for(var i=0;i<10;i++)
        {
            RecentTrainerCache.SetRecentTrainer(new SimpleTrainerInfo(GameVersion.OR){OT="長い別の親の名前",Gender=1});
            Assert.Equal(plan.Pokemon.StoredData,new PokemonTransfer().Prepare(original,"xy",trainer:profile).Pokemon.StoredData);
        }
        Assert.Equal(before,original.StoredData);
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(plan.Pokemon,"b2w2"));
        Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(plan.Pokemon,"hgss"));
    }

    [Theory]
    [InlineData("egg")]
    [InlineData("item")]
    [InlineData("fusion")]
    [InlineData("profile")]
    public void RejectedForwardMoveNeverChangesSourceOrMain(string condition)
    {
        var pk=new PK5{Species=25,CurrentLevel=20,Version=GameVersion.B};
        if(condition=="egg")pk.IsEgg=true;
        if(condition=="item")pk.HeldItem=234;
        if(condition=="fusion"){pk.Species=646;pk.Form=1;}
        var source=Pokemon(pk);var before=source.StoredData.ToArray();
        var data=Gen6Fixtures.Create(GameVersion.Y);var main=data.ToArray();
        if(condition=="profile") Assert.Throws<InvalidDataException>(()=>new PokemonTransfer().Prepare(source,"xy"));
        else Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(data,new(false,0,0),source.StoredData,"pk5"));
        Assert.Equal(before,source.StoredData);Assert.Equal(main,data);
    }

    [Fact]
    public void FourthGenerationMustPassThroughGen5AndCannotBeImplicitlyConvertedTwice()
    {
        var source=new GameSaveReader().Read(Gen4Fixtures.Create(GameVersion.SS)).Boxes[17].Slots[29];
        var data=Gen6Fixtures.Create(GameVersion.X);var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().Insert(data,new(false,0,0),source.StoredData,"pk4",new(2026,11,3)));
        Assert.Equal(before,data);
    }
}
