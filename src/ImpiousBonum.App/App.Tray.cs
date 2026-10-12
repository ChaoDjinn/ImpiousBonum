using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App;

// The tray: what its few menu items do, and the actions buttons on the dashboard ask for.
public partial class App
{
    /// <summary>What each tray menu item does, and filling in the layouts each time the menu opens.</summary>
    private void ConnectTray(TrayIcon tray)
    {
        tray.MenuOpening += (_, _) =>
        {
            if (_layouts is not null)
                tray.SetLayouts(_layouts.List(), _layouts.Active);
        };
        tray.LayoutSelected += (_, name) => SelectLayout(name);
        tray.OpenRequested += (_, _) => ShowSettings();
        tray.EditLayoutRequested += (_, _) => OpenEditor();
        tray.RestartToUpdateRequested += async (_, _) => await RestartToUpdateAsync();
        tray.ExitRequested += async (_, _) => await ExitAsync();
    }

    /// <summary>A tap on a dashboard button or media control. Problems show as a notification, since the tap gave no other feedback.</summary>
    private void RunAction(WidgetAction action)
    {
        if (action.Kind == WidgetAction.Notify)
        {
            _tray?.ShowAlert(action.Target);
            return;
        }
        if (_actions?.Run(action) is not { } problem)
            return;
        AppLog.Warning($"Button ({action.Kind}): {problem}");
        _tray?.ShowError("Impious Bonum", problem);
    }

    private string? SwitchLayoutFromButton(string name)
    {
        if (_layouts?.Library.Find(name) is not { } found)
            return $"There's no layout called \"{name}\". Check the button's target in the editor.";
        SelectLayout(found);
        return null;
    }
}
