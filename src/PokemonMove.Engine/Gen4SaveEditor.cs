using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Edits only active Generation 4 general/storage blocks, preserving DSV footer and optional blocks.</summary>
public sealed class Gen4SaveEditor : ISaveEditor
{
    public SaveEdit Remove(byte[] source, PokemonLocation location)
    {
        var (save, snapshot) = Open(source);
        ValidateLocation(save, location);
        var pokemon = location.IsParty ? save.GetPartySlotAtIndex(location.Slot) : save.GetBoxSlotAtIndex(location.Box, location.Slot);
        var summary = Gen4PokemonCodec.Describe(pokemon, location.Slot);
        if (!summary.CanStore) throw new InvalidDataException("空き枠または検証に失敗した個体は移動できません。");
        if (ItemStorage4.Mail.Contains((ushort)pokemon.HeldItem)) throw new InvalidDataException("メールを持っているポケモンは、ゲーム内でメールを外してから移動してください。");
        if (location.IsParty)
        {
            if (!Enumerable.Range(0, save.PartyCount).Where(i => i != location.Slot)
                .Select(save.GetPartySlotAtIndex).Any(p => p.Species != 0 && !p.IsEgg && p.Stat_HPCurrent > 0))
                throw new InvalidDataException("戦えるポケモンを手持ちに1匹以上残してください。");
            save.DeletePartySlot(location.Slot);
        }
        else
        {
            save.SetBoxSlotAtIndex(save.BlankPKM, location.Box, location.Slot, EntityImportSettings.None);
            if (save is SAV4HGSS hgss) hgss.FlagsBoxContentChanged |= 1 << location.Box;
        }
        return new(Finish(source, save), summary, snapshot.Game, snapshot.TrainerName);
    }

    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon)
    {
        var (save, snapshot) = Open(source);
        if (location.IsParty) throw new InvalidDataException("引き出し先はボックスの空き枠を選択してください。");
        ValidateLocation(save, location);
        if (save.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0)
            throw new InvalidDataException("引き出し先の枠は空いていません。");
        var summary = new Gen4PokemonCodec().Decode(pokemon);
        // Do not insert a second byte-identical instance into the destination save.
        if (snapshot.Party.Concat(snapshot.Boxes.SelectMany(b => b.Slots))
            .Any(p => p.CanStore && p.StoredData.AsSpan().SequenceEqual(summary.StoredData)))
            throw new InvalidDataException("同じデータのポケモンが、このセーブにすでに存在します。");
        var pk = new PK4(summary.StoredData.ToArray());
        if (ItemStorage4.Mail.Contains((ushort)pk.HeldItem)) throw new InvalidDataException("メールを持つ個体には対応していません。");
        ValidateCompatibility(save, pk);
        save.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None);
        if (save is SAV4HGSS hgss) hgss.FlagsBoxContentChanged |= 1 << location.Box;
        return new(Finish(source, save), summary, snapshot.Game, snapshot.TrainerName);
    }

    private static (SAV4, SaveSnapshot) Open(byte[] source)
    {
        var save = Gen4SaveReader.Open(source);
        var view = new Gen4SaveReader().Read(source);
        if (save.PartyCount is < 1 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return (save, view);
    }

    private static void ValidateCompatibility(SAV4 save, PK4 pk)
    {
        if (!save.Personal.IsPresentInGame(pk.Species, pk.Form)
            || (save is SAV4Sinnoh && pk.Species == (ushort)Species.Pichu && pk.Form != 0))
            throw new InvalidDataException("このフォルムは移動先の作品に対応していません。対応する作品へ引き出してください。");
        if (pk.HeldItem != 0 && !save.HeldItems.Contains((ushort)pk.HeldItem))
            throw new InvalidDataException("この持ち物は移動先の作品に対応していません。対応する作品へ引き出し、ゲーム内で持ち物を外してください。");
    }

    private static void ValidateLocation(SAV4 save, PokemonLocation p)
    {
        if (p.IsParty ? p.Slot < 0 || p.Slot >= save.PartyCount : p.Box < 0 || p.Box >= save.BoxCount || p.Slot < 0 || p.Slot >= save.BoxSlotCount)
            throw new InvalidDataException("選択した枠が見つかりません。セーブを開き直してください。");
    }

    private static byte[] Finish(byte[] original, SAV4 save)
    {
        var layout = Gen4SaveLayout.For(save);
        foreach (var (start, length) in layout.Blocks)
        {
            var block = save.Data.Slice(Gen4SaveLayout.ActiveOffset(save, start, length), length);
            BinaryPrimitives.WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^layout.FooterSize]));
        }
        var result = original.ToArray();
        save.Data.CopyTo(result.AsSpan(0, Gen4SaveReader.RawSize));
        _ = new Gen4SaveReader().Read(result);
        return result;
    }
}
