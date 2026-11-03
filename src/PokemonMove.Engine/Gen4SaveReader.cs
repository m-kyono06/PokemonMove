using PKHeX.Core;
using System.Buffers.Binary;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Creates a detached view of DP, Pt or HGSS. Never writes to the input file.</summary>
public sealed class Gen4SaveReader : PokemonMove.Domain.ISaveReader
{
    public const int RawSize = 0x80000;
    public const int MaximumFileSize = RawSize + 4096;

    public SaveSnapshot Read(byte[] data)
    {
        var save = Open(data);
        var warnings = save.ChecksumsValid ? Array.Empty<string>() :
            new[] { "追加領域（殿堂入り・バトルビデオ等）のチェックサムに不一致があります。手持ち・ボックスの検証は正常です。追加領域は変更しません。" };
        var party = Enumerable.Range(0, save.PartyCount).Select(i => Gen4PokemonCodec.Describe(save.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, save.BoxCount).Select(b => new BoxSnapshot(((IBoxDetailName)save).GetBoxName(b),
            Enumerable.Range(0, save.BoxSlotCount).Select(s => Gen4PokemonCodec.Describe(save.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        var (game, format) = save switch
        {
            // The DP save format does not reliably distinguish Diamond from Pearl.
            SAV4DP => ("ダイヤモンド・パール", "dp"),
            SAV4Pt => ("プラチナ", "pt"),
            SAV4HGSS when save.Version == GameVersion.HG => ("ハートゴールド", "hgss"),
            _ => ("ソウルシルバー", "hgss"),
        };
        return new(save.OT, game, party, boxes) { Warnings = warnings, FormatKey = format };
    }

    internal static SAV4 Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < RawSize || data.Length > MaximumFileSize)
            throw new InvalidDataException("D・P・Pt・HGSSの通常セーブ（512 KiB）またはDraSticのバッテリーセーブを選択してください。ステートセーブには対応していません。");
        if (SaveUtil.GetSaveFile(data.ToArray()) is not SAV4 save || save is not (SAV4DP or SAV4Pt or SAV4HGSS))
            throw new InvalidDataException("D・P・Pt・HGSSのセーブとして読み取れません。ゲーム内で保存したセーブを選択してください。");
        var layout = Gen4SaveLayout.For(save);
        foreach (var (start, length) in layout.Blocks)
        {
            var block = save.Data.Slice(Gen4SaveLayout.ActiveOffset(save, start, length), length);
            if (Checksums.CRC16_CCITT(block[..^layout.FooterSize]) != BinaryPrimitives.ReadUInt16LittleEndian(block[^2..]))
                throw new InvalidDataException($"{(start == 0 ? "主人公・手持ち" : "ボックス")}領域のチェックサムが一致しません。通常セーブのコピーを確認してください。\n診断情報：{save.ChecksumInfo}");
        }
        if (save is SAV4HGSS && save.Version is not (GameVersion.HG or GameVersion.SS))
            throw new InvalidDataException("HGSSの作品情報を確認できません。ゲーム内で保存し直してください。");
        if (save.PartyCount is < 0 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
}
