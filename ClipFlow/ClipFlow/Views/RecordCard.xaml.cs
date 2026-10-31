using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ClipFlow.ViewModels;
using System.ComponentModel;
namespace ClipFlow.Views;
public sealed partial class RecordCard : UserControl
{
    private RecordViewModel? _item;
    public RecordCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (_item is not null) _item.PropertyChanged -= Changed; _item = null; };
    }
    private void Subscribe()
    {
        if (_item is not null) _item.PropertyChanged -= Changed;
        _item = DataContext as RecordViewModel;
        if (_item is not null) _item.PropertyChanged += Changed;
        UpdateFeedback();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(RecordViewModel.IsCopied)) UpdateFeedback(); }
    private void UpdateFeedback()
    {
        if (_item?.IsCopied == true)
        {
            var green = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            var white = new SolidColorBrush(Microsoft.UI.Colors.White);
            foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                CopyButton.Resources["ButtonBackground" + state] = green;
                CopyButton.Resources["ButtonForeground" + state] = white;
            }
            CopyButton.Background = green; CopyButton.Foreground = white;
        }
        else
        {
            foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            { CopyButton.Resources.Remove("ButtonBackground" + state); CopyButton.Resources.Remove("ButtonForeground" + state); }
            CopyButton.ClearValue(Button.BackgroundProperty); CopyButton.ClearValue(Button.ForegroundProperty);
        }
    }
}
