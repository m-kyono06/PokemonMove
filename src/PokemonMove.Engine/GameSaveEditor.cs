using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class GameSaveEditor : ISaveEditor, ISaveShinyEditor
{
    private static ISaveEditor For(byte[] data) => GameSaveReader.Detect(data) switch
    {
        SAV4DP or SAV4Pt or SAV4HGSS => new Gen4SaveEditor(),
        SAV5BW or SAV5B2W2 => new Gen5SaveEditor(),
        SAV6XY or SAV6AO => new Gen6SaveEditor(),
        SAV7SM or SAV7USUM => new Gen7SaveEditor(),
        _ => throw new InvalidDataException("この作品の移動には対応していません。"),
    };
    public SaveEdit MakeShiny(byte[] source, PokemonLocation location) => GameSaveReader.Detect(source) switch
    {
        SAV4DP or SAV4Pt or SAV4HGSS => new Gen4SaveEditor().MakeShiny(source, location),
        SAV5BW or SAV5B2W2 => new Gen5SaveEditor().MakeShiny(source, location),
        SAV6XY or SAV6AO => new Gen6SaveEditor().MakeShiny(source, location),
        SAV7SM or SAV7USUM => new Gen7SaveEditor().MakeShiny(source, location),
        _ => throw new InvalidDataException("この作品の色違い化には対応していません。"),
    };
    public SaveEdit Remove(byte[] source, PokemonLocation location) => For(source).Remove(source, location);
    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon, string formatKey = "pk4", DateOnly? transferDate = null) =>
        For(source).Insert(source, location, pokemon, formatKey, transferDate);
}
