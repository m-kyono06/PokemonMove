using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen7SaveEditor : ISaveEditor
{
    public SaveEdit Remove(byte[] source, PokemonLocation location)
    {
        var save = Open(source); ValidateLocation(save, location);
        var pk = location.IsParty ? save.GetPartySlotAtIndex(location.Slot) : save.GetBoxSlotAtIndex(location.Box, location.Slot);
        var summary = PokemonCodec.Describe(pk, location.Slot);
        if (!summary.CanStore) throw new InvalidDataException("空き枠または検証に失敗した個体は移動できません。");
        ValidateMovable(pk);
        // Resort slots hold separate copies of active participants. Never leave one behind.
        for (var i = 0; i < ResortSave7.ResortCount; i++)
        {
            var active = new PK7(save.ResortSave[i].ToArray());
            if (active.Species != 0 && active.Species == pk.Species && active.PID == pk.PID
                && active.ID32 == pk.ID32 && active.EncryptionConstant == pk.EncryptionConstant)
                throw new InvalidDataException("ポケリゾートにいる個体は、ゲーム内で戻してから移動してください。");
        }
        if (location.IsParty)
        {
            if (!Enumerable.Range(0, save.PartyCount).Where(i => i != location.Slot)
                .Select(save.GetPartySlotAtIndex).Any(p => p.Species != 0 && !p.IsEgg && p.Stat_HPCurrent > 0))
                throw new InvalidDataException("戦えるポケモンを手持ちに1匹以上残してください。");
            save.DeletePartySlot(location.Slot);
        }
        else save.SetBoxSlotAtIndex(save.BlankPKM, location.Box, location.Slot, EntityImportSettings.None);
        return new(Finish(save, location), summary, Gen7SaveReader.Title(save), save.OT);
    }
    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon, string formatKey = "pk4", DateOnly? transferDate = null)
    {
        var save = Open(source);
        if (location.IsParty) throw new InvalidDataException("引き出し先はボックスの空き枠を選択してください。");
        ValidateLocation(save, location);
        if (location.Box >= save.BoxesUnlocked) throw new InvalidDataException("ゲーム内で開放済みのボックスを選択してください。");
        if (save.GetBoxSlotAtIndex(location.Box, location.Slot).Species != 0) throw new InvalidDataException("引き出し先の枠は空いていません。");
        var original = new PokemonCodec().Decode(pokemon, formatKey);
        var plan = new PokemonTransfer().Prepare(original, Gen7SaveReader.Format(save), transferDate, Gen7SaveReader.Profile(save));
        var pk = new PK7(plan.Pokemon.StoredData.ToArray()); ValidateMovable(pk);
        if (pk.Species > save.MaxSpeciesID || !save.Personal.IsPresentInGame(pk.Species, pk.Form))
            throw new InvalidDataException("このフォルムは移動先の作品に対応していません。対応する作品へ引き出してください。");
        if (pk.HeldItem != 0 && (pk.HeldItem > save.MaxItemID || !save.HeldItems.Contains((ushort)pk.HeldItem)))
            throw new InvalidDataException("この持ち物は移動先の作品に対応していません。");
        if (new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4, pk.RelearnMove1, pk.RelearnMove2, pk.RelearnMove3, pk.RelearnMove4 }.Any(m => m > save.MaxMoveID))
            throw new InvalidDataException("移動先の作品で使えない技・思い出せる技があります。対応する作品へ引き出してください。");
        if (pk.Ability > save.MaxAbilityID)
            throw new InvalidDataException("移動先の作品で使えない特性です。対応する作品へ引き出してください。");
        var snapshot = new Gen7SaveReader().Read(source);
        if (snapshot.Party.Concat(snapshot.Boxes.SelectMany(b => b.Slots))
            .Any(p => p.CanStore && p.StoredData.AsSpan().SequenceEqual(plan.Pokemon.StoredData)))
            throw new InvalidDataException("同じデータのポケモンが、このセーブにすでに存在します。");
        save.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None);
        return new(Finish(save, location), original, Gen7SaveReader.Title(save), save.OT);
    }
    private static SAV7 Open(byte[] data)
    {
        var save = Gen7SaveReader.Open(data);
        if (save.PartyCount is < 1 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    private static void ValidateLocation(SAV7 save, PokemonLocation p)
    {
        if (p.IsParty ? p.Slot < 0 || p.Slot >= save.PartyCount : p.Box < 0 || p.Box >= save.BoxCount || p.Slot < 0 || p.Slot >= save.BoxSlotCount)
            throw new InvalidDataException("選択した枠が見つかりません。セーブを開き直してください。");
        if (!p.IsParty && save.BoxLayout.TeamSlots.Contains(p.Box * save.BoxSlotCount + p.Slot))
            throw new InvalidDataException("バトルチームに登録された個体・枠は移動できません。ゲーム内でチームから外してください。");
    }
    private static void ValidateMovable(PKM pk)
    {
        if (pk.Species == (ushort)Species.Kyurem && pk.Form != 0)
            throw new InvalidDataException("合体中のキュレムは移動できません。ゲーム内で分離してください。");
        if (pk.Species == (ushort)Species.Necrozma && pk.Form != 0)
            throw new InvalidDataException("合体中・ウルトラバースト中のネクロズマは移動できません。ゲーム内で通常の姿に戻してください。");
        if (pk.Species == (ushort)Species.Furfrou && pk.Form != 0)
            throw new InvalidDataException("トリミング中のトリミアンは、ゲーム内で通常の姿に戻してから移動してください。");
        if (FormInfo.IsBattleOnlyForm(pk.Species, pk.Form, 7))
            throw new InvalidDataException("戦闘中だけのフォルムは移動できません。ゲーム内で通常の姿に戻してください。");
    }
    private static byte[] Finish(SAV7 save, PokemonLocation p)
    {
        // Preserve all other blocks and battle-team assignments. Only update this block CRC and the save signature.
        BlockInfo.SetChecksums([save.AllBlocks[p.IsParty ? 4 : 14]], save.Data);
        var result = save.Data.ToArray(); Gen7SaveSignature.Sign(result);
        _ = Gen7SaveReader.Open(result);
        return result;
    }
}
