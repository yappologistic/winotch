using WpfControls = System.Windows.Controls;

namespace Winotch.CommandBar;

public partial class CommandBarPanel : WpfControls.UserControl
{
    public CommandBarPanel()
    {
        InitializeComponent();
    }

    public WpfControls.TextBox InputBox => CommandInputBox;
    public WpfControls.ListBox ResultsList => CommandResultsList;

    public CommandBarResult? SelectedResult => CommandResultsList.SelectedItem as CommandBarResult;

    public void SetResults(IReadOnlyList<CommandBarResult> results)
    {
        CommandResultsList.ItemsSource = results;
        CommandResultsList.SelectedIndex = results.Count == 0 ? -1 : 0;
    }

    public void Clear()
    {
        CommandInputBox.Text = "";
        SetResults([]);
    }

    public void FocusInput()
    {
        CommandInputBox.Focus();
        CommandInputBox.CaretIndex = CommandInputBox.Text.Length;
    }

    public void SelectNext(int delta)
    {
        var count = CommandResultsList.Items.Count;
        if (count == 0)
        {
            return;
        }

        var current = CommandResultsList.SelectedIndex < 0 ? 0 : CommandResultsList.SelectedIndex;
        CommandResultsList.SelectedIndex = (current + delta + count) % count;
        CommandResultsList.ScrollIntoView(CommandResultsList.SelectedItem);
    }
}
