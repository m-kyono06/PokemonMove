using PokemonMove.Domain;
using PokemonMove.Engine;

namespace PokemonMove.App;

public sealed class MainPage : ContentPage
{
    private readonly ISaveReader reader;
    private SaveSnapshot? snapshot;
    private readonly Label status = new() { Text = "HGSS のセーブを開いて、冒険の仲間を確認しましょう。", TextColor = Color.FromArgb("AFC2CD") };
    private readonly Picker location = new() { Title = "手持ち / ボックス", TextColor = Colors.White, BackgroundColor = Color.FromArgb("233E4B") };
    private readonly Grid slots = new() { ColumnSpacing = 5, RowSpacing = 5 };
    private readonly Button open = new() { Text = "セーブファイルを開く", BackgroundColor = Color.FromArgb("61DFC0"), TextColor = Color.FromArgb("152B36"), CornerRadius = 14 };

    public MainPage(ISaveReader reader)
    {
        this.reader = reader;
        Title = "PokemonMove";
        BackgroundColor = Color.FromArgb("152B36");
        for (var i = 0; i < 6; i++) slots.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 5; i++) slots.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        open.Clicked += OpenSave;
        location.SelectedIndexChanged += (_, _) => RenderSlots();
        location.IsVisible = false;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(18, 30), Spacing = 20,
                Children =
                {
                    new Label { Text = "POKEMON MOVE", FontSize = 13, CharacterSpacing = 3, TextColor = Color.FromArgb("61DFC0") },
                    new Label { Text = "冒険の仲間を、ひとつに。", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    new Label { Text = "ハートゴールド / ソウルシルバー", TextColor = Color.FromArgb("AFC2CD") },
                    open, status, location, slots,
                    new Label { Text = "このバージョンは表示専用です。元のセーブは変更しません。\n\nmelonDS：通常の .sav\nDraStic：バッテリーセーブの .dsv\nステートセーブには対応していません。\n\nバンク・移動・収集図鑑は今後追加予定です。", FontSize = 13, TextColor = Color.FromArgb("AFC2CD") }
                }
            }
        };
    }

    private async void OpenSave(object? sender, EventArgs e)
    {
        open.IsEnabled = false;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "HGSS の通常セーブを選択" });
            if (file is null) return;
            status.Text = "セーブを読み込み中…";
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            // Limit reads even when the document provider reports no length.
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer)) != 0)
            {
                if (memory.Length + count > HgssSaveReader.MaximumFileSize)
                    throw new InvalidDataException("ファイルが大きすぎます。HGSSの通常セーブを選択してください。");
                memory.Write(buffer, 0, count);
            }
            var loaded = await Task.Run(() => reader.Read(memory.ToArray()));
            snapshot = loaded;
            location.Items.Clear();
            location.Items.Add($"手持ち（{loaded.Party.Count}匹）");
            foreach (var box in loaded.Boxes) location.Items.Add(box.Name);
            location.IsVisible = true;
            location.SelectedIndex = 0;
            status.Text = $"{loaded.Game}\nトレーナー：{loaded.TrainerName}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status.Text = snapshot is null ? "セーブを開けませんでした。別のファイルをお試しください。" : $"{snapshot.Game}\nトレーナー：{snapshot.TrainerName}（前のセーブを表示中）";
            await DisplayAlertAsync("読み込みできませんでした", ex is InvalidDataException ? ex.Message : "ファイルの内容またはアクセス権限を確認してください。", "閉じる");
        }
        finally { open.IsEnabled = true; }
    }

    private void RenderSlots()
    {
        slots.Children.Clear();
        if (snapshot is null || location.SelectedIndex < 0) return;
        var pokemon = location.SelectedIndex == 0 ? snapshot.Party : snapshot.Boxes[location.SelectedIndex - 1].Slots;
        foreach (var p in pokemon)
        {
            var button = new Button
            {
                Text = p.Label, FontSize = 10, Padding = new Thickness(1), HeightRequest = 68,
                BackgroundColor = Color.FromArgb(p.Species == 0 ? "1C333E" : "294D59"), TextColor = Colors.White,
                CornerRadius = 8, IsEnabled = p.Species != 0,
            };
            button.Clicked += async (_, _) => await DisplayAlertAsync(p.Name,
                $"ニックネーム：{p.Nickname}\nLv.{p.Level}\nタイプ：{p.Type}\n色違い：{(p.IsShiny ? "はい" : "いいえ")}\n\n技\n{string.Join("\n", p.Moves)}", "閉じる");
            slots.Add(button, p.Slot % 6, p.Slot / 6);
        }
    }
}
