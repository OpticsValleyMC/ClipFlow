using Microsoft.UI.Xaml.Controls;
using ClipFlow.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
namespace ClipFlow.Views;
public sealed partial class HistoryView : UserControl
{
    private MainViewModel? _owner;
    private readonly ListWheelScroll _wheelScroll;

    public HistoryView()
    {
        InitializeComponent();
        _wheelScroll = new ListWheelScroll(HistoryList, distancePerNotch: 48);
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
        _wheelScroll.Reset();
        if (args.NewStartingIndex != 0) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_owner is not null && _owner.Records.Count > 0)
            {
                _wheelScroll.Reset();
                HistoryList.ScrollIntoView(_owner.Records[0], ScrollIntoViewAlignment.Leading);
            }
        });
    }
}
