using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenQuickHost;

/// <summary>
/// Editable secret with an inline eye button. PasswordBox is the default visual and
/// automation surface. Plaintext is created in TextBox only while explicitly revealed.
/// </summary>
public partial class SecretValueEditor : System.Windows.Controls.UserControl
{
    private bool _isSynchronizing;
    private bool _isRevealed;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value), typeof(string), typeof(SecretValueEditor),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value ?? string.Empty);
    }

    /// <summary>Fired only after an actual user edit, not after binding initialization or eye clicks.</summary>
    public event EventHandler? ValueEdited;

    public SecretValueEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HideValue();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                HideValue();
        };
        SyncInputs();
    }

    private static void OnValueChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        ((SecretValueEditor)obj).SyncInputs();
    }

    private void SyncInputs()
    {
        if (MaskedInput is null || RevealedInput is null || _isSynchronizing)
            return;

        _isSynchronizing = true;
        try
        {
            var current = Value ?? string.Empty;
            if (MaskedInput.Password != current)
                MaskedInput.Password = current;
            var visibleText = _isRevealed ? current : string.Empty;
            if (RevealedInput.Text != visibleText)
                RevealedInput.Text = visibleText;
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void MaskedInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizing || _isRevealed)
            return;

        SetCurrentValue(ValueProperty, MaskedInput.Password);
        ValueEdited?.Invoke(this, EventArgs.Empty);
    }

    private void RevealedInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSynchronizing || !_isRevealed)
            return;

        SetCurrentValue(ValueProperty, RevealedInput.Text);
        ValueEdited?.Invoke(this, EventArgs.Empty);
    }

    private void EyeButton_Click(object sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        UpdateRevealState();
        if (_isRevealed)
        {
            RevealedInput.Focus();
            RevealedInput.CaretIndex = RevealedInput.Text.Length;
        }
        else
        {
            MaskedInput.Focus();
        }
    }

    private void HideValue()
    {
        if (!_isRevealed)
            return;
        _isRevealed = false;
        UpdateRevealState();
    }

    private void UpdateRevealState()
    {
        SyncInputs();
        MaskedInput.Visibility = _isRevealed ? Visibility.Collapsed : Visibility.Visible;
        RevealedInput.Visibility = _isRevealed ? Visibility.Visible : Visibility.Collapsed;
        ShowEyeIcon.Visibility = _isRevealed ? Visibility.Collapsed : Visibility.Visible;
        HideEyeIcon.Visibility = _isRevealed ? Visibility.Visible : Visibility.Collapsed;
        EyeButton.ToolTip = _isRevealed ? "隐藏内容" : "显示内容";
        System.Windows.Automation.AutomationProperties.SetName(
            EyeButton, _isRevealed ? "隐藏内容" : "显示内容");
    }
}
