namespace PokemonMove.Domain;

public sealed record PokemonSummary(int Slot, ushort Species, string Name, string Nickname,
    int Level, string Type, IReadOnlyList<string> Moves, bool IsShiny)
{
    public string FormatKey { get; init; } = "pk4";
    public int Generation => FormatKey switch { "pk5" => 5, "pk6" => 6, "pk7" => 7, _ => 4 };
    public int Form { get; init; }
    public PokemonDetails? Details { get; init; }
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
    public TransferTrainerProfile? TransferTrainer { get; init; }
    public int? UnlockedBoxCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ISaveReader
{
    SaveSnapshot Read(byte[] data);
}

public sealed record TransferTrainerProfile(string Name, byte Gender, byte Country, byte Region, byte ConsoleRegion);
