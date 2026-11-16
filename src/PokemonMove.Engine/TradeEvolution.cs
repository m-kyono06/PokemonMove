using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Local simulation of trade evolution using the current storage generation's evolution rules.</summary>
public sealed class TradeEvolution : ITradeEvolution
{
    private const int Everstone = 229;
    public TradeEvolutionPlan Prepare(PokemonSummary pokemon, PokemonSummary? partner = null)
    {
        var first = Read(pokemon);
        var pair = first.Species is 588 or 616;
        if (pair)
        {
            if (partner is null || partner.FormatKey != pokemon.FormatKey)
                throw new InvalidDataException("カブルモとチョボマキは、同じ保存世代の相手をバンクから選んでください。");
            var second = Read(partner);
            if ((first.Species == 588 && second.Species != 616) || (first.Species == 616 && second.Species != 588))
                throw new InvalidDataException("カブルモの相手はチョボマキ、チョボマキの相手はカブルモを選んでください。");
            if (first.HeldItem == Everstone || second.HeldItem == Everstone)
                throw new InvalidDataException("どちらかがかわらずのいしを持っています。元のゲームで外してから保管し直してください。");
            var a = Evolve(first, second.Species); var b = Evolve(second, first.Species);
            return new(a, b, Notices(first, a, second, b));
        }
        if (partner is not null) throw new InvalidDataException("この通信進化には相手の個体を選びません。");
        var evolved = Evolve(first);
        return new(evolved, null, Notices(first, evolved));
    }
    private static PKM Read(PokemonSummary source)
    {
        var summary = new PokemonCodec().Decode(source.StoredData.ToArray(), source.FormatKey);
        PKM pk = source.FormatKey switch
        {
            "pk4" => new PK4(summary.StoredData.ToArray()), "pk5" => new PK5(summary.StoredData.ToArray()),
            "pk6" => new PK6(summary.StoredData.ToArray()), "pk7" => new PK7(summary.StoredData.ToArray()),
            _ => throw new InvalidDataException("この保存世代の通信進化には対応していません。"),
        };
        if (pk.IsEgg) throw new InvalidDataException("タマゴは通信進化できません。");
        IPersonalTable table = pk.Format switch { 4 => PersonalTable.HGSS, 5 => PersonalTable.B2W2, 6 => PersonalTable.AO, _ => PersonalTable.USUM };
        if (!table.IsPresentInGame(pk.Species, pk.Form) || pk.Species == 0)
            throw new InvalidDataException("このポケモンの情報を確認できません。");
        return pk;
    }
    private static PokemonSummary Evolve(PKM pk, ushort? partner = null)
    {
        if (pk.HeldItem == Everstone && pk.Species != 64)
            throw new InvalidDataException("かわらずのいしを持っています。元のゲームで外してから保管し直してください。");
        var methods = EvolutionTree.GetEvolutionTree(pk.Context).Forward.GetForward(pk.Species, pk.Form).ToArray();
        var trades = methods.Where(m => m.Method is EvolutionType.Trade or EvolutionType.TradeHeldItem or EvolutionType.TradeShelmetKarrablast).ToArray();
        if (trades.Length == 0) throw new InvalidDataException("このポケモンは、この保存世代では通信進化しません。");
        var matches = trades.Where(m => m.Method switch
        {
            EvolutionType.Trade => partner is null,
            EvolutionType.TradeHeldItem => partner is null && m.Argument == pk.HeldItem,
            EvolutionType.TradeShelmetKarrablast => partner is not null && (m.Argument == 0 || m.Argument == partner),
            _ => false,
        }).ToArray();
        if (matches.Length != 1)
        {
            var required = trades.Where(m => m.Method == EvolutionType.TradeHeldItem)
                .Select(m => GameInfo.GetStrings("ja").itemlist[m.Argument]).Distinct();
            throw new InvalidDataException("通信進化の条件に合いません。必要な持ち物：" + string.Join("／", required)
                + "。元のゲームで持たせてから保管し直してください。");
        }
        var method = matches[0]; var result = pk.Clone();
        var level = pk.CurrentLevel;
        var slot = pk.AbilityNumber switch { 1 => 0, 2 => 1, 4 when pk.Format >= 5 => 2, _ => -1 };
        if (slot < 0 || pk.PersonalInfo.GetAbilityAtIndex(slot) != pk.Ability)
            throw new InvalidDataException("特性の枠を確認できないため、通信進化できません。");
        result.Species = method.Species; result.Form = method.GetDestinationForm(pk.Form);
        if (result.Species > (pk.Format switch { 4 => 493, 5 => 649, 6 => 721, _ => 807 })
            || (pk.Format == 7 && pk.Version is GameVersion.SN or GameVersion.MN && result.Species > 802))
            throw new InvalidDataException("進化先が現在の保存世代に存在しません。");
        if (method.Method == EvolutionType.TradeHeldItem) result.HeldItem = 0;
        result.RefreshAbility(slot);
        if (result.CurrentLevel != level) throw new InvalidDataException("経験値とレベルを維持した進化を確認できません。");
        if (!pk.IsNicknamed)
        {
            result.Nickname = SpeciesName.GetSpeciesNameGeneration(result.Species, result.Language, result.Format);
            result.IsNicknamed = false;
        }
        var data = new byte[result.SIZE_STORED]; result.WriteEncryptedDataStored(data);
        return new PokemonCodec().Decode(data, $"pk{pk.Format}");
    }
    private static IReadOnlyList<string> Notices(PKM first, PokemonSummary result, PKM? second = null, PokemonSummary? other = null)
    {
        var notices = new List<string>
        {
            $"{SpeciesName.GetSpeciesNameGeneration(first.Species, 1, first.Format)} → {result.Name}",
            "バンク内で通信進化を再現します。進化前の個体を置き換え、進化前には戻せません。セーブへは引き出すまで書き込みません。",
            "レベル・経験値・性格・性別・親ID・個体値・努力値・色違い・技とPPを保持し、能力値と特性は進化後の同じ特性枠で再計算します。ニックネームがない場合は進化後の種名になります。",
            "進化時に覚える技は自動習得しません。必要に応じてゲーム内の技思い出しを利用してください。",
        };
        if (first.HeldItem != 0)
        {
            // Compare actual item IDs, not translated names.
            PKM evolved = first.Format switch { 4 => new PK4(result.StoredData.ToArray()), 5 => new PK5(result.StoredData.ToArray()), 6 => new PK6(result.StoredData.ToArray()), _ => new PK7(result.StoredData.ToArray()) };
            if (evolved.HeldItem == 0) notices.Add("通信進化に必要な持ち物を消費します。");
        }
        if (second is not null && other is not null)
            notices.Add($"相手の{SpeciesName.GetSpeciesNameGeneration(second.Species, 1, second.Format)}も{other.Name}へ進化し、2匹を同時に置き換えます。");
        return notices;
    }
}
