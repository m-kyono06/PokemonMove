namespace PokemonMove.Domain;

public sealed record TradeEvolutionPlan(PokemonSummary Pokemon, PokemonSummary? Partner, IReadOnlyList<string> Notices);
public interface ITradeEvolution
{
    TradeEvolutionPlan Prepare(PokemonSummary pokemon, PokemonSummary? partner = null);
}
