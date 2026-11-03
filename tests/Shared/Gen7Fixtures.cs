using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Engine;

namespace PokemonMove.TestSupport;

internal static class Gen7Fixtures
{
    internal static byte[] Create(GameVersion version, PK7? boxPokemon = null, int unlocked = 32, bool registered = false, bool resort = false)
    {
        SAV7 save = version is GameVersion.SN or GameVersion.MN ? new SAV7SM() : new SAV7USUM();
        save.Version=version;save.OT="ヨウ";save.Gender=0;save.Country=1;save.Region=2;save.ConsoleRegion=0;save.Language=1;save.BoxesUnlocked=unlocked;
        save.TimeStampCurrent=123456789;save.TimeStampPrevious=987654;
        var metadata=save.Data.Length-0x200;
        BinaryPrimitives.WriteUInt32LittleEndian(save.Data[(metadata+0x10)..],0x42454546);
        foreach(var block in save.AllBlocks)
        {
            var entry=save.Data.Slice(metadata+0x14+(int)block.ID*8,8);
            BinaryPrimitives.WriteUInt32LittleEndian(entry,(uint)block.Length);BinaryPrimitives.WriteUInt16LittleEndian(entry[4..],(ushort)block.ID);
        }
        for(var i=0;i<2;i++)
        {
            var pk=new PK7{Species=(ushort)(725+i),PID=(uint)(100+i),EncryptionConstant=(uint)(777+i),Version=version,CurrentLevel=20};
            pk.ResetPartyStats();save.SetPartySlotAtIndex(pk,i,EntityImportSettings.None);
        }
        var boxed=boxPokemon??new PK7{Species=722,Nickname="モク",CurrentLevel=50,PID=91,EncryptionConstant=333,Version=version,ID32=1234567,
            IV_HP=31,IV_ATK=31,IV_DEF=31,IV_SPA=31,IV_SPD=31,IV_SPE=31,EV_ATK=252,EV_SPE=252,EV_HP=6,Nature=Nature.Adamant,Ability=65,Move1=33};
        save.SetBoxSlotAtIndex(boxed,31,29,EntityImportSettings.None);save.SetBoxName(31,"さいご");
        save.Data.Slice(save is SAV7USUM ? 0x65600 : 0x65000,0xB60).Fill(0x6B); // Preserve opaque Pokefinder data.
        if(registered)save.BoxLayout.TeamSlots[0]=31*30+29;
        if(resort)boxed.WriteEncryptedDataStored(save.ResortSave[0].Span);
        var result=save.Write().ToArray();Gen7SaveSignature.Sign(result);return result;
    }
}
