using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>A deterministic, explicitly non-official reconstruction for older game formats.</summary>
internal static class BackwardPokemonTransfer
{
    internal static int Generation(string format) => format switch
    {
        "dp" or "pt" or "hgss" => 4, "bw" or "b2w2" => 5,
        "xy" or "oras" => 6, "sm" or "usum" => 7, _ => 0,
    };

    internal static PokemonTransferPlan Prepare(PokemonSummary source, string format, DateOnly? transferDate)
    {
        if (transferDate is not { Year: >= 2000 and <= 2099 } date)
            throw new InvalidDataException("逆方向の変換日を確認できません。移動先を選び直してください。");
        PKM original = source.FormatKey switch
        {
            "pk5" => new PK5(source.StoredData.ToArray()),
            "pk6" => new PK6(source.StoredData.ToArray()),
            "pk7" => new PK7(source.StoredData.ToArray()),
            _ => throw new InvalidDataException("この逆方向の変換には対応していません。"),
        };
        SaveFile target = format switch
        {
            "dp" => new SAV4DP(), "pt" => new SAV4Pt(), "hgss" => new SAV4HGSS(),
            "bw" => new SAV5BW(), "b2w2" => new SAV5B2W2(),
            "xy" => new SAV6XY(), "oras" => new SAV6AO(),
            _ => throw new InvalidDataException("この逆方向の移動先には対応していません。"),
        };
        var generation = Generation(format);
        if (original.Species > target.MaxSpeciesID || !target.Personal.IsPresentInGame(original.Species, original.Form))
            throw new InvalidDataException("このポケモン・フォルムは移動先の作品に存在しません。個体はバンクに残っています。");
        // Gen7 Pikachu forms are caps; the same numeric slots in ORAS are cosplay forms.
        if (original.Format == 7 && original.Species == (ushort)Species.Pikachu && original.Form != 0)
            throw new InvalidDataException("サトシの帽子のピカチュウは以前の世代の同じ番号のフォルムとは別の姿です。通常フォルムへ自動変換しません。");
        if (original.IsEgg) throw new InvalidDataException("タマゴは逆方向に変換できません。先にゲーム内で孵化させてください。");
        if (original.HeldItem != 0) throw new InvalidDataException("世代を戻す前に元のゲーム内で持ち物を外してください。個体はバンクに残っています。");
        if (FormInfo.IsBattleOnlyForm(original.Species, original.Form, original.Format)
            || (original.Species == (ushort)Species.Kyurem && original.Form != 0)
            || (original.Species == (ushort)Species.Necrozma && original.Form != 0)
            || (original.Species == (ushort)Species.Furfrou && original.Form != 0))
            throw new InvalidDataException("このフォルムは元のゲーム内で通常の姿へ戻してください。");
        if (new[] { original.Move1, original.Move2, original.Move3, original.Move4,
                original.RelearnMove1, original.RelearnMove2, original.RelearnMove3, original.RelearnMove4 }.Any(m => m > target.MaxMoveID))
            throw new InvalidDataException("移動先に存在しない技・思い出せる技があります。技は自動で削除しません。個体はバンクに残っています。");
        if ((int)original.Nature >= 25 || original.Gender > 2 || original.OriginalTrainerGender > 1 || original.CurrentLevel is < 1 or > 100)
            throw new InvalidDataException("性格・性別・レベルの情報を確認できません。");
        var personal = target.Personal.GetFormEntry(original.Species, original.Form);
        var ability = personal.GetIndexOfAbility(original.Ability);
        if (original.Ability == 0 || original.Ability > target.MaxAbilityID || ability < 0
            || (generation == 4 && (ability > 1 || original.AbilityNumber == 4)))
            throw new InvalidDataException("この特性は移動先のポケモンに対応していません。隠れ特性を第4世代へ持ち込むこともできません。個体はバンクに残っています。");
        if (original.Ball > target.MaxBallID)
            throw new InvalidDataException("このボールは移動先に存在しません。ボールは自動で変更しません。");
        if (original.Language is not (1 or 2 or 3 or 4 or 5 or 7 or 8))
            throw new InvalidDataException("この言語は移動先の世代に対応していません。個体はバンクに残っています。");

        PKM result = generation switch { 4 => new PK4(), 5 => new PK5(), _ => new PK6() };
        result.Species = original.Species; result.Form = original.Form;
        result.Version = format switch
        {
            "dp" => GameVersion.D, "pt" => GameVersion.Pt, "hgss" => GameVersion.HG,
            "bw" => GameVersion.B, "b2w2" => GameVersion.B2, "xy" => GameVersion.X, _ => GameVersion.OR,
        };
        result.Language = original.Language; result.ID32 = original.ID32;
        result.OriginalTrainerGender = original.OriginalTrainerGender;
        result.OriginalTrainerName = original.OriginalTrainerName; result.Nickname = original.Nickname;
        result.IsNicknamed = original.IsNicknamed;
        if (result.OriginalTrainerName != original.OriginalTrainerName || result.Nickname != original.Nickname)
            throw new InvalidDataException("親名・ニックネームを移動先の文字数・文字コードで保持できません。元の作品で名前を確認してください。");
        result.Gender = original.Gender;
        if (generation != 4) result.Nature = original.Nature;
        result.PID = FindPid(original, personal.Gender, ability, generation);
        result.Ability = original.Ability;
        if (result is PK5 fifth) fifth.HiddenAbility = ability == 2;
        if (result is PK6 sixth)
        {
            sixth.AbilityNumber = 1 << ability;
            sixth.EncryptionConstant = original.EncryptionConstant;
            sixth.RelearnMove1 = original.RelearnMove1; sixth.RelearnMove2 = original.RelearnMove2;
            sixth.RelearnMove3 = original.RelearnMove3; sixth.RelearnMove4 = original.RelearnMove4;
        }
        result.IV_HP = original.IV_HP; result.IV_ATK = original.IV_ATK; result.IV_DEF = original.IV_DEF;
        result.IV_SPA = original.IV_SPA; result.IV_SPD = original.IV_SPD; result.IV_SPE = original.IV_SPE;
        result.EV_HP = original.EV_HP; result.EV_ATK = original.EV_ATK; result.EV_DEF = original.EV_DEF;
        result.EV_SPA = original.EV_SPA; result.EV_SPD = original.EV_SPD; result.EV_SPE = original.EV_SPE;
        result.CurrentLevel = original.CurrentLevel;
        result.OriginalTrainerFriendship = original.OriginalTrainerFriendship;
        result.PokerusDays = original.PokerusDays; result.PokerusStrain = original.PokerusStrain;
        result.Ball = original.Ball;
        if (result.Ball != original.Ball)
            throw new InvalidDataException("このボールを移動先の作品でそのまま保持できません。ボールは自動で変更しません。");
        result.Move1 = original.Move1; result.Move2 = original.Move2; result.Move3 = original.Move3; result.Move4 = original.Move4;
        result.Move1_PPUps = original.Move1_PPUps; result.Move2_PPUps = original.Move2_PPUps;
        result.Move3_PPUps = original.Move3_PPUps; result.Move4_PPUps = original.Move4_PPUps;
        result.HealPP();
        result.MetDate = date; result.MetLevel = original.CurrentLevel; result.MetLocation = 0;
        if (result.Nature != original.Nature || result.IsShiny != original.IsShiny || !result.IsGenderValid())
            throw new InvalidDataException("性格・性別・色違いを保った変換ができません。個体はバンクに残っています。");
        var data = new byte[result.SIZE_STORED]; result.WriteEncryptedDataStored(data);
        var converted = new PokemonCodec().Decode(data, $"pk{generation}");
        var notices = new List<string>
        {
            $"第{source.Generation}→第{generation}世代へ非公式の逆方向変換を行います。公式の正規個体としての整合性は保証できません。",
            $"出身作品を{OriginTitle(format)}相当へ変更し、出会った場所は未設定、レベルは現在のレベル、日付は{date:yyyy/MM/dd}に設定します。元の出会った記録には復元できません。",
            "種類・姿・親ID・親名・性別・性格・色違い・個体値・努力値・技を保持し、特性の枠は同じ特性になる移動先の枠へ合わせます。経験値は同じレベルの開始値になり、能力値は移動先の仕様で再計算、技のPPは回復します。",
            "リボン・配布フラグ・コンテスト・マーキング・思い出・なかよし度・すごいとっくん・元の孵化情報などの追加記録は引き継ぎません。すごいとっくんの効果も失われ、元の個体値で計算します。",
        };
        if (generation < 6) notices.Add("思い出せる技の専用記録・暗号化定数・後の世代のトレーナー情報は引き継ぎません。親ID表示も以前の世代の方式になります。");
        if (result.PID != original.PID) notices.Add("性格・性別・特性枠・色違いを保つためPIDを変更します。PIDによる模様や進化分岐が変わる場合があります。");
        return new(converted, true, notices);
    }

