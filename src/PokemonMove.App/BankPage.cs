using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using System.Security.Cryptography;

namespace PokemonMove.App;

public sealed class BankPage : ContentPage
{
    private readonly IBankStore bank;
    private readonly ISaveDocuments documents;
    private readonly ISaveReader reader;
    private readonly IRegisteredSaves registry;
    private readonly IPokemonInspection inspection;
    private readonly IDualScreenManager dual;
    private readonly UserOperations operations;
    private BankItem? selectedItem;
    private readonly Label selected = new() { Text = "ポケモンを選択してください。", TextColor = Colors.White, FontSize = 12 };
    private readonly Button withdraw = ActionButton("選択したポケモンをセーブへ移動");
    private bool pendingMove;
    private IReadOnlyList<BankItem> items = [];
    private List<BankItem> filtered = [];
    private bool busy;
    private readonly Label status = new() { TextColor = Colors.White };
    private readonly SearchBar search = new() { Placeholder = "名前・ニックネームで検索", TextColor = Colors.White, PlaceholderColor = Colors.LightGray };
    private readonly Picker box = new() { Title = "バンクボックス", TextColor = Colors.White };
    private readonly Grid slots = new() { ColumnSpacing = 5, RowSpacing = 5 };
    private readonly Button export = ActionButton("バックアップを書き出す");
    private readonly Button import = ActionButton("バックアップを取り込む");
    private readonly Button recover = ActionButton("中断した移動を復旧する");
    private readonly Button exportSave = ActionButton("直前のセーブのバックアップを書き出す");

