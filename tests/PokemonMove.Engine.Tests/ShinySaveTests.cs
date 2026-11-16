using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class ShinySaveTests
{
    private static readonly GameVersion[] Games=[GameVersion.D,GameVersion.P,GameVersion.Pt,GameVersion.HG,GameVersion.SS,
        GameVersion.B,GameVersion.W,GameVersion.B2,GameVersion.W2,GameVersion.X,GameVersion.Y,GameVersion.OR,GameVersion.AS,
        GameVersion.SN,GameVersion.MN,GameVersion.US,GameVersion.UM];
    public static IEnumerable<object[]> Slots=>from game in Games from party in new[]{false,true} select new object[]{game,party};
    private static int Generation(GameVersion g)=>g switch{GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS=>4,
        GameVersion.B or GameVersion.W or GameVersion.B2 or GameVersion.W2=>5,GameVersion.X or GameVersion.Y or GameVersion.OR or GameVersion.AS=>6,_=>7};
    private static byte[] Save(GameVersion g)=>Generation(g) switch{4=>Gen4Fixtures.Create(g,dsv:true),5=>Gen5Fixtures.Create(g,dsv:true,boxPokemon:new PK5{Species=649,Gender=2,PID=91,TID16=42,CurrentLevel=50,Version=g,Ability=88}),6=>Gen6Fixtures.Create(g),_=>Gen7Fixtures.Create(g)};
    private static SaveFile Core(byte[] data)=>SaveUtil.GetSaveFile(data.Take(data.Length>0x80000?0x80000:data.Length).ToArray())!;
    [Theory]
    [MemberData(nameof(Slots))]
    public void ChangesOnlyExistingPokemonPidAndChecksumsAcrossAllGamesAndLocations(GameVersion game,bool party)
    {
        var data=Save(game);var before=data.ToArray();var snapshot=new GameSaveReader().Read(data);
        var location=new PokemonLocation(party,snapshot.Boxes.Count-1,party?0:29);
        var original=party?Core(data).GetPartySlotAtIndex(0):Core(data).GetBoxSlotAtIndex(location.Box,29);
        var view=party?snapshot.Party[0]:snapshot.Boxes[location.Box].Slots[29];
        var plan=ShinyPokemonChange.Prepare(view);
        var edit=new GameSaveEditor().MakeShiny(data,location);var changed=new GameSaveReader().Read(edit.After);
        var result=party?Core(edit.After).GetPartySlotAtIndex(0):Core(edit.After).GetBoxSlotAtIndex(location.Box,29);
        Assert.True(result.IsShiny);Assert.Equal(view.StoredData,edit.Pokemon.StoredData);
        Assert.Equal(plan.Pokemon.StoredData,(party?changed.Party[0]:changed.Boxes[location.Box].Slots[29]).StoredData);
        Assert.Equal(snapshot.Party.Count,changed.Party.Count);Assert.Equal(snapshot.Boxes.Count,changed.Boxes.Count);
        for(var b=0;b<snapshot.Boxes.Count;b++)for(var slot=0;slot<30;slot++)
            if(party||b!=location.Box||slot!=29)Assert.Equal(snapshot.Boxes[b].Slots[slot].StoredData,changed.Boxes[b].Slots[slot].StoredData);
        for(var p=0;p<snapshot.Party.Count;p++)if(!party||p!=0)Assert.Equal(snapshot.Party[p].StoredData,changed.Party[p].StoredData);
        Assert.Equal(original.Stat_HPCurrent,result.Stat_HPCurrent);Assert.Equal(original.Status_Condition,result.Status_Condition);
        Assert.Equal(original.ID32,result.ID32);Assert.Equal(original.Gender,result.Gender);Assert.Equal(original.Nature,result.Nature);
        Assert.Equal(original.AbilityNumber,result.AbilityNumber);Assert.Equal(original.Characteristic,result.Characteristic);
        result.PID=original.PID;result.RefreshChecksum();Assert.Equal(original.Data.ToArray(),result.Data.ToArray());
        Assert.Equal(before,data);Assert.Equal(data.Length,edit.After.Length);
        Assert.Equal(edit.After,new GameSaveEditor().MakeShiny(before,location).After);
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().MakeShiny(edit.After,location));
    }
    [Theory]
    [InlineData(GameVersion.SS)][InlineData(GameVersion.B2)][InlineData(GameVersion.X)][InlineData(GameVersion.UM)]
    public void InjuredStatusAffectedPartyPokemonIsNotHealedOrReset(GameVersion game)
    {
        var core=Core(Save(game));var pk=core.GetPartySlotAtIndex(0);
        pk.Stat_HPCurrent=1;pk.Status_Condition=8;pk.Move1=33;pk.Move1_PP=1;
        core.SetPartySlotAtIndex(pk,0,EntityImportSettings.None);
        var data=core.Write().ToArray();if(Generation(game)==7)Gen7SaveSignature.Sign(data);
        var edit=new GameSaveEditor().MakeShiny(data,new(true,0,0));var changed=Core(edit.After).GetPartySlotAtIndex(0);
        Assert.True(changed.IsShiny);Assert.Equal(1,changed.Stat_HPCurrent);Assert.Equal(8,changed.Status_Condition);Assert.Equal(1,changed.Move1_PP);
        Assert.Equal(2,new GameSaveReader().Read(edit.After).Party.Count);
    }

    public static IEnumerable<object[]> Identities=>from generation in new[]{4,5,6,7} from nature in Enumerable.Range(0,25) select new object[]{generation,nature};
    [Theory]
    [MemberData(nameof(Identities))]
    public void AllNaturesRemainUnchangedAlongWithPidLinkedTraits(int generation,int nature)
    {
        PKM pk=generation switch{4=>new PK4(),5=>new PK5(),6=>new PK6(),_=>new PK7()};
        pk.Species=133;pk.Version=generation switch{4=>GameVersion.SS,5=>GameVersion.B2,6=>GameVersion.AS,_=>GameVersion.UM};
        pk.PID=(uint)(123450+nature);pk.Nature=(Nature)nature;pk.ID32=0x1234ABCD;pk.EncryptionConstant=555;
        pk.Gender=generation<=5?EntityGender.GetFromPIDAndRatio(pk.PID,pk.PersonalInfo.Gender):(byte)1;
        pk.RefreshAbility(generation<=5?pk.PIDAbility:0);pk.CurrentLevel=50;
        pk.IV_HP=pk.IV_ATK=pk.IV_DEF=pk.IV_SPA=pk.IV_SPD=pk.IV_SPE=31;
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var source=new PokemonCodec().Decode(bytes,$"pk{generation}");var result=ShinyPokemonChange.Prepare(source).Pokemon;
        Assert.True(result.IsShiny);Assert.Equal(source.Details!.Nature,result.Details!.Nature);
        Assert.Equal(source.Details.Gender,result.Details.Gender);Assert.Equal(source.Details.OriginalTrainerIdText,result.Details.OriginalTrainerIdText);
        Assert.Equal(source.Details.IndividualValues,result.Details.IndividualValues);
    }
    [Theory]
    [InlineData(4)][InlineData(5)]
    public void WurmpleEvolutionBranchIsRetainedAndSpindaPatternChangeIsDisclosed(int generation)
    {
        PKM pk=generation==4?new PK4():new PK5();pk.Species=265;pk.PID=123456789;pk.Version=generation==4?GameVersion.SS:GameVersion.B2;
        pk.Gender=EntityGender.GetFromPIDAndRatio(pk.PID,pk.PersonalInfo.Gender);pk.ID32=42;pk.CurrentLevel=20;pk.RefreshChecksum();
        var bytes=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);var s=new PokemonCodec().Decode(bytes,$"pk{generation}");
        var plan=ShinyPokemonChange.Prepare(s);PKM result=generation==4?new PK4(plan.Pokemon.StoredData.ToArray()):new PK5(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(WurmpleUtil.GetWurmpleEvoVal(pk.PID),WurmpleUtil.GetWurmpleEvoVal(result.PID));
        pk.Species=327;pk.WriteEncryptedDataStored(bytes);s=new PokemonCodec().Decode(bytes,$"pk{generation}");
        Assert.Contains(ShinyPokemonChange.Prepare(s).Notices,n=>n.Contains("模様"));
    }
    [Theory]
    [InlineData(6,GameVersion.SS)][InlineData(7,GameVersion.SS)]
    [InlineData(6,GameVersion.B2)][InlineData(7,GameVersion.B2)]
    public void TransferredPokemonKeepGenderNatureAndEncryptionConstant(int format,GameVersion origin)
    {
        PKM pk=format==6?new PK6():new PK7();pk.Species=25;pk.Version=origin;pk.PID=123463;pk.ID32=0x1234ABCD;
        pk.EncryptionConstant=987654;pk.Nature=(Nature)(pk.PID%25);pk.Gender=EntityGender.GetFromPIDAndRatio(pk.PID,pk.PersonalInfo.Gender);
        pk.Ability=9;pk.AbilityNumber=1;pk.CurrentLevel=50;
        var data=new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(data);var source=new PokemonCodec().Decode(data,$"pk{format}");
        var plan=ShinyPokemonChange.Prepare(source);PKM result=format==6?new PK6(plan.Pokemon.StoredData.ToArray()):new PK7(plan.Pokemon.StoredData.ToArray());
        Assert.True(result.IsShiny);Assert.Equal(pk.Nature,result.Nature);Assert.Equal(pk.Gender,result.Gender);
        Assert.Equal(pk.EncryptionConstant,result.EncryptionConstant);Assert.Equal(pk.Version,result.Version);Assert.Equal(pk.ID32,result.ID32);
    }

    [Theory]
    [InlineData("empty")][InlineData("egg")][InlineData("team")][InlineData("resort")][InlineData("corrupt")]
    public void UnsupportedOrProtectedSlotsNeverChangeInput(string condition)
    {
        var pk=new PK7{Species=25,PID=1234567,ID32=42,EncryptionConstant=123,CurrentLevel=20,Version=GameVersion.UM};
        if(condition=="egg")pk.IsEgg=true;
        var data=Gen7Fixtures.Create(GameVersion.UM,boxPokemon:pk,registered:condition=="team",resort:condition=="resort");
        if(condition=="corrupt")data[0x1600]^=1;
        var before=data.ToArray();
        Assert.Throws<InvalidDataException>(()=>new GameSaveEditor().MakeShiny(data,new(false,condition=="empty"?0:31,condition=="empty"?0:29)));
        Assert.Equal(before,data);
    }
}
