namespace ClipFlow;

internal static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(4) ?? "1.1.0.0";
    public const string Author = "OpticsValley";
    public static string VersionLabel => "ClipFlow " + Version;
    public static string AboutVersion => "版本 " + Version;
    public static string AboutAuthor => "作者 " + Author;
}
