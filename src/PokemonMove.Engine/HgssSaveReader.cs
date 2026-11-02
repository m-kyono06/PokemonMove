using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Creates a detached, read-only view. Never writes to the input file.</summary>
public sealed class HgssSaveReader : PokemonMove.Domain.ISaveReader
{
    public const int RawSize = 0x80000;
    public const int MaximumFileSize = RawSize + 4096;

    public SaveSnapshot Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < RawSize || data.Length > MaximumFileSize)
            throw new InvalidDataException("HGSSの通常セーブ（512 KiB）またはDraSticのバッテリーセーブを選択してください。ステートセーブには対応していません。");
        var save = SaveUtil.GetSaveFile(data.ToArray());
        if (save is not SAV4HGSS hgss)
            throw new InvalidDataException("HGSSのセーブとして読み取れません。ゲーム内で保存したセーブを選択してください。");
        if (!hgss.ChecksumsValid)
            throw new InvalidDataException("セーブのチェックサムが一致しません。ゲーム内で保存し直したファイルをお試しください。");
        var strings = GameInfo.GetStrings("ja");
        PokemonSummary Describe(PKM p, int slot)
        {
            if (p.Species == 0)
                return new(slot, 0, "空き", "", 0, "", [], false);
            var personal = p.PersonalInfo;
            string At(string[] values, int index) => index < values.Length ? values[index] : $"#{index}";
            var types = new[] { At(strings.types, personal.Type1), At(strings.types, personal.Type2) }.Distinct();
            return new(slot, p.Species, At(strings.specieslist, p.Species), p.Nickname,
                p.CurrentLevel, string.Join(" / ", types),
                new[] { p.Move1, p.Move2, p.Move3, p.Move4 }.Where(m => m != 0)
                    .Select(m => At(strings.movelist, m)).ToArray(), p.IsShiny);
        }
        var party = Enumerable.Range(0, hgss.PartyCount).Select(i => Describe(hgss.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, hgss.BoxCount).Select(b => new BoxSnapshot(hgss.GetBoxName(b),
            Enumerable.Range(0, hgss.BoxSlotCount).Select(s => Describe(hgss.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        return new(hgss.OT, hgss.Version == GameVersion.HG ? "ハートゴールド" : "ソウルシルバー", party, boxes);
    }
}