    public BankPage(IBankStore bank, ISaveDocuments documents, ISaveReader reader, IRegisteredSaves registry, IPokemonInspection inspection, IDualScreenManager dual, UserOperations operations)
    {
        this.bank = bank;
        this.documents = documents;
        this.reader = reader;
        this.registry = registry;
        this.inspection = inspection;
        this.dual = dual;
        this.operations = operations;
        Title = "ローカルバンク";
        BackgroundColor = Color.FromArgb("152B36");
        for (var i = 0; i < 6; i++) slots.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 5; i++) slots.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        search.TextChanged += (_, _) => Filter();
        box.SelectedIndexChanged += (_, _) => Render();
        export.Clicked += async (_, _) => await Run(async () =>
        {
            var data = await bank.ExportAsync();
            var file = Path.Combine(FileSystem.CacheDirectory, $"PokemonMove-bank-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            await File.WriteAllBytesAsync(file, data);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "バンクのバックアップを保存", File = new ShareFile(file, "application/json") });
        });
        import.Clicked += async (_, _) => await Run(async () =>
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "PokemonMove バンクのバックアップを選択" });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer)) != 0)
            {
                if (memory.Length + count > LocalBankStore.MaximumBackupBytes) throw new InvalidDataException("バックアップが大きすぎます。");
                memory.Write(buffer, 0, count);
            }
            var added = await bank.ImportAsync(memory.ToArray());
            await Refresh();
            await DisplayAlertAsync("取り込み完了", $"{added}匹を追加しました。同じデータは重複して追加しません。", "閉じる");
        });
        recover.Clicked += async (_, _) => await Run(async () =>
        {
            if (!await bank.HasPendingMoveAsync()) { await DisplayAlertAsync("移動の復旧", "中断した移動はありません。", "閉じる"); return; }
            await DisplayAlertAsync("移動の復旧", "エミュレータを終了し、中断した移動で選択した同じセーブを指定してください。", "選択する");
            var file = await documents.PickAsync();
            if (file is null) return;
            try { await bank.RecoverMoveAsync(file); }
            catch (SaveRecoveryRequiredException ex)
            {
                if (!await DisplayAlertAsync("移動前のセーブへ復旧", ex.Message + "\n選択したファイルを移動前の内容に戻します。", "復旧する", "キャンセル")) return;
                await bank.RecoverMoveAsync(file, restoreOriginal: true);
            }
            await Refresh();
            await DisplayAlertAsync("復旧完了", "セーブとバンクの状態を照合しました。", "閉じる");
        });
        exportSave.Clicked += async (_, _) => await Run(async () =>
        {
            var backup = await bank.LastSaveBackupAsync();
            var file = Path.Combine(FileSystem.CacheDirectory, backup.Name);
            await File.WriteAllBytesAsync(file, backup.Data);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "移動前のセーブを保存", File = new ShareFile(file, "application/octet-stream") });
        });
        withdraw.IsEnabled = false;
        withdraw.Clicked += async (_, _) => { if (selectedItem is { } item) await Run(() => Withdraw(item)); };
        SizeChanged += (_, _) => { foreach (var button in slots.Children.OfType<Button>()) button.HeightRequest = SlotHeight; };
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 12, Spacing = 8,
            Children =
            {
                status, search, box, slots, selected, withdraw,
                new Label { Text = "移動保管した個体は、保管元を含む対応セーブへ引き出せます。第4世代→第5世代は一方向の変換です。第5世代から第4世代へは戻せません。旧版のコピーはそのまま引き出せません。\n\nアプリの削除・端末変更の前にバックアップを書き出して保管してください。", TextColor = Colors.LightGray, FontSize = 13 },
                export, import, exportSave, recover
            }
        }};
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(Refresh);
    }

    private async Task Refresh()
    {
        // Parsing the collection must not block Android's UI thread.
        items = await Task.Run(bank.ListAsync);
        pendingMove = await bank.HasPendingMoveAsync();
        Filter();
    }

    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        filtered = items.Where(i => query.Length == 0 || i.Pokemon.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Pokemon.Nickname.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        status.Text = $"保管：{items.Count}匹 / 表示：{filtered.Count}匹" + (items.Count == 0 ? "\nセーブ内のポケモンを選び、バンクへ移動できます。" : "");
        if (pendingMove) status.Text += "\n中断した移動があります。先に復旧してください。";
        box.Items.Clear();
        for (var i = 0; i < Math.Max(1, (filtered.Count + 29) / 30); i++) box.Items.Add($"バンクボックス {i + 1}");
        box.SelectedIndex = 0;
    }

    private void Render()
    {
        slots.Children.Clear();
        inspection.Clear();
        selectedItem = null; withdraw.IsEnabled = false; selected.Text = "ポケモンを選択してください。";
        if (box.SelectedIndex < 0) return;
        var page = filtered.Skip(box.SelectedIndex * 30).Take(30).ToArray();
        for (var i = 0; i < page.Length; i++)
        {
            var item = page[i];
            var button = new Button { Text = item.Pokemon.Label, FontSize = 10, Padding = 1, HeightRequest = SlotHeight, CornerRadius = 8, BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White };
            button.Clicked += async (_, _) =>
            {
                if (busy) return;
                using var operation = operations.Begin();
                selectedItem = item; withdraw.IsEnabled = item.Entry.TransferEligible && !pendingMove;
                selected.Text = $"選択：{item.Pokemon.Name} Lv.{item.Pokemon.Level}（第{(item.Pokemon.FormatKey == "pk5" ? 5 : 4)}世代）" + (item.Entry.TransferEligible ? "" : "（旧コピー）");
                inspection.SelectPokemon(item.Pokemon, $"バンク / 保管元：{item.Entry.SourceGame}");
                if (!dual.IsActive) await DisplayAlertAsync(item.Pokemon.Name, Detail(item.Pokemon), "閉じる");
            };
            slots.Add(button, i % 6, i / 6);
        }
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        using var operation = operations.Begin();
        busy = true;
        export.IsEnabled = import.IsEnabled = exportSave.IsEnabled = recover.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("バンクを処理できませんでした", ex is InvalidDataException ? ex.Message : "保存先の空き容量・アクセス権限を確認してください。", "閉じる");
        }
        finally { busy = false; export.IsEnabled = import.IsEnabled = exportSave.IsEnabled = recover.IsEnabled = true; }
    }

    private async Task Withdraw(BankItem item)
    {
        var detail = (dual.IsActive ? $"{item.Pokemon.Name} Lv.{item.Pokemon.Level}" : Detail(item.Pokemon)) + $"\n\n保管元：{item.Entry.SourceGame}\nトレーナー：{item.Entry.SourceTrainer}";
        if (!item.Entry.TransferEligible)
        {
            await DisplayAlertAsync(item.Pokemon.Name, detail + "\n\n旧版のコピー保管です。元セーブの同じ個体をバンクへ移動すると引き出せるようになります。", "閉じる");
            return;
        }
        if (!await DisplayAlertAsync(item.Pokemon.Name, detail + "\n\n書き込みを確認した後、バンクから取り除きます。エミュレータを終了してください。", "セーブへ移動", "閉じる")) return;
        var target = await documents.PickAsync();
        if (target is null) return;
        var data = await target.ReadAsync();
        var save = await Task.Run(() => reader.Read(data));
        await registry.RegisterAsync(target.Id, save);
        var transferDate = DateOnly.FromDateTime(DateTime.Today);
        var plan = new PokemonTransfer().Prepare(item.Pokemon, save.FormatKey, transferDate);
        if (save.UnlockedBoxCount == 0) throw new InvalidDataException("開放済みのボックスがありません。先にゲーム内でボックスを利用してください。");
        var choices = save.Boxes.Take(save.UnlockedBoxCount ?? save.Boxes.Count).Select((b, i) => $"{i + 1}: {b.Name}（空き{b.Slots.Count(p => p.Species == 0)}）").ToArray();
        var selected = await DisplayActionSheetAsync($"{save.Game} / {save.TrainerName} の引き出し先", "キャンセル", null, choices);
        var index = Array.IndexOf(choices, selected);
        if (index < 0) return;
        var slot = save.Boxes[index].Slots.FirstOrDefault(p => p.Species == 0);
        if (slot is null) throw new InvalidDataException("そのボックスに空き枠がありません。");
        var notices = plan.Notices.Count == 0 ? "" : "\n\n" + string.Join("\n", plan.Notices);
        if (!await DisplayAlertAsync(plan.IsGenerationTransfer ? "世代をまたぐ移動を確定" : "移動を確定", $"{save.Game} / {save.TrainerName}\n{save.Boxes[index].Name} の {slot.Slot + 1}番へ移動します。選択したセーブを書き換え、成功後にバンクから取り除きます。{notices}", plan.IsGenerationTransfer ? "第5世代へ移動" : "移動する", "キャンセル")) return;
        await bank.MoveToSaveAsync(item.Entry.Id, target, new(false, index, slot.Slot), SHA256.HashData(data), transferDate);
        await Refresh();
        inspection.SelectPokemon(plan.Pokemon, $"{save.Game} / {save.TrainerName} / {save.Boxes[index].Name}へ移動しました");
        await DisplayAlertAsync("移動完了", "セーブへの書き込みを確認し、バンクから取り除きました。", "閉じる");
    }

    private double SlotHeight => Height > 0 && Height < 550 ? 36 : 60;

    internal static string Detail(PokemonSummary p)
    {
        var d = p.Details;
        var stats = d is null ? "" : $"\nHP {d.Stats.HP} / 攻撃 {d.Stats.Attack} / 防御 {d.Stats.Defense}\n特攻 {d.Stats.SpecialAttack} / 特防 {d.Stats.SpecialDefense} / 素早さ {d.Stats.Speed}";
        var training = d is null ? "" : $"\n努力値（合計 {d.TotalEffortValues}）\nHP {d.EffortValues.HP} / 攻撃 {d.EffortValues.Attack} / 防御 {d.EffortValues.Defense}\n特攻 {d.EffortValues.SpecialAttack} / 特防 {d.EffortValues.SpecialDefense} / 素早さ {d.EffortValues.Speed}\n個体値（{d.PerfectIVCount}V）\nHP {d.IndividualValues.HP} / 攻撃 {d.IndividualValues.Attack} / 防御 {d.IndividualValues.Defense}\n特攻 {d.IndividualValues.SpecialAttack} / 特防 {d.IndividualValues.SpecialDefense} / 素早さ {d.IndividualValues.Speed}\n親のID：{d.OriginalTrainerId:D5}";
        return $"ニックネーム：{p.Nickname}\nLv.{p.Level} {d?.Gender}\nタイプ：{p.Type}\n色違い：{(p.IsShiny ? "はい" : "いいえ")}\n性格：{d?.Nature ?? "—"}\n特性：{d?.Ability ?? "—"}\nもちもの：{d?.HeldItem ?? "—"}{stats}\n{training}\n\n技\n{string.Join("\n", p.Moves)}";
    }
    private static Button ActionButton(string text) => new() { Text = text, BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White, CornerRadius = 12 };
}
