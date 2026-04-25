using System.Windows;
using System.Windows.Controls;

namespace OpenQuickHost;

public partial class SkillExportOptionsWindow : Window
{
    public SkillExportOptionsWindow()
    {
        InitializeComponent();
    }

    public SkillExportTarget SelectedTarget =>
        TargetComboBox.SelectedItem is ComboBoxItem item &&
        Enum.TryParse<SkillExportTarget>(item.Tag?.ToString(), out var target)
            ? target
            : SkillExportTarget.Codex;

    public SkillExportScope SelectedScope =>
        ScopeComboBox.SelectedItem is ComboBoxItem item &&
        Enum.TryParse<SkillExportScope>(item.Tag?.ToString(), out var scope)
            ? scope
            : SkillExportScope.Project;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
