using PKHeX.Core;
using System.Buffers.Binary;
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
        ValidateDisplayBlocks(hgss);
        var warnings = hgss.ChecksumsValid ? Array.Empty<string>() :
            new[] { "追加領域（バトルビデオ等）のチェックサムに不一致があります。手持ち・ボックスの検証は正常です。表示専用で読み込んでいます。" };
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
        return new(hgss.OT, hgss.Version == GameVersion.HG ? "ハートゴールド" : "ソウルシルバー", party, boxes) { Warnings = warnings };
    }

    // Match the block selection and CRC coverage in PKHeX's pinned SAV4 implementation.
    // Additional blocks are not consumed by this read-only viewer.
    private static void ValidateDisplayBlocks(SAV4HGSS save)
    {
        const int partitionSize = 0x40000;
        const int storageStart = 0xF700;
        const int storageSize = 0x12310;
        Validate(0, SAV4HGSS.GeneralSize, "主人公・手持ち");
        Validate(storageStart, storageSize, "ボックス");

        void Validate(int start, int length, string name)
        {
            var footer = start + length - 0x14;
            var partition = SAV4BlockDetection.CompareFooters(save.Data, footer, footer + partitionSize);
            var block = save.Data.Slice(start + partition * partitionSize, length);
            if (Checksums.CRC16_CCITT(block[..^0x10]) != BinaryPrimitives.ReadUInt16LittleEndian(block[^2..]))
                throw new InvalidDataException($"{name}領域のチェックサムが一致しません。通常セーブのコピーを確認してください。\n診断情報：{save.ChecksumInfo}");
        }
    }

}
