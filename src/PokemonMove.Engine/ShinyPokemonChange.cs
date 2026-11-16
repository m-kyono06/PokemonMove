using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Only changes the PID of an existing validated Pokemon, never generates a new entity.</summary>
public static class ShinyPokemonChange
{
    public static ShinyPokemonPlan Prepare(PokemonSummary pokemon)
    {
        var original = new PokemonCodec().Decode(pokemon.StoredData.ToArray(), pokemon.FormatKey);
        PKM pk = original.FormatKey switch
        {
            "pk4" => new PK4(original.StoredData.ToArray()), "pk5" => new PK5(original.StoredData.ToArray()),
            "pk6" => new PK6(original.StoredData.ToArray()), _ => new PK7(original.StoredData.ToArray()),
        };
        var changed = Apply(pk);
        var data = new byte[changed.SIZE_STORED]; changed.WriteEncryptedDataStored(data);
        var notices = new List<string>
        {
            "選択中のセーブの、この個体だけを色違いに変更します。新規作成・複製・バンクへの追加は行いません。",
            "性格・性別・特性・親ID・個体値・努力値・技・PP・持ち物・出会った記録を保ち、色違い判定に必要なPIDを変更します。",
            "変更前のセーブをバックアップし、書き込み後に照合します。エミュレータを終了してから実行してください。",
            "セーブ編集による色違い化です。正規入手の個体としての整合性は保証できません。",
        };
        if (pk.Species == (ushort)Species.Spinda && pk.Format <= 5)
            notices.Add("第4・第5世代のパッチールは、PIDの変更により模様も変わります。");
        return new(new PokemonCodec().Decode(data, original.FormatKey), notices);
    }
    internal static PKM Apply(PKM original)
    {
        if (original.Species == 0 || !original.ChecksumValid || original.IsEgg)
            throw new InvalidDataException("空き枠・タマゴ・検証に失敗した個体は色違いに変更できません。");
        if (original.IsShiny) throw new InvalidDataException("この個体はすでに色違いです。");
        var result = original.Clone();
        var count = original.Format < 6 ? 8u : 16u;
        var firstLow = (ushort)original.PID;
        bool Matches(uint pid)
        {
            if ((original.Format == 4 || original.Generation is 3 or 4) && pid % 25 != (uint)original.Nature) return false;
            if (original.Format <= 5 && pid % 6 != original.PID % 6) return false;
            if ((original.Format <= 5 || original.Generation <= 5)
                && EntityGender.GetFromPIDAndRatio(pid, original.PersonalInfo.Gender) != original.Gender) return false;
            if (original.Format <= 5)
            {
                var ability = (int)(((original.Format == 5 && original.Gen5) ? pid >> 16 : pid) & 1);
                if (ability != original.PIDAbility) return false;
                if (original.Species is >= 265 and <= 269
                    && WurmpleUtil.GetWurmpleEvoVal(pid) != WurmpleUtil.GetWurmpleEvoVal(original.PID)) return false;
            }
            return true;
        }
        for (uint offset = 0; offset <= ushort.MaxValue; offset++)
        {
            var low = (uint)(ushort)(firstLow + offset);
            for (uint xor = 0; xor < count; xor++)
            {
                var high = low ^ original.TID16 ^ original.SID16 ^ xor;
                var pid = high << 16 | low;
                if (!Matches(pid)) continue;
                result.PID = pid;
                if (!result.IsShiny || result.Nature != original.Nature || result.Gender != original.Gender
                    || result.AbilityNumber != original.AbilityNumber || result.Characteristic != original.Characteristic)
                    throw new InvalidDataException("既存の能力・性格・性別を維持した色違い化を確認できません。");
                return result;
            }
        }
        throw new InvalidDataException("性格・性別・特性枠などを維持した色違い化ができません。この個体は変更しません。");
    }
}
