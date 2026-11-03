using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class DsSaveEditor : ISaveEditor
{
    private static ISaveEditor For(byte[] data) => DsSaveReader.Detect(data) switch
    {
        SAV4DP or SAV4Pt or SAV4HGSS => new Gen4SaveEditor(),
        SAV5BW or SAV5B2W2 => new Gen5SaveEditor(),
        _ => throw new InvalidDataException("この作品の移動には対応していません。"),
    };
    public SaveEdit Remove(byte[] source, PokemonLocation location) => For(source).Remove(source, location);
    public SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon, string formatKey = "pk4", DateOnly? transferDate = null) =>
        For(source).Insert(source, location, pokemon, formatKey, transferDate);
}
