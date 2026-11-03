namespace PokemonMove.Domain;

public sealed record PokemonBattleStats(int HP, int Attack, int Defense, int SpecialAttack, int SpecialDefense, int Speed);
public sealed record PokemonDetails(string Gender, string Nature, string Ability, string HeldItem,
    PokemonBattleStats Stats, int? CurrentHP, PokemonBattleStats EffortValues,
    PokemonBattleStats IndividualValues, ushort OriginalTrainerId)
{
    public string OriginalTrainerIdText { get; init; } = OriginalTrainerId.ToString("D5");
    public int HyperTrainingCount { get; init; }
    public int PerfectIVCount => new[] { IndividualValues.HP, IndividualValues.Attack, IndividualValues.Defense,
        IndividualValues.SpecialAttack, IndividualValues.SpecialDefense, IndividualValues.Speed }.Count(value => value == 31);
    public int TotalEffortValues => EffortValues.HP + EffortValues.Attack + EffortValues.Defense
        + EffortValues.SpecialAttack + EffortValues.SpecialDefense + EffortValues.Speed;
}
public sealed record InspectionSnapshot(PokemonSummary? Pokemon, DexEntry? Species, DexVariant Variant, string Context);
public interface IPokemonInspection
{
    InspectionSnapshot Current { get; }
    event EventHandler? Changed;
    void SelectPokemon(PokemonSummary pokemon, string context);
    void SelectSpecies(DexEntry species, DexVariant variant);
    void Clear();
}
public sealed class PokemonInspection : IPokemonInspection
{
    private readonly object gate = new();
    private InspectionSnapshot current = new(null, null, DexVariant.All, "下画面でポケモンを選択してください。");
    public InspectionSnapshot Current { get { lock (gate) return current; } }
    public event EventHandler? Changed;
    public void SelectPokemon(PokemonSummary pokemon, string context)
    {
        if (pokemon.Species == 0) { Clear(); return; }
        Set(new(pokemon with { StoredData = [] }, null, DexVariant.All, context));
    }
    public void SelectSpecies(DexEntry species, DexVariant variant) => Set(new(null, species, variant, "収集図鑑"));
    public void Clear() => Set(new(null, null, DexVariant.All, "下画面でポケモンを選択してください。"));
    private void Set(InspectionSnapshot next)
    { lock (gate) current = next; Changed?.Invoke(this, EventArgs.Empty); }
}
public sealed record ScreenPanel(int Id, string Name, int Width, int Height);
public sealed record ScreenRoute(int Controls, int Inspector);
public static class DualScreenRouting
{
    public static ScreenRoute? Default(IReadOnlyList<ScreenPanel> screens)
    {
        var ids = screens.Select(s => s.Id).Distinct().Order().ToArray();
        if (ids.Length < 2) return null;
        var upper = ids.Contains(0) ? 0 : ids[0];
        return new(ids.First(i => i != upper), upper);
    }
    // Choose a default only before a route is saved. Never silently replace a saved assignment.
    public static ScreenRoute? Resolve(ScreenRoute? saved, IReadOnlyList<ScreenPanel> screens) =>
        saved is null ? Default(screens) : IsValid(saved, screens) ? saved : null;
    public static bool IsValid(ScreenRoute route, IReadOnlyList<ScreenPanel> screens) =>
        route.Controls != route.Inspector && screens.Any(s => s.Id == route.Controls) && screens.Any(s => s.Id == route.Inspector);
}
public interface IDualScreenManager
{
    IReadOnlyList<ScreenPanel> Screens { get; }
    ScreenRoute? ConfiguredRoute { get; }
    bool Enabled { get; }
    bool IsActive { get; }
    string Status { get; }
    Task EnableAsync(int controlsDisplay, int inspectorDisplay);
    void Stop();
}

// Screen changes must not tear down windows while a picker, confirmation or transfer is active.
public sealed class UserOperations
{
    private int count;
    public bool IsBusy => Volatile.Read(ref count) != 0;
    public event EventHandler? BecameIdle;
    public IDisposable Begin() { Interlocked.Increment(ref count); return new Lease(this); }
    private sealed class Lease(UserOperations owner) : IDisposable
    {
        private UserOperations? current = owner;
        public void Dispose()
        {
            var parent = Interlocked.Exchange(ref current, null);
            if (parent is not null && Interlocked.Decrement(ref parent.count) == 0) parent.BecameIdle?.Invoke(parent, EventArgs.Empty);
        }
    }
}
