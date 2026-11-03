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
            new[] { "追加領域（バトルビデオ等）のチェックサムに不一致があります。手持ち・ボックスの検証は正常です。追加領域は変更しません。" };
        var party = Enumerable.Range(0, hgss.PartyCount).Select(i => HgssPokemonCodec.Describe(hgss.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, hgss.BoxCount).Select(b => new BoxSnapshot(hgss.GetBoxName(b),
            Enumerable.Range(0, hgss.BoxSlotCount).Select(s => HgssPokemonCodec.Describe(hgss.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
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
