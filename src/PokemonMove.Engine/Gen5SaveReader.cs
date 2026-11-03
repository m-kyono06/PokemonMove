using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen5SaveReader : PokemonMove.Domain.ISaveReader
{
    internal static SAV5 Open(byte[] data)
    {
        if (GameSaveReader.Detect(data) is not SAV5 save || save is not (SAV5BW or SAV5B2W2))
            throw new InvalidDataException("BW・BW2の通常セーブを選択してください。");
        // Validate both each local CRC and its mirror in the checksum table. Do not repair damaged input.
        if (!save.ChecksumsValid)
            throw new InvalidDataException($"BW・BW2のセーブのチェックサムが一致しません。ゲーム内で保存し直したファイルをお試しください。\n診断情報：{save.ChecksumInfo}");
        if (save is SAV5BW ? save.Version is not (GameVersion.B or GameVersion.W)
            : save.Version is not (GameVersion.B2 or GameVersion.W2))
            throw new InvalidDataException("BW・BW2の作品情報を確認できません。ゲーム内で保存し直してください。");
        if (save.PartyCount is < 0 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    public SaveSnapshot Read(byte[] data)
    {
        var save = Open(data);
        var party = Enumerable.Range(0, save.PartyCount).Select(i => PokemonCodec.Describe(save.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, save.BoxCount).Select(b => new BoxSnapshot(save.GetBoxName(b),
            Enumerable.Range(0, save.BoxSlotCount).Select(s => PokemonCodec.Describe(save.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        var title = save.Version switch
        {
            GameVersion.B => "ブラック", GameVersion.W => "ホワイト",
            GameVersion.B2 => "ブラック2", _ => "ホワイト2",
        };
        return new(save.OT, title, party, boxes)
        {
            FormatKey = save is SAV5BW ? "bw" : "b2w2",
            UnlockedBoxCount = Math.Clamp(save.BoxesUnlocked, 0, save.BoxCount),
        };
    }
}
