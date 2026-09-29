namespace AdventureCreator.Studio.Pages;

/// <summary>A simple modal sheet showing a scrollable report (import results, validation, reference help).</summary>
public sealed class ReportPage : ContentPage
{
    public ReportPage(string title, string text)
    {
        Title = title;
        BackgroundColor = AdventureCreator.Maui.Theme.Window;
        var close = new Button { Text = "Close", HorizontalOptions = LayoutOptions.End, WidthRequest = 100 };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var copy = new Button { Text = "Copy", HorizontalOptions = LayoutOptions.End, WidthRequest = 100 };
        copy.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(text);
        var grid = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = 20, RowSpacing = 12 };
        grid.Add(new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = AdventureCreator.Maui.Theme.Accent }, 0, 0);
        grid.Add(new ScrollView { Content = new Label { Text = text, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap } }, 0, 1);
        grid.Add(new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { copy, close } }, 0, 2);
        Content = grid;
    }
}
