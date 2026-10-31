using System.Runtime.InteropServices;
using ClipFlow.Core;
using ClipFlow.Services;
using ClipFlow.ViewModels;
using ClipFlow.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.ApplicationModel.DataTransfer;

namespace ClipFlow.Diagnostics;

internal static class QuickFeaturesScenario
{
    internal static async Task RunAsync(MainViewModel vm, MainWindow main, FrameworkElement root, Func<QuickHistoryWindow?> popup, Action<string> navigate)
    {
        var report = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); report.Add("PASS: " + message); }
        async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 60; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("新增快捷功能验收超时");
        }
        var input = new TextBox { Text = "before [replace] after", AcceptsReturn = true };
        var other = new Window { Content = input };
        using var competitor = new HotkeyService(WinRT.Interop.WindowNative.GetWindowHandle(other), () => { });
        try
        {
            Check(vm.Settings.HotkeyModifiers == 1 && vm.Settings.HotkeyKey == 0x56, "old settings default to Alt + V");
            var text = "ClipFlow alpha NEEDLE needle 😀 fixture";
            await vm.Store.SaveAsync(new CapturedContent(ContentKind.Text, text, ContentIdentity.TextHash(text)), 500);
            var longText = new string('文', 3000) + "NEEDLE needle tail";
            await vm.Store.SaveAsync(new CapturedContent(ContentKind.Text, longText, ContentIdentity.TextHash(longText)), 500);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aP3sAAAAASUVORK5CYII=");
            var image = await vm.Store.SaveAsync(new CapturedContent(ContentKind.Image, null, ContentIdentity.ImageHash(png, 1, 1), Width: 1, Height: 1, ImagePng: png, ThumbnailPng: png), 500);
            var textRecord = (await vm.Store.LoadAsync()).Single(record => record.Text == text);
            await vm.Store.SetFavoriteAsync(textRecord.Id, true);
            await vm.UpdateSettingsAsync(vm.Settings);
            navigate("recent"); vm.Search = "needle";
            await Task.Delay(300); root.UpdateLayout();
            var item = vm.Records.Single(record => record.Record.Text == text);
            Check(vm.Records.Count == 2 && vm.Records.All(record => record.Preview.Contains("NEEDLE")), "search previews include matches beyond the first 512 characters");
            var cards = Find<RecordCard>(root).Where(card => card.DataContext is RecordViewModel record && vm.Records.Contains(record)).ToList();
            Check(cards.Count >= 2 && cards.All(card => ((TextBlock)card.FindName("PreviewText")).TextHighlighters.Single().Ranges.Count == 2), "all case-insensitive keyword occurrences are highlighted");
            await vm.CopyAsync(item);
            Check(item.IsCopied && item.CopyGlyph == "\uE73E" && await Clipboard.GetContent().GetTextAsync() == text, "successful clipboard write shows a temporary check mark");
            await Task.Delay(350); root.UpdateLayout();
            await IntegrationScenario.RenderAsync(root, Path.Combine(vm.Store.RootPath, "copy-search.png"));
            await WaitAsync(() => !item.IsCopied);
            Check(item.CopyGlyph == "\uE8C8", "copy feedback automatically restores the copy icon");
            var imageItem = new RecordViewModel(image, vm);
            await vm.CopyAsync(imageItem);
            Check(imageItem.IsCopied && Clipboard.GetContent().Contains(StandardDataFormats.Bitmap), "image copy uses the same successful feedback");
            var missing = new RecordViewModel(image with { Id = Guid.NewGuid().ToString(), ImagePath = "images/missing.png" }, vm);
            var failed = false;
            try { await vm.CopyAsync(missing); } catch { failed = true; }
            Check(failed && !missing.IsCopied, "failed clipboard writes never show success feedback");
            vm.Search = ""; await Task.Delay(100);
            Check(Find<RecordCard>(root).All(card => ((TextBlock)card.FindName("PreviewText")).TextHighlighters.Count == 0), "clearing the search removes highlights from recycled cards");
            await vm.ChangeShortcutAsync(7, 0x79); // Ctrl + Alt + Shift + F10, never take the user's Alt+V.
            var saved = await new HistoryStore(vm.Store.RootPath).InitializeAndLoadAsync();
            Check(saved.HotkeyModifiers == 7 && saved.HotkeyKey == 0x79, "custom shortcut survives storage reload");
            Check(competitor.Register(7, 0x78), "isolated conflicting shortcut registered");
            var rejected = false;
            try { await vm.ChangeShortcutAsync(7, 0x78); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && vm.Settings.HotkeyKey == 0x79, "conflicts preserve the previous shortcut and preference");
            rejected = false;
            try { await vm.ChangeShortcutAsync(2, 0x56); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && vm.Settings.HotkeyKey == 0x79, "custom shortcuts cannot intercept the standard Ctrl+V paste gesture");
            navigate("settings"); await Task.Delay(100);
            await IntegrationScenario.RenderAsync(root, Path.Combine(vm.Store.RootPath, "shortcut-settings.png"));
            SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(main), 0x10, UIntPtr.Zero, IntPtr.Zero);
            other.Activate(); await Task.Delay(150);
            await NativeWindowCapture.SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(other), Path.Combine(vm.Store.RootPath, "paste-target.png"));
            input.Focus(FocusState.Programmatic); input.SelectionStart = 7; input.SelectionLength = 9;
            await NativeKeyboard.ChordAsync(0x11, 0x12, 0x10, 0x79);
            await WaitAsync(() => popup()?.IsOpen == true);
            var quick = popup()!;
            var quickRoot = (FrameworkElement)quick.Content;
            var search = (TextBox)quickRoot.FindName("SearchBox");
            var list = (ListView)quickRoot.FindName("RecordsList");
            await WaitAsync(() => list.Items.Count == 3);
            Check(!IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(main)), "real global shortcut opens the floating history while main window stays hidden");
            await Task.Delay(300);
            var beforeDrag = quick.AppWindow.Position;
            await NativePointer.DragAsync(quick.NativeHandle, (FrameworkElement)quickRoot.FindName("DragRegion"), beforeDrag.X > 100 ? -70 : 70, beforeDrag.Y > 100 ? -40 : 40);
            Check(Math.Abs(quick.AppWindow.Position.X - beforeDrag.X) >= 40 && quick.IsOpen, "native mouse drag moves floating history without dismissing it");
            var favorites = (ToggleButton)quickRoot.FindName("FavoritesOnly");
            favorites.IsChecked = true;
            Check(list.Items.Count == 1, "floating favorites filter includes only saved favorites");
            favorites.IsChecked = false;
            search.Focus(FocusState.Programmatic);
            // Seed a committed query, so Enter tests picker selection rather
            // than committing a candidate from the user's active IME.
            search.Text = "alpha";
            await Task.Delay(400);
            Check(list.Items.Count == 1 && vm.Search == "", $"floating search filters independently of the main window (rows={list.Items.Count}, floating={search.Text}, main={vm.Search})");
            await NativeWindowCapture.SaveOwnWindowAsync(quick.NativeHandle, Path.Combine(vm.Store.RootPath, "quick-history.png"));
            search.Focus(FocusState.Programmatic);
            await NativeKeyboard.PressAsync(0x28); await NativeKeyboard.PressAsync(0x0D);
            await WaitAsync(() => !quick.IsOpen);
            await WaitAsync(() => input.Text == "before " + text + " after");
            Check(await Clipboard.GetContent().GetTextAsync() == text, "Enter pastes into the original input selection and dismisses the picker");
            other.Activate(); await Task.Delay(100);
            await NativeKeyboard.ChordAsync(0x11, 0x12, 0x10, 0x79);
            await WaitAsync(() => popup()?.IsOpen == true);
            var second = popup()!;
            var secondRoot = (FrameworkElement)second.Content;
            var secondList = (ListView)secondRoot.FindName("RecordsList");
            await WaitAsync(() => secondList.Items.Count == 3); await Task.Delay(300);
            var favorite = Find<Button>(secondRoot).First(button => button.Tag is RecordViewModel record && record.Id == textRecord.Id && button.Name != "PasteItem");
            var unchanged = input.Text;
            await NativePointer.ClickAsync(second.NativeHandle, favorite);
            await WaitAsync(() => secondList.Items.Cast<RecordViewModel>().Single(record => record.Id == textRecord.Id).Record.IsFavorite == false);
            Check(second.IsOpen && input.Text == unchanged && !(await vm.Store.LoadAsync()).Single(record => record.Id == textRecord.Id).IsFavorite, "favorite action persists and never triggers paste");
            var menuButton = Find<Button>(secondRoot).First(button => button.Flyout is MenuFlyout && button.DataContext is RecordViewModel record && record.Id == image.Id);
            menuButton.Flyout.ShowAt(menuButton); await Task.Delay(150);
            var delete = ((MenuFlyout)menuButton.Flyout).Items.OfType<MenuFlyoutItem>().Single(menu => menu.Text == "删除");
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(delete).GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitAsync(() => secondList.Items.Count == 2);
            Check(second.IsOpen && input.Text == unchanged && !(await vm.Store.LoadAsync()).Any(record => record.Id == image.Id), "per-record delete removes storage without pasting or closing the picker");
            ((TextBox)secondRoot.FindName("SearchBox")).Text = "alpha"; await Task.Delay(300);
            var pasteButton = Find<Button>(secondRoot).First(button => button.Name == "PasteItem" && button.Tag is RecordViewModel record && record.Id == textRecord.Id);
            input.SelectionStart = input.Text.Length; input.SelectionLength = 0;
            await NativePointer.ClickAsync(second.NativeHandle, pasteButton);
            await WaitAsync(() => !second.IsOpen && input.Text == unchanged + text);
            Check(true, "real item click restores the original caret and automatically pastes text");
            other.Activate(); input.Focus(FocusState.Programmatic); await Task.Delay(100);
            await NativeKeyboard.ChordAsync(0x11, 0x12, 0x10, 0x79);
            await WaitAsync(() => popup()?.IsOpen == true);
            await NativeKeyboard.PressAsync(0x1B);
            await WaitAsync(() => popup()?.IsOpen == false);
            Check(true, "Escape closes the floating history and it can reopen");
            for (var i = 0; i < 10; i++)
            {
                var scrolling = "ClipFlow scrolling fixture " + i;
                await vm.Store.SaveAsync(new CapturedContent(ContentKind.Text, scrolling, ContentIdentity.TextHash(scrolling)), 500);
            }
            other.Activate(); await Task.Delay(100);
            await NativeKeyboard.ChordAsync(0x11, 0x12, 0x10, 0x79);
            await WaitAsync(() => popup()?.IsOpen == true);
            var scrollingPicker = popup()!;
            var scrollingRoot = (FrameworkElement)scrollingPicker.Content;
            var scrollingList = (ListView)scrollingRoot.FindName("RecordsList");
            await WaitAsync(() => scrollingList.Items.Count > 10); await Task.Delay(250);
            var viewer = Find<ScrollViewer>(scrollingList).First();
            var scrollbar = Find<ScrollBar>(scrollingList).First(bar => bar.Orientation == Orientation.Vertical);
            var container = (ListViewItem)scrollingList.ContainerFromIndex(0);
            var card = (FrameworkElement)container.ContentTemplateRoot;
            var cardRight = card.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(card.ActualWidth, 0)).X;
            var barLeft = scrollbar.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point()).X;
            Check(barLeft >= cardRight - .5, $"scrollbar stays outside cards (card={cardRight:F1}, bar={barLeft:F1})");
            await NativePointer.WheelAsync(scrollingPicker.NativeHandle, scrollingList, -120);
            var wheelOffset = viewer.VerticalOffset;
            Check(wheelOffset >= 70, $"wheel responds immediately without a long easing tail (offset={wheelOffset:F1})");
            await Task.Delay(200);
            Check(Math.Abs(viewer.VerticalOffset - wheelOffset) < .5, "wheel stops promptly without trailing drift");
            await NativePointer.WheelAsync(scrollingPicker.NativeHandle, scrollingList, 120);
            await WaitAsync(() => viewer.VerticalOffset < 1);
            var body = Find<Button>(scrollingRoot).First(button => button.Name == "PasteItem");
            await NativePointer.HoverAsync(scrollingPicker.NativeHandle, body);
            await NativeWindowCapture.SaveOwnWindowAsync(scrollingPicker.NativeHandle, Path.Combine(vm.Store.RootPath, "picker-body-hover.png"));
            var more = Find<Button>(scrollingRoot).First(button => button.Flyout is MenuFlyout);
            await NativePointer.HoverAsync(scrollingPicker.NativeHandle, more);
            await NativeWindowCapture.SaveOwnWindowAsync(scrollingPicker.NativeHandle, Path.Combine(vm.Store.RootPath, "picker-more-hover.png"));
            var star = Find<Button>(scrollingRoot).First(button => button.Tag is RecordViewModel && button.Name != "PasteItem");
            await NativePointer.HoverAsync(scrollingPicker.NativeHandle, star);
            await NativeWindowCapture.SaveOwnWindowAsync(scrollingPicker.NativeHandle, Path.Combine(vm.Store.RootPath, "picker-favorite-hover.png"));
            other.Activate(); await WaitAsync(() => popup()?.IsOpen == false);
            Check(true, "switching to another window dismisses floating history");
            report.Add("SUCCESS");
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); }
        finally { competitor.Dispose(); other.Close(); popup()?.Dismiss(); }
        await File.WriteAllLinesAsync(Path.Combine(vm.Store.RootPath, "quick-results.txt"), report);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task<AppSettings> InitializeAndLoadAsync(this HistoryStore store) { await store.InitializeAsync(); return await store.LoadSettingsAsync(); }
    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr window);
}
