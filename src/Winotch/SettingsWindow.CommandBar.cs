using Microsoft.UI.Xaml;

namespace Winotch;

public partial class SettingsWindow
{
    private void CommandBarSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            CommandBar = settings.CommandBar with
            {
                Enabled = CommandBarEnabledToggle.IsOn,
                Hotkey = CommandBarHotkeyTextBox.Text,
                AppLauncherEnabled = CommandBarAppsToggle.IsOn,
                WindowSwitcherEnabled = CommandBarWindowsToggle.IsOn,
                CalculatorEnabled = CommandBarCalculatorToggle.IsOn,
                UnitConverterEnabled = CommandBarUnitsToggle.IsOn,
                QuickCommandsEnabled = CommandBarQuickCommandsToggle.IsOn
            }
        });
    }
}

