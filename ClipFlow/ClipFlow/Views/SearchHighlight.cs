using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace ClipFlow.Views;

public static class SearchHighlight
{
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached("Query", typeof(string), typeof(SearchHighlight), new PropertyMetadata("", Changed));
    public static string GetQuery(DependencyObject element) => (string)element.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject element, string value) => element.SetValue(QueryProperty, value);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock block) return;
        if (block.ReadLocalValue(ObserverProperty) == DependencyProperty.UnsetValue)
        {
            block.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Apply(block));
            block.ActualThemeChanged += (_, _) => Apply(block);
            block.SetValue(ObserverProperty, true);
        }
        Apply(block);
    }
    private static readonly DependencyProperty ObserverProperty = DependencyProperty.RegisterAttached("Observer", typeof(bool), typeof(SearchHighlight), new PropertyMetadata(false));
    private static void Apply(TextBlock block)
    {
        block.TextHighlighters.Clear();
        var query = GetQuery(block);
        if (string.IsNullOrEmpty(query)) return;
        var highlighter = new TextHighlighter
        {
            Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        for (var start = 0; start <= block.Text.Length - query.Length;)
        {
            var index = block.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            highlighter.Ranges.Add(new TextRange { StartIndex = index, Length = query.Length });
            start = index + query.Length;
        }
        if (highlighter.Ranges.Count > 0) block.TextHighlighters.Add(highlighter);
    }
}
