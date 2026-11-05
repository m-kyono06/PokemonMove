using Microsoft.Maui.Controls.Shapes;

namespace PokemonMove.App;

/// <summary>Shared handheld-console palette and compact, framed controls.</summary>
internal static class RetroTheme
{
    private static readonly Color Frame = Color.FromArgb("8298A5");
    private static readonly Color Gold = Color.FromArgb("F2CF72");
    private static readonly Color Surface = Color.FromArgb("344E65");

    public static ResourceDictionary Resources()
    {
        var resources = new ResourceDictionary();
        var buttons = new Style(typeof(Button));
        buttons.Setters.Add(new Setter { Property = Button.CornerRadiusProperty, Value = 4 });
        buttons.Setters.Add(new Setter { Property = Button.BorderWidthProperty, Value = 1d });
        buttons.Setters.Add(new Setter { Property = Button.BorderColorProperty, Value = Frame });
        buttons.Setters.Add(new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold });
        resources.Add(buttons);
        return resources;
    }

    public static Border Panel(View content) => new()
    {
        Content = content, BackgroundColor = Surface, Stroke = Frame,
        StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 4 },
        Padding = 8, Margin = new Thickness(0, 2)
    };

    public static View Heading(string title, string subtitle)
    {
        var words = new VerticalStackLayout { Spacing = 1, Children =
        {
            new Label { Text = subtitle, FontSize = 10, CharacterSpacing = 2, TextColor = Gold },
            new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White }
        }};
        var grid = new Grid { ColumnDefinitions = { new(8), new(GridLength.Star) }, ColumnSpacing = 10 };
        grid.Add(new BoxView { Color = Gold }, 0);
        grid.Add(words, 1);
        return grid;
    }

    public static void SelectSlot(Grid slots, Button selected)
    {
        foreach (var button in slots.Children.OfType<Button>())
        {
            var active = ReferenceEquals(button, selected);
            button.BorderColor = active ? Gold : Frame;
            button.BorderWidth = active ? 3 : 1;
            if (!button.IsEnabled) continue;
            button.BackgroundColor = active ? Color.FromArgb("735D32") : Color.FromArgb("45637B");
        }
    }
}
