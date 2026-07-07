using System.Windows;

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
                Enabled = CommandBarEnabledToggle.IsChecked == true,
                Hotkey = CommandBarHotkeyTextBox.Text,
                AppLauncherEnabled = CommandBarAppsToggle.IsChecked == true,
                WindowSwitcherEnabled = CommandBarWindowsToggle.IsChecked == true,
                CalculatorEnabled = CommandBarCalculatorToggle.IsChecked == true,
                UnitConverterEnabled = CommandBarUnitsToggle.IsChecked == true,
                QuickCommandsEnabled = CommandBarQuickCommandsToggle.IsChecked == true
            }
        });
    }
}

