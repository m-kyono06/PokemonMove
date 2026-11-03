using System.Text;
using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Current ownership, not the game's seen/caught flags. All HGSS national species (1..493).</summary>
public sealed class HgssCollectionDex : ICollectionDex
{
    public CollectionDexSnapshot Build(SaveSnapshot? save, IReadOnlyList<BankItem> bank)
        => BuildAll(save is null ? [] : [save], bank);

    public CollectionDexSnapshot BuildAll(IReadOnlyList<SaveSnapshot> saves, IReadOnlyList<BankItem> bank)
    {
        var counts = new int[494, 6];
        var locations = Enumerable.Range(0, 494).Select(_ => new List<DexSaveLocation>()).ToArray();
        for (var index = 0; index < saves.Count; index++)
        {
            var save = saves[index];
            var local = new int[494, 4];
            Add(save.Party, 0, local);
            Add(save.Boxes.SelectMany(b => b.Slots), 1, local);
            for (var i = 1; i <= 493; i++)
                if (local[i, 0] + local[i, 1] != 0)
                    locations[i].Add(new(index + 1, save.Game, save.TrainerName, local[i, 0], local[i, 1], local[i, 2], local[i, 3]));
        }
        Add(bank.Select(i => i.Pokemon), 2, null);
        var names = GameInfo.GetStrings("ja").specieslist;
        return new(Enumerable.Range(1, 493).Select(i => new DexEntry((ushort)i, names[i],
            counts[i, 0], counts[i, 1], counts[i, 2], counts[i, 3], counts[i, 4], counts[i, 5])
            { Saves = locations[i].ToArray() }).ToArray(), saves.Count != 0) { SaveCount = saves.Count };

        void Add(IEnumerable<PokemonSummary> pokemon, int source, int[,]? local)
        {
            foreach (var p in pokemon)
            {
                if (p.Species is 0 or > 493 || p.IsEgg || !p.CanStore) continue;
                counts[p.Species, source]++;
                if (p.IsShiny) counts[p.Species, source + 3]++;
                if (local is null) continue;
                local[p.Species, source]++;
                if (p.IsShiny) local[p.Species, source + 2]++;
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
