using static ClipFlow.Core.LocalizedStrings;

namespace ClipFlow;

internal static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(4) ?? "1.2.0.0";
    public const string Author = "OpticsValley";
    public static string VersionLabel => "ClipFlow " + Version;
    public static string AboutVersion => L("VersionFormat", Version);
    public static string AboutAuthor => L("AuthorFormat", Author);
}
