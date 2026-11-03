using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Decrypted X/Y main only. Encrypted cartridge/container saves and ORAS are not supported.</summary>
public sealed class Gen6SaveReader : PokemonMove.Domain.ISaveReader
{
    public const int MainSize = 0x65600;
    internal static SAV6XY Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != MainSize || GameSaveReader.Detect(data) is not SAV6XY save)
            throw new InvalidDataException("AzaharのXYのセーブフォルダーにある、復号済みmainファイル（415232バイト）を選択してください。");
        // Validate metadata too: CRCs alone do not cover lengths/IDs or the BEEF marker.
        const int metadata = MainSize - 0x200;
        foreach (var block in save.AllBlocks)
        {
            var entry = save.Data.Slice(metadata + 0x14 + (int)block.ID * 8, 8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != block.Length
                || BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) != block.ID)
                throw new InvalidDataException("XYのセーブのブロック情報が不正です。ゲーム内で保存し直してください。");
        }
        if (!save.ChecksumsValid)
            throw new InvalidDataException($"XYのセーブのチェックサムが一致しません。ゲーム内で保存し直したmainをお試しください。\n診断情報：{save.ChecksumInfo}");
        if (save.Version is not (GameVersion.X or GameVersion.Y))
            throw new InvalidDataException("XYの作品情報を確認できません。");
        if (save.PartyCount is < 0 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    internal static TransferTrainerProfile Profile(SAV6XY save) => new(save.OT, save.Gender, save.Country, save.Region, save.ConsoleRegion);
    public SaveSnapshot Read(byte[] data)
    {
        var save = Open(data);
        var party = Enumerable.Range(0, save.PartyCount).Select(i => PokemonCodec.Describe(save.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, save.BoxCount).Select(b => new BoxSnapshot(save.GetBoxName(b),
            Enumerable.Range(0, save.BoxSlotCount).Select(s => PokemonCodec.Describe(save.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        return new(save.OT, save.Version == GameVersion.X ? "X" : "Y", party, boxes)
        {
            FormatKey = "xy", UnlockedBoxCount = Math.Clamp(save.BoxesUnlocked, 0, save.BoxCount),
            TransferTrainer = Profile(save),
        };
    }
}
