using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Decrypted XY and ORAS main files. Encrypted containers and demo saves are not supported.</summary>
public sealed class Gen6SaveReader : PokemonMove.Domain.ISaveReader
{
    public const int MainSize = 0x65600;
    public const int OrasMainSize = 0x76000;
    internal static SAV6 Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((data.Length != MainSize && data.Length != OrasMainSize) || GameSaveReader.Detect(data) is not SAV6 save || save is not (SAV6XY or SAV6AO))
            throw new InvalidDataException("AzaharのXY・ORASのセーブフォルダーにある、復号済みmainファイルを選択してください。");
        // Validate metadata too: CRCs alone do not cover lengths/IDs or the BEEF marker.
        var metadata = data.Length - 0x200;
        foreach (var block in save.AllBlocks)
        {
            var entry = save.Data.Slice(metadata + 0x14 + (int)block.ID * 8, 8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != block.Length
                || BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) != block.ID)
                throw new InvalidDataException("XY・ORASのセーブのブロック情報が不正です。ゲーム内で保存し直してください。");
        }
        if (!save.ChecksumsValid)
            throw new InvalidDataException($"XY・ORASのセーブのチェックサムが一致しません。ゲーム内で保存し直したmainをお試しください。\n診断情報：{save.ChecksumInfo}");
        if (save is SAV6XY ? save.Version is not (GameVersion.X or GameVersion.Y) : save.Version is not (GameVersion.OR or GameVersion.AS))
            throw new InvalidDataException("XY・ORASの作品情報を確認できません。");
        if (save.PartyCount is < 0 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    internal static TransferTrainerProfile Profile(SAV6 save) => new(save.OT, save.Gender, save.Country, save.Region, save.ConsoleRegion);
    internal static string Format(SAV6 save) => save is SAV6AO ? "oras" : "xy";
    internal static string Title(SAV6 save) => save.Version switch
    {
        GameVersion.X => "X", GameVersion.Y => "Y", GameVersion.OR => "オメガルビー", _ => "アルファサファイア",
    };
    public SaveSnapshot Read(byte[] data)
    {
        var save = Open(data);
        var party = Enumerable.Range(0, save.PartyCount).Select(i => PokemonCodec.Describe(save.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, save.BoxCount).Select(b => new BoxSnapshot(((IBoxDetailName)save).GetBoxName(b),
            Enumerable.Range(0, save.BoxSlotCount).Select(s => PokemonCodec.Describe(save.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        return new(save.OT, Title(save), party, boxes)
        {
            FormatKey = Format(save), UnlockedBoxCount = Math.Clamp(save.BoxesUnlocked, 0, save.BoxCount),
            TransferTrainer = Profile(save),
        };
    }
}
