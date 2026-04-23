using System.Windows;

namespace OpenQuickHost;

public partial class AddJsonExtensionWindow : Window
{
    public AddJsonExtensionWindow(string initialJson, bool isEditMode = false)
    {
        InitializeComponent();
        Title = isEditMode ? "编辑 JSON 扩展" : "添加 JSON 扩展";
        TitleText.Text = isEditMode ? "编辑单文件 JSON 扩展" : "添加单文件 JSON 扩展";
        SaveButton.Content = isEditMode ? "保存修改" : "保存扩展";
        JsonEditor.Text = initialJson;
        Loaded += (_, _) =>
        {
            JsonEditor.Focus();
            JsonEditor.SelectAll();
        };
    }

    public string JsonContent => JsonEditor.Text;

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(JsonEditor.Text))
        {
            ShowError("JSON 内容不能为空。");
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
