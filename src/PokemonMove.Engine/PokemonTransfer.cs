using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>Deterministic one-way PK4→PK5 and PK5→PK6 conversions. Never downgrade.</summary>
public sealed class PokemonTransfer
{
    public PokemonTransferPlan Prepare(PokemonSummary pokemon, string targetFormat, DateOnly? transferDate = null, TransferTrainerProfile? trainer = null)
    {
        var source = new PokemonCodec().Decode(pokemon.StoredData, pokemon.FormatKey);
        if (targetFormat is "sm" or "usum") return PrepareGen7(source, trainer);
        if (source.FormatKey == "pk7") throw new InvalidDataException("第7世代の個体は以前の作品へ戻せません。個体はバンクに残っています。");
        if (targetFormat is "xy" or "oras") return PrepareGen6(source, trainer);
        if (source.FormatKey == "pk6") throw new InvalidDataException("第6世代の個体をDS作品へ戻すことはできません。個体はバンクに残っています。");
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
    private static PokemonTransferPlan PrepareGen6(PokemonSummary source, TransferTrainerProfile? trainer)
    {
        if (source.FormatKey == "pk6") return new(source, false, []);
        if (source.FormatKey != "pk5")
            throw new InvalidDataException("第4世代からXY・ORASへ直接は移動できません。先にBW・BW2へ移動してください。");
        if (trainer is null || string.IsNullOrWhiteSpace(trainer.Name))
            throw new InvalidDataException("移動先のトレーナー情報を確認できません。XY・ORASのセーブを選び直してください。");
        var pk5 = new PK5(source.StoredData.ToArray());
        if (pk5.IsEgg) throw new InvalidDataException("タマゴは第6世代へ移動できません。ゲーム内で孵化させてください。");
        if (pk5.HeldItem != 0) throw new InvalidDataException("第6世代へ移動する前に、BW・BW2のゲーム内で持ち物を外してください。");
        if (pk5.Species == (ushort)Species.Kyurem && pk5.Form != 0)
            throw new InvalidDataException("合体中のキュレムは移動できません。ゲーム内で分離してください。");
        var pk6 = pk5.ConvertToPK6();
        // ConvertToPK6 uses a global recent-trainer cache and a random memory feeling.
        // Replace every cache-dependent value and the random field, so recovery reproduces the exact bytes.
        pk6.HandlingTrainerTrash.Clear(); pk6.HandlingTrainerName = trainer.Name;
        pk6.HandlingTrainerGender = trainer.Gender;
        pk6.Country = trainer.Country; pk6.Region = trainer.Region; pk6.ConsoleRegion = trainer.ConsoleRegion;
        pk6.Geo1_Country = trainer.Country; pk6.Geo1_Region = trainer.Region;
        var feelings = Enumerable.Range(0, 10).Where(i => MemoryContext6.CanHaveFeeling6(4, i, 0)).ToArray();
        pk6.HandlingTrainerMemoryFeeling = (byte)feelings[(int)(pk5.PID % (uint)feelings.Length)];
        var data = new byte[pk6.SIZE_STORED];pk6.WriteEncryptedDataStored(data);
        var notices = new List<string>
        {
            "第6世代へ変換します。DS作品へは戻せません。",
            "名前・リボン・なつき度・タイプ・能力値・特性・技のPPを第6世代の仕様へ合わせ、移動先のトレーナー情報と思い出を設定します。",
            "出会った場所と日付は第5世代の情報を保持します。",
        };
        if (pk5.PID != pk6.PID) notices.Add("色違い状態を保つためPIDを補正します（元のPIDは暗号化定数として保持）。");
        if (new[] { pk5.EV_HP, pk5.EV_ATK, pk5.EV_DEF, pk5.EV_SPA, pk5.EV_SPD, pk5.EV_SPE }.Any(ev => ev > 252))
            notices.Add("252を超える能力の努力値は、第6世代の上限252へ調整します。");
        return new(new Gen6PokemonCodec().Decode(data), true, notices);
    }

    private static PokemonTransferPlan PrepareGen7(PokemonSummary source, TransferTrainerProfile? trainer)
    {
        if (source.FormatKey == "pk7") return new(source, false, []);
        if (source.FormatKey != "pk6") throw new InvalidDataException("先にXY・ORASを経由してからサン・ムーン／USUMへ移動してください。");
        if (trainer is null || string.IsNullOrWhiteSpace(trainer.Name)) throw new InvalidDataException("移動先のトレーナー情報を確認できません。");
        var pk6 = new PK6(source.StoredData.ToArray());
        if (pk6.IsEgg) throw new InvalidDataException("タマゴは第7世代へ移動できません。ゲーム内で孵化させてください。");
        if (pk6.HeldItem != 0) throw new InvalidDataException("第7世代へ移動する前に、元のゲーム内で持ち物を外してください。");
        if ((pk6.Species == (ushort)Species.Kyurem && pk6.Form != 0)
            || (pk6.Species == (ushort)Species.Furfrou && pk6.Form != 0) || FormInfo.IsBattleOnlyForm(pk6.Species, pk6.Form, 6))
            throw new InvalidDataException("このフォルムはゲーム内で通常の姿に戻してから移動してください。");
        var pk7 = pk6.ConvertToPK7();
        pk7.HandlingTrainerTrash.Clear(); pk7.HandlingTrainerName = trainer.Name;
        pk7.HandlingTrainerGender = trainer.Gender; pk7.CurrentHandler = 1;
        pk7.HandlingTrainerFriendship = pk7.PersonalInfo.BaseFriendship;
        // ConvertToPK7 creates a random memory; fix it so a journal can reproduce identical bytes later.
        pk7.HandlingTrainerMemoryFeeling = (byte)(pk7.EncryptionConstant % 10);
        var bytes = new byte[pk7.SIZE_STORED];pk7.WriteEncryptedDataStored(bytes);
        return new(new Gen7PokemonCodec().Decode(bytes), true,
        [
            "第7世代へ変換します。以前の作品へは戻せません。",
            "親ID・個体値・努力値・性格・色違い状態・出会った情報を保持します。",
            "マーキング・特性・なつき度・思い出・リフレ等の情報と技のPPを第7世代の仕様へ合わせ、移動先のトレーナー情報を設定します。能力値は第7世代の種族値・特性で計算します。",
        ]);
    }

}
