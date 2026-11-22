using System.ComponentModel;
using ClipFlow.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Windows.System.UserProfile;

namespace ClipFlow.Services;

internal static class UiLanguage
{
    private static readonly Dictionary<string, LocalizedLabel> Labels = [];
    private static readonly LocalizedLabel LanguageTag = new(LocalizedStrings.Culture.Name);

    internal static void Apply(AppLanguage language)
    {
#if DEBUG
        // Keep existing desktop fixtures independent of the host language.
        if (language == AppLanguage.System && Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") is not null)
            language = AppLanguage.SimplifiedChinese;
#endif
        var culture = LocalizedStrings.ResolveCulture(language, GlobalizationPreferences.Languages);
        if (Labels.Count > 0 && culture.Name == LocalizedStrings.Culture.Name) return;

        LocalizedStrings.UseCulture(culture);
        foreach (var (key, value) in LocalizedStrings.GetStrings())
        {
            if (Labels.TryGetValue(key, out var label)) label.Update(value);
            else
            {
                label = new LocalizedLabel(value);
                Labels.Add(key, label);
                Application.Current.Resources["L_" + key] = label;
            }
        }
        LanguageTag.Update(culture.Name);
    }

    internal static void Bind(FrameworkElement target, DependencyProperty property, string key) =>
        target.SetBinding(property, CreateBinding(Labels[key]));

    internal static void BindLanguage(FrameworkElement target) =>
        target.SetBinding(FrameworkElement.LanguageProperty, CreateBinding(LanguageTag));

    private static Binding CreateBinding(LocalizedLabel label) => new()
    {
        Source = label,
        Path = new PropertyPath(nameof(LocalizedLabel.Value)),
        Mode = BindingMode.OneWay
    };
}

public sealed class LocalizedLabel(string value) : INotifyPropertyChanged
{
    public string Value { get; private set; } = value;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(string value)
    {
        if (value == Value) return;
        Value = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
