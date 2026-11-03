namespace PokemonMove.Domain;

public sealed record PokemonSummary(int Slot, ushort Species, string Name, string Nickname,
    int Level, string Type, IReadOnlyList<string> Moves, bool IsShiny)
{
    public bool IsEgg { get; init; }
    public byte[] StoredData { get; init; } = [];
    public bool CanStore => Species != 0 && StoredData.Length != 0;
    public string Label => Species == 0 ? "空き" : $"{Name}\nLv.{Level}{(IsShiny ? " ★" : "")}";
}

public sealed record BoxSnapshot(string Name, IReadOnlyList<PokemonSummary> Slots);
public sealed record SaveSnapshot(string TrainerName, string Game,
    IReadOnlyList<PokemonSummary> Party, IReadOnlyList<BoxSnapshot> Boxes)
{
    public string FormatKey { get; init; } = "hgss";
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ISaveReader
{
    SaveSnapshot Read(byte[] data);
}
