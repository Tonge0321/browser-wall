using System.Text;
using System.Windows;
using BrowserWall.Core;
using BrowserWall.Core.Config;
using BrowserWall.Core.Session;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

public partial class App : Application
{
    private const string Title = "BrowserWall";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var env = new RealConfigEnvironment();
        ConfigLoadResult load = ConfigLoader.Load(env);

        // The UI language is decided once here and does not change while running.
        UiLanguage lang = load.Parse.Language;

        if (load.CreatedDefault)
        {
            MessageBox.Show(
                UiStrings.Format(lang, UiTextKey.ConfigCreated, load.ConfigPath, WallLimits.MaxCells),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (!load.Parse.Success)
        {
            var sb = new StringBuilder();
            sb.AppendLine(UiStrings.Get(lang, UiTextKey.ConfigInvalidHeader));
            sb.AppendLine(load.ConfigPath);
            sb.AppendLine();
            foreach (ConfigError error in load.Parse.Errors)
            {
                string field = error.Field == ConfigError.WholeFile ? UiStrings.Get(lang, UiTextKey.ConfigWholeFile) : error.Field;
                sb.AppendLine(UiStrings.Format(lang, UiTextKey.ConfigErrorLine, field, error.Message));
            }

            MessageBox.Show(sb.ToString(), Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // URLs to open: the last page list; the config file when there is no usable list.
        var store = new SessionStore(env);
        SessionLoadResult session = SessionLoader.Load(store, load.Parse.Config!.Urls);
        if (session.ShowInvalidNotice)
        {
            MessageBox.Show(
                $"{UiStrings.Get(lang, UiTextKey.SessionInvalid)}\n{store.FilePath}",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var tracker = new SessionTracker(store, session.Urls);
        var window = new MainWindow(load.Parse.Config!, load.ConfigPath, session.Urls, tracker, lang);
        MainWindow = window;
        window.Show();
    }
}
