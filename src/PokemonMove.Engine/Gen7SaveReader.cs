using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen7SaveReader : PokemonMove.Domain.ISaveReader
{
    public const int MainSize = 0x6BE00;
    internal const int SignatureOffset = 0x6BB00;
    internal static SAV7SM Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != MainSize || GameSaveReader.Detect(data) is not SAV7SM save)
            throw new InvalidDataException("Azaharのサン・ムーンの復号済みmainファイルを選択してください。USUM・体験版には未対応です。");
        // Verify the original signature before using PKHeX's normalized, signature-cleared buffer.
        if (!Gen7SaveSignature.Verify(data))
            throw new InvalidDataException("サン・ムーンのセーブの署名が一致しません。ゲーム内で保存し直したmainをお試しください。");
        var normalized = data.ToArray(); normalized.AsSpan(SignatureOffset, 0x80).Clear();
        if (!normalized.AsSpan().SequenceEqual(save.Data))
            throw new InvalidDataException("サン・ムーンのバトルチーム情報を確認できません。ゲーム内で保存し直してください。");
        foreach (var block in save.AllBlocks)
        {
            var entry = save.Data.Slice(MainSize - 0x200 + 0x14 + (int)block.ID * 8, 8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != block.Length || BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) != block.ID)
                throw new InvalidDataException("サン・ムーンのブロック情報が不正です。");
        }
        if (!save.ChecksumsValid) throw new InvalidDataException($"サン・ムーンのセーブのチェックサムが一致しません。\n診断情報：{save.ChecksumInfo}");
        if (save.Version is not (GameVersion.SN or GameVersion.MN)) throw new InvalidDataException("サン・ムーンの作品情報を確認できません。");
        if (save.PartyCount is < 0 or > 6) throw new InvalidDataException("手持ちの数が不正です。");
        return save;
    }
    internal static string Title(SAV7SM save) => save.Version == GameVersion.SN ? "サン" : "ムーン";
    internal static TransferTrainerProfile Profile(SAV7SM save) => new(save.OT, save.Gender, save.Country, save.Region, save.ConsoleRegion);
    public SaveSnapshot Read(byte[] data)
    {
        var save = Open(data);
        var party = Enumerable.Range(0, save.PartyCount).Select(i => PokemonCodec.Describe(save.GetPartySlotAtIndex(i), i)).ToArray();
        var boxes = Enumerable.Range(0, save.BoxCount).Select(b => new BoxSnapshot(save.GetBoxName(b),
            Enumerable.Range(0, save.BoxSlotCount).Select(s => PokemonCodec.Describe(save.GetBoxSlotAtIndex(b, s), s)).ToArray())).ToArray();
        return new(save.OT, Title(save), party, boxes)
        { FormatKey = "sm", UnlockedBoxCount = Math.Clamp(save.BoxesUnlocked, 0, save.BoxCount), TransferTrainer = Profile(save) };
    }
}
