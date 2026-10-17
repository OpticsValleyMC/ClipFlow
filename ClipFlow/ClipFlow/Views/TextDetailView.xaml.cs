using ClipFlow.Core;
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
        Summary.Text = $"共 {text.Length:N0} 字符" + (_pages.Count > 1 ? " · 分页浏览，复制按钮会复制全部原文" : " · 可滚动查看与选择文本");
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
        PageLabel.Text = $"第 {_page + 1} / {_pages.Count} 页";
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => GoToPage(_page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => GoToPage(_page + 1);
}
