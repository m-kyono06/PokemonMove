using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Edits only active HGSS general/storage blocks, preserving DSV footer and optional blocks.</summary>
public sealed class HgssSaveEditor : ISaveEditor
{
    private const int StorageStart = 0xF700;
    private const int StorageSize = 0x12310;

    public SaveEdit Remove(byte[] source, PokemonLocation location)
    {
        var (save, snapshot) = Open(source);
        ValidateLocation(save, location);
        var pokemon = location.IsParty ? save.GetPartySlotAtIndex(location.Slot) : save.GetBoxSlotAtIndex(location.Box, location.Slot);
        var summary = HgssPokemonCodec.Describe(pokemon, location.Slot);
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
            save.FlagsBoxContentChanged |= 1 << location.Box;
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
        var summary = new HgssPokemonCodec().Decode(pokemon);
        // Do not insert a second byte-identical instance into the destination save.
        if (snapshot.Party.Concat(snapshot.Boxes.SelectMany(b => b.Slots))
            .Any(p => p.CanStore && p.StoredData.AsSpan().SequenceEqual(summary.StoredData)))
            throw new InvalidDataException("同じデータのポケモンが、このセーブにすでに存在します。");
        var pk = new PK4(summary.StoredData.ToArray());
        if (ItemStorage4.Mail.Contains((ushort)pk.HeldItem)) throw new InvalidDataException("メールを持つ個体には対応していません。");
        save.SetBoxSlotAtIndex(pk, location.Box, location.Slot, EntityImportSettings.None);
        save.FlagsBoxContentChanged |= 1 << location.Box;
        return new(Finish(source, save), summary, snapshot.Game, snapshot.TrainerName);
    }

    private static (SAV4HGSS, SaveSnapshot) Open(byte[] source)
    {
        var view = new HgssSaveReader().Read(source); // Strict active-block checks, including warning-only optional blocks.
        var save = SaveUtil.GetSaveFile(source.ToArray()) as SAV4HGSS ?? throw new InvalidDataException("HGSSの通常セーブを選択してください。");
        if (save.Version is not (GameVersion.HG or GameVersion.SS)) throw new InvalidDataException("作品情報を確認できないため、このセーブには書き込めません。");
        if (save.PartyCount is < 1 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return (save, view);
    }

    private static void ValidateLocation(SAV4HGSS save, PokemonLocation p)
    {
        if (p.IsParty ? p.Slot < 0 || p.Slot >= save.PartyCount : p.Box < 0 || p.Box >= save.BoxCount || p.Slot < 0 || p.Slot >= save.BoxSlotCount)
            throw new InvalidDataException("選択した枠が見つかりません。セーブを開き直してください。");
    }

    private static byte[] Finish(byte[] original, SAV4HGSS save)
    {
        Refresh(0, SAV4HGSS.GeneralSize);
        Refresh(StorageStart, StorageSize);
        var result = original.ToArray();
        save.Data.CopyTo(result.AsSpan(0, HgssSaveReader.RawSize));
        _ = new HgssSaveReader().Read(result);
        return result;

        void Refresh(int start, int length)
        {
            var footer = start + length - 0x14;
            var partition = SAV4BlockDetection.CompareFooters(save.Data, footer, footer + 0x40000);
            var block = save.Data.Slice(start + partition * 0x40000, length);
            BinaryPrimitives.WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^0x10]));
        }
    }
}