    private static uint FindPid(PKM source, byte genderRatio, int ability, int generation)
    {
        bool Matches(uint pid)
        {
            var xor = (ushort)pid ^ (ushort)(pid >> 16) ^ source.TID16 ^ source.SID16;
            if ((xor < (generation < 6 ? 8 : 16)) != source.IsShiny) return false;
            if (generation == 6) return true;
            if (EntityGender.GetFromPIDAndRatio(pid, genderRatio) != source.Gender) return false;
            if (generation == 4 && pid % 25 != (uint)source.Nature) return false;
            return ability == 2 || ((generation == 5 ? pid >> 16 : pid) & 1) == (uint)ability;
        }
        if (Matches(source.PID)) return source.PID;
        if (source.IsShiny)
        {
            for (uint low = 0; low <= ushort.MaxValue; low++)
                for (uint xor = 0; xor < 8; xor++)
                {
                    var high = low ^ source.TID16 ^ source.SID16 ^ xor;
                    var pid = high << 16 | low;
                    if (Matches(pid)) return pid;
                }
        }
        else
        {
            var candidate = source.PID;
            for (var i = 0; i < 1_048_576; i++)
            {
                candidate = unchecked(candidate * 1664525 + 1013904223);
                if (Matches(candidate)) return candidate;
            }
        }
        throw new InvalidDataException("性格・性別・色違い・特性枠を同時に保ったPIDを作れません。この個体は逆方向へ移動できません。");
    }
    private static string OriginTitle(string format) => format switch
    {
        "dp" => "ダイヤモンド", "pt" => "プラチナ", "hgss" => "ハートゴールド",
        "bw" => "ブラック", "b2w2" => "ブラック2", "xy" => "X", _ => "オメガルビー",
    };
}
