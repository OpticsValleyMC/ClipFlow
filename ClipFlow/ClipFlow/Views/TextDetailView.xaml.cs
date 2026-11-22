using ClipFlow.Core;
using static ClipFlow.Core.LocalizedStrings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipFlow.Views;

public sealed partial class TextDetailView : UserControl
{
    private readonly TextPages _pages;
    private int _page;
    public int PageCount => _pages.Count;
    public TextDetailView(string text)
    {
        InitializeComponent();
        _pages = new TextPages(text);
        Summary.Text = L("TextLengthFormat", text.Length) + (_pages.Count > 1 ? L("TextPagedHelp") : L("TextScrollHelp"));
        Paging.Visibility = _pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        GoToPage(0);
    }
    public void GoToPage(int index)
    {
        _page = Math.Clamp(index, 0, _pages.Count - 1);
        FullText.Text = _pages.GetPage(_page);
        FullText.Select(0, 0);
        Previous.IsEnabled = _page > 0;
        Next.IsEnabled = _page < _pages.Count - 1;
        PageLabel.Text = L("TextPageFormat", _page + 1, _pages.Count);
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => GoToPage(_page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => GoToPage(_page + 1);
}
