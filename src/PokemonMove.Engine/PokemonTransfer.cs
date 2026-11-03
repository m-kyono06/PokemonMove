using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Deterministic, explicitly dated one-way PK4→PK5 conversion. Also validates generation boundaries.</summary>
public sealed class PokemonTransfer
{
    public PokemonTransferPlan Prepare(PokemonSummary pokemon, string targetFormat, DateOnly? transferDate = null)
    {
        var source = new PokemonCodec().Decode(pokemon.StoredData, pokemon.FormatKey);
        var target5 = targetFormat is "bw" or "b2w2";
        if (!target5 && targetFormat is not ("dp" or "pt" or "hgss"))
            throw new InvalidDataException("この移動先の作品には対応していません。");
        if (source.FormatKey == "pk5" && !target5)
            throw new InvalidDataException("第5世代の個体を第4世代へ戻すことはできません。個体はバンクに残っています。");
        if (source.FormatKey == "pk5" || !target5) return new(source, false, []);
        if (transferDate is not { Year: >= 2000 and <= 2099 } date)
            throw new InvalidDataException("世代移動の日付を確認できません。移動先を選び直してください。");
        var pk4 = new PK4(source.StoredData.ToArray());
        if (pk4.IsEgg) throw new InvalidDataException("タマゴは第5世代へ移動できません。ゲーム内で孵化させてください。");
        if (pk4.Species == (ushort)Species.Pichu && pk4.Form != 0)
            throw new InvalidDataException("ギザみみピチューは第5世代へ移動できません。");
        if (pk4.HeldItem != 0)
            throw new InvalidDataException("第5世代へ移動する前に、第4世代のゲーム内で持ち物を外してください。個体はバンクに残っています。");
        if (pk4.MetLocationDP > 4000)
            throw new InvalidDataException("第4世代の出会った場所の情報を確認できません。この個体は世代変換できません。");
        var pk5 = pk4.ConvertToPK5();
        if (pk5.Move1 != pk4.Move1 || pk5.Move2 != pk4.Move2 || pk5.Move3 != pk4.Move3 || pk5.Move4 != pk4.Move4)
            throw new InvalidDataException("世代移動で削除されるひでん技を覚えています。第4世代のゲーム内で忘れさせてから移動してください。技は自動で削除しません。");
        if (pk5.Form != pk4.Form)
            throw new InvalidDataException("このフォルムは世代移動で変化します。第4世代のゲーム内で通常フォルムに戻してから移動してください。");
        pk5.MetDate = date;
        var data = new byte[pk5.SIZE_STORED]; pk5.WriteEncryptedDataStored(data);
        var converted = new Gen5PokemonCodec().Decode(data);
        var notices = new List<string>
        {
            "第5世代へ変換します。第4世代へは戻せません。",
            $"出会った場所・レベル・日付を移動情報に変更します（日付：{date:yyyy/MM/dd}）。",
            "なつき度は70、技のPPは回復した状態になります。名前の文字コードも第5世代へ変換します。",
        };
        if (pk4.Gender != pk5.Gender) notices.Add("性別情報を第5世代に合わせて補正します。");
        return new(converted, true, notices);
    }
}
