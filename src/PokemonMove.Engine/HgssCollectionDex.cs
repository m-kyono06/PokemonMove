using System.Text;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Current ownership, not the game's seen/caught flags. All HGSS national species (1..493).</summary>
public sealed class HgssCollectionDex : ICollectionDex
{
    public CollectionDexSnapshot Build(SaveSnapshot? save, IReadOnlyList<BankItem> bank)
    {
        var counts = new int[494, 6];
        if (save is not null)
        {
            Add(save.Party, 0);
            Add(save.Boxes.SelectMany(b => b.Slots), 1);
        }
        // Legacy copies are still stored collection data; they do not grant withdrawal eligibility here.
        Add(bank.Select(i => i.Pokemon), 2);
        var names = GameInfo.GetStrings("ja").specieslist;
        return new(Enumerable.Range(1, 493).Select(i => new DexEntry((ushort)i, names[i],
            counts[i, 0], counts[i, 1], counts[i, 2], counts[i, 3], counts[i, 4], counts[i, 5])).ToArray(), save is not null);

        void Add(IEnumerable<PokemonSummary> pokemon, int source)
        {
            foreach (var p in pokemon)
            {
                if (p.Species is 0 or > 493 || p.IsEgg || !p.CanStore) continue;
                counts[p.Species, source]++;
                if (p.IsShiny) counts[p.Species, source + 3]++;
            }
        }
    }

    public IReadOnlyList<DexEntry> Filter(CollectionDexSnapshot dex, DexVariant variant, DexOwnership ownership, string query)
    {
        query = Normalize(query.Trim());
        var hasNumber = int.TryParse(query, out var number);
        return dex.Entries.Where(e => ownership switch
        {
            DexOwnership.Owned => e.Count(variant) > 0,
            DexOwnership.Missing => e.Count(variant) == 0,
            _ => true
        }).Where(e => query.Length == 0 || (hasNumber ? e.Species == number : Normalize(e.Name).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Select(c => c is >= '\u3041' and <= '\u3096' ? (char)(c + 0x60) : c).ToArray());
}
