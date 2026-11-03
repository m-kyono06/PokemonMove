using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Edits only the selected party/box block and its CRCs. Backup regions and DSV footer stay intact.</summary>
public sealed class Gen5SaveEditor : ISaveEditor
{
    public SaveEdit Remove(byte[] source, PokemonLocation location)
    {
        var save = Open(source); ValidateLocation(save, location);
        var pk = location.IsParty ? save.GetPartySlotAtIndex(location.Slot) : save.GetBoxSlotAtIndex(location.Box, location.Slot);
        var summary = PokemonCodec.Describe(pk, location.Slot);
        if (!summary.CanStore) throw new InvalidDataException("空き枠または検証に失敗した個体は移動できません。");
        ValidateMovable(pk);
        if (location.IsParty)
        {
            if (!Enumerable.Range(0, save.PartyCount).Where(i => i != location.Slot)
                .Select(save.GetPartySlotAtIndex).Any(p => p.Species != 0 && !p.IsEgg && p.Stat_HPCurrent > 0))
                throw new InvalidDataException("戦えるポケモンを手持ちに1匹以上残してください。");
            save.DeletePartySlot(location.Slot);
        }
        else save.SetBoxSlotAtIndex(save.BlankPKM, location.Box, location.Slot, EntityImportSettings.None);
        return new(Finish(source, save, location), summary, Title(save), save.OT);
    }
    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon, string formatKey = "pk4", DateOnly? transferDate = null)
    {
        var save = Open(source);
        if (location.IsParty) throw new InvalidDataException("引き出し先はボックスの空き枠を選択してください。");
        ValidateLocation(save, location);
        if (location.Box >= save.BoxesUnlocked)
            throw new InvalidDataException("ゲーム内で開放済みのボックスを選択してください。");
        if (save.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0)
            throw new InvalidDataException("引き出し先の枠は空いていません。");
        var sourcePokemon = new PokemonCodec().Decode(pokemon, formatKey);
        var plan = new PokemonTransfer().Prepare(sourcePokemon, save is SAV5BW ? "bw" : "b2w2", transferDate);
        var summary = plan.Pokemon;
        var pk = new PK5(summary.StoredData.ToArray()); ValidateMovable(pk);
        if (!save.Personal.IsPresentInGame(pk.Species, pk.Form))
            throw new InvalidDataException("このフォルムは移動先の作品に対応していません。対応する作品へ引き出してください。");
        if (pk.HeldItem != 0 && (pk.HeldItem > save.MaxItemID || !save.HeldItems.Contains((ushort)pk.HeldItem)))
            throw new InvalidDataException("この持ち物は移動先の作品に対応していません。対応する作品へ引き出し、ゲーム内で持ち物を外してください。");
        var view = new Gen5SaveReader().Read(source);
        if (view.Party.Concat(view.Boxes.SelectMany(b => b.Slots))
            .Any(p => p.CanStore && p.StoredData.AsSpan().SequenceEqual(summary.StoredData)))
            throw new InvalidDataException("同じデータのポケモンが、このセーブにすでに存在します。");
        save.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None);
        return new(Finish(source, save, location), sourcePokemon, Title(save), save.OT);
    }
    private static SAV5 Open(byte[] data)
    {
        var save = Gen5SaveReader.Open(data);
        if (save.PartyCount is < 1 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    private static string Title(SAV5 save) => save.Version switch
    { GameVersion.B => "ブラック", GameVersion.W => "ホワイト", GameVersion.B2 => "ブラック2", _ => "ホワイト2" };
    private static void ValidateLocation(SAV5 save, PokemonLocation p)
    {
        if (p.IsParty ? p.Slot < 0 || p.Slot >= save.PartyCount : p.Box < 0 || p.Box >= save.BoxCount || p.Slot < 0 || p.Slot >= save.BoxSlotCount)
            throw new InvalidDataException("選択した枠が見つかりません。セーブを開き直してください。");
    }
    private static void ValidateMovable(PKM pk)
    {
        if (pk.Species == (ushort)Species.Kyurem && pk.Form != 0)
            throw new InvalidDataException("合体中のキュレムは移動できません。ゲーム内で分離してください。");
        if (ItemStorage4.Mail.Contains((ushort)pk.HeldItem))
            throw new InvalidDataException("メールを持っているポケモンは、ゲーム内でメールを外してから移動してください。");
    }
    private static byte[] Finish(byte[] original, SAV5 save, PokemonLocation p)
    {
        // PKHeX's block metadata writes the local CRC and table mirror, then refreshes the table CRC.
        var changed = save.AllBlocks[p.IsParty ? 26 : p.Box + 1];
        BlockInfo.SetChecksums([changed, save.AllBlocks[^1]], save.Data);
        var result = original.ToArray(); save.Data.CopyTo(result.AsSpan(0, Gen4SaveReader.RawSize));
        _ = Gen5SaveReader.Open(result);
        return result;
    }
}
