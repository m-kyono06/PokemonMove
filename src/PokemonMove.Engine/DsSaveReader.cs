using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class DsSaveReader : PokemonMove.Domain.ISaveReader
{
    public const int MaximumFileSize = Gen4SaveReader.MaximumFileSize;
    internal static SaveFile Detect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < Gen4SaveReader.RawSize || data.Length > MaximumFileSize)
            throw new InvalidDataException("D・P・Pt・HGSS・BW・BW2の通常セーブ（512 KiB）またはDSVを選択してください。ステートセーブには対応していません。");
        return SaveUtil.GetSaveFile(data.ToArray()) ?? throw new InvalidDataException("対応作品の通常セーブとして読み取れません。ゲーム内で保存したファイルを選択してください。");
    }
    public SaveSnapshot Read(byte[] data) => Detect(data) switch
    {
        SAV4DP or SAV4Pt or SAV4HGSS => new Gen4SaveReader().Read(data),
        SAV5BW or SAV5B2W2 => new Gen5SaveReader().Read(data),
        _ => throw new InvalidDataException("この作品のセーブにはまだ対応していません。"),
    };
}
