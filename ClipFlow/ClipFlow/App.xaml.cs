using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Path = System.IO.Path;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ClipFlow
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private Microsoft.Windows.AppLifecycle.AppInstance? _instance;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
#if DEBUG
            UnhandledException += (_, args) =>
            {
                var testRoot = Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT");
                if (testRoot is null) return;
                try
                {
                    Directory.CreateDirectory(testRoot);
                    File.WriteAllText(System.IO.Path.Combine(testRoot, "unhandled-error.txt"), args.Exception.ToString());
                }
                catch { }
            };
#endif
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
#if DEBUG
                // Only the separately registered acceptance identity accepts diagnostic activation arguments.
                var activationData = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Data;
                var activationArguments = activationData is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch ? launch.Arguments : args.Arguments;
                const string testPrefix = "--clipflow-test=";
                if (activationArguments.StartsWith(testPrefix, StringComparison.Ordinal)
                    && Windows.ApplicationModel.Package.Current.Id.Name == "OpticsValley.ClipFlow.StartupAcceptance")
                {
                    var payload = System.Text.Json.JsonSerializer.Deserialize<AcceptanceLaunch>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(activationArguments[testPrefix.Length..])))!;
                    var root = Path.GetFullPath(payload.Root);
                    var allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")) + Path.DirectorySeparatorChar;
                    if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || payload.Mode is not ("startup-check" or "background-check"))
                        throw new InvalidDataException("验收激活参数不属于当前独立包。");
                    Environment.SetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT", root);
                    Environment.SetEnvironmentVariable("CLIPFLOW_SELF_TEST", payload.Mode);
                    if (payload.Mode == "background-check")
                    {
                        var fixture = new ClipFlow.Core.HistoryStore(root);
                        await fixture.InitializeAsync();
                        await fixture.SaveSettingsAsync(new ClipFlow.Core.AppSettings(Backdrop: ClipFlow.Core.BackdropLevel.Subtle));
                    }
                }
#endif
                var key = "ClipFlow.CurrentUser";
#if DEBUG
                // Isolated tests must never activate or alter the user's normal instance.
                var testRoot = Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT");
                if (testRoot is not null) key += "." + ClipFlow.Core.ContentIdentity.TextHash(testRoot);
#endif
                _instance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey(key);
                if (!_instance.IsCurrent)
                {
                    await _instance.RedirectActivationToAsync(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
                    Exit();
                    return;
                }
                _instance.Activated += (_, activation) =>
                {
                    if (activation.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask)
                        _window?.DispatcherQueue.TryEnqueue(() => (_window as MainWindow)?.RestoreWindow());
                };
                _window = new MainWindow();
                var startupActivation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
#if DEBUG
                var backgroundTest = Environment.GetEnvironmentVariable("CLIPFLOW_SELF_TEST") == "background-check"
                    && Environment.GetEnvironmentVariable("CLIPFLOW_TEST_DATA_ROOT") is not null;
                startupActivation |= backgroundTest;
#endif
                if (startupActivation)
                {
                    await ((MainWindow)_window).StartInBackgroundAsync();
#if DEBUG
                    if (backgroundTest) await ((MainWindow)_window).CheckBackgroundForTestAsync();
#endif
                }
                else _window.Activate();
            }
            catch
            {
                if (_instance is { IsCurrent: false }) { Exit(); return; }
                // Registration failure must not prevent access to the local history.
                if (_window is null) { _window = new MainWindow(); _window.Activate(); }
            }
        }
#if DEBUG
        private sealed record AcceptanceLaunch(string Mode, string Root);
#endif
    }
}
