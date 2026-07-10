using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Winotch.CommandBar;

public partial class CommandBarPanel : UserControl
{
    public CommandBarPanel()
    {
        InitializeComponent();
    }

    public TextBox InputBox => CommandInputBox;
    public ListView ResultsList => CommandResultsList;

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
        CommandInputBox.Focus(FocusState.Programmatic);
        CommandInputBox.SelectionStart = CommandInputBox.Text.Length;
        CommandInputBox.SelectionLength = 0;
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
