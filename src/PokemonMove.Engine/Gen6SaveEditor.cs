using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen6SaveEditor : ISaveEditor
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
        return new(Finish(save, location), summary, Title(save), save.OT);
    }
    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon, string formatKey = "pk4", DateOnly? transferDate = null)
    {
        var save = Open(source);
        if (location.IsParty) throw new InvalidDataException("引き出し先はボックスの空き枠を選択してください。");
        ValidateLocation(save, location);
        if (location.Box >= save.BoxesUnlocked) throw new InvalidDataException("ゲーム内で開放済みのボックスを選択してください。");
        if (save.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0) throw new InvalidDataException("引き出し先の枠は空いていません。");
        var original = new PokemonCodec().Decode(pokemon, formatKey);
        var plan = new PokemonTransfer().Prepare(original, "xy", transferDate, Gen6SaveReader.Profile(save));
        var pk = new PK6(plan.Pokemon.StoredData.ToArray()); ValidateMovable(pk);
        if (!save.Personal.IsPresentInGame(pk.Species, pk.Form))
            throw new InvalidDataException("このフォルムはXYに対応していません。対応する作品へ引き出してください。");
        if (pk.HeldItem != 0 && (pk.HeldItem > save.MaxItemID || !save.HeldItems.Contains((ushort)pk.HeldItem)))
            throw new InvalidDataException("この持ち物はXYに対応していません。");
        if (new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }.Any(m => m > save.MaxMoveID))
            throw new InvalidDataException("XYで使えない技を覚えています。対応する作品で技を変更してください。");
        var snapshot = new Gen6SaveReader().Read(source);
        if (snapshot.Party.Concat(snapshot.Boxes.SelectMany(b => b.Slots))
            .Any(p => p.CanStore && p.StoredData.AsSpan().SequenceEqual(plan.Pokemon.StoredData)))
            throw new InvalidDataException("同じデータのポケモンが、このセーブにすでに存在します。");
        save.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None);
        return new(Finish(save, location), original, Title(save), save.OT);
    }
    private static SAV6XY Open(byte[] data)
    {
        var save = Gen6SaveReader.Open(data);
        if (save.PartyCount is < 1 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    private static string Title(SAV6XY save) => save.Version == GameVersion.X ? "X" : "Y";
    private static void ValidateLocation(SAV6XY save, PokemonLocation p)
    {
        if (p.IsParty ? p.Slot < 0 || p.Slot >= save.PartyCount : p.Box < 0 || p.Box >= save.BoxCount || p.Slot < 0 || p.Slot >= save.BoxSlotCount)
            throw new InvalidDataException("選択した枠が見つかりません。セーブを開き直してください。");
    }
    private static void ValidateMovable(PKM pk)
    {
        if (pk.Species == (ushort)Species.Kyurem && pk.Form != 0)
            throw new InvalidDataException("合体中のキュレムは移動できません。ゲーム内で分離してください。");
        if (pk.Species == (ushort)Species.Furfrou && pk.Form != 0)
            throw new InvalidDataException("トリミング中のトリミアンは、ゲーム内で通常の姿に戻してから移動してください。");
        if (FormInfo.IsBattleOnlyForm(pk.Species, pk.Form, 6))
            throw new InvalidDataException("戦闘中だけのフォルムは移動できません。ゲーム内で通常の姿に戻してください。");
    }
    private static byte[] Finish(SAV6XY save, PokemonLocation p)
    {
        // No full Write(): preserve unrelated CRCs, secure timestamps, metadata and unused data.
        BlockInfo.SetChecksums([save.AllBlocks[p.IsParty ? 18 : 53]], save.Data);
        var result = save.Data.ToArray(); _ = Gen6SaveReader.Open(result);
        return result;
    }
}
