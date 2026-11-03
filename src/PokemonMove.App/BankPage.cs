using PokemonMove.Domain;
using PokemonMove.Infrastructure;

namespace PokemonMove.App;

public sealed class BankPage : ContentPage
{
    private readonly IBankStore bank;
    private IReadOnlyList<BankItem> items = [];
    private List<BankItem> filtered = [];
    private bool busy;
    private readonly Label status = new() { TextColor = Colors.White };
    private readonly SearchBar search = new() { Placeholder = "名前・ニックネームで検索", TextColor = Colors.White, PlaceholderColor = Colors.LightGray };
    private readonly Picker box = new() { Title = "バンクボックス", TextColor = Colors.White };
    private readonly Grid slots = new() { ColumnSpacing = 5, RowSpacing = 5 };
    private readonly Button export = ActionButton("バックアップを書き出す");
    private readonly Button import = ActionButton("バックアップを取り込む");
    private readonly Button restore = ActionButton("直前の状態へ戻す");

    public BankPage(IBankStore bank)
    {
        this.bank = bank;
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
        restore.Clicked += async (_, _) => await Run(async () =>
        {
            if (!await DisplayAlertAsync("直前の状態へ戻す", "現在のバンクを直前の自動バックアップで置き換えます。直近の追加分は戻ります。よろしいですか？", "戻す", "キャンセル")) return;
            await bank.RestorePreviousAsync();
            await Refresh();
        });
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 18, Spacing = 16,
            Children =
            {
                new Label { Text = "仲間を、この端末に。", FontSize = 24, TextColor = Colors.White, FontAttributes = FontAttributes.Bold },
                status, search, box, slots,
                new Label { Text = "セーブからのコピーを保管しています。ゲームへの引き出しは今後追加予定です。\n\nアプリの削除・端末変更の前にバックアップを書き出して保管してください。", TextColor = Colors.LightGray, FontSize = 13 },
                export, import, restore
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
        Filter();
    }

    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        filtered = items.Where(i => query.Length == 0 || i.Pokemon.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Pokemon.Nickname.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        status.Text = $"保管：{items.Count}匹 / 表示：{filtered.Count}匹" + (items.Count == 0 ? "\nセーブ内のポケモンを選び、バンクにコピーして保管できます。" : "");
        box.Items.Clear();
        for (var i = 0; i < Math.Max(1, (filtered.Count + 29) / 30); i++) box.Items.Add($"バンクボックス {i + 1}");
        box.SelectedIndex = 0;
    }

    private void Render()
    {
        slots.Children.Clear();
        if (box.SelectedIndex < 0) return;
        var page = filtered.Skip(box.SelectedIndex * 30).Take(30).ToArray();
        for (var i = 0; i < page.Length; i++)
        {
            var item = page[i];
            var button = new Button { Text = item.Pokemon.Label, FontSize = 10, Padding = 1, HeightRequest = 68, CornerRadius = 8, BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White };
            button.Clicked += async (_, _) => await DisplayAlertAsync(item.Pokemon.Name,
                Detail(item.Pokemon) + $"\n\nコピー元：{item.Entry.SourceGame}\nトレーナー：{item.Entry.SourceTrainer}\n保管日時：{item.Entry.StoredAt.ToLocalTime():yyyy/MM/dd HH:mm}", "閉じる");
            slots.Add(button, i % 6, i / 6);
        }
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        export.IsEnabled = import.IsEnabled = restore.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("バンクを処理できませんでした", ex is InvalidDataException ? ex.Message : "保存先の空き容量・アクセス権限を確認してください。", "閉じる");
        }
        finally { busy = false; export.IsEnabled = import.IsEnabled = restore.IsEnabled = true; }
    }

    internal static string Detail(PokemonSummary p) => $"ニックネーム：{p.Nickname}\nLv.{p.Level}\nタイプ：{p.Type}\n色違い：{(p.IsShiny ? "はい" : "いいえ")}\n\n技\n{string.Join("\n", p.Moves)}";
    private static Button ActionButton(string text) => new() { Text = text, BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White, CornerRadius = 12 };
}
