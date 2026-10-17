using Microsoft.UI.Xaml.Controls;
using ClipFlow.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
namespace ClipFlow.Views;
public sealed partial class HistoryView : UserControl
{
    private MainViewModel? _owner;
    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_owner is not null)
            {
                _owner.Records.CollectionChanged -= RecordsChanged;
                _owner.PropertyChanged -= OwnerChanged;
            }
            _owner = DataContext as MainViewModel;
            if (_owner is not null)
            {
                _owner.Records.CollectionChanged += RecordsChanged;
                _owner.PropertyChanged += OwnerChanged;
            }
            UpdateRecordingIcon();
        };
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.RecordingSeverity)) UpdateRecordingIcon();
    }
    private void UpdateRecordingIcon()
    {
        var key = _owner?.RecordingSeverity switch
        {
            InfoBarSeverity.Success => "RecordingIcon",
            InfoBarSeverity.Warning => "PausedIcon",
            _ => "UnavailableIcon"
        };
        RecordingStatus.IconSource = (IconSource)Resources[key];
    }
    private void RecordsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewStartingIndex != 0) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_owner is not null && _owner.Records.Count > 0)
                HistoryList.ScrollIntoView(_owner.Records[0], ScrollIntoViewAlignment.Leading);
        });
    }
}
