using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

public enum YanziQuestionKind { Text, Choice, YesNo }

public sealed record YanziQuestion(string Id, string Title, YanziQuestionKind Kind,
    IReadOnlyList<string>? Options = null, bool Required = true);

/// <summary>Native questionnaire wizard. Answers exist only in memory unless the host saves them.</summary>
public sealed class YanziQuestionnaire : UserControl
{
    private readonly IReadOnlyList<YanziQuestion> _questions;
    private readonly Dictionary<string, string> _answers = new();
    private readonly StackPanel _root = new();
    private int _index;
    private TextBox? _input;
    private ComboBox? _choice;

    public event EventHandler<IReadOnlyDictionary<string, string>>? Completed;
    public IReadOnlyDictionary<string, string> Answers => _answers;
    public int CurrentIndex => _index;

    public YanziQuestionnaire(IReadOnlyList<YanziQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0 || questions.Any(q => string.IsNullOrWhiteSpace(q.Id)
            || string.IsNullOrWhiteSpace(q.Title)))
            throw new ArgumentException("Questions must have non-empty identifiers and titles.", nameof(questions));
        if (questions.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            throw new ArgumentException("Question IDs must be unique.", nameof(questions));
        _questions = questions;
        Content = _root;
        ShowQuestion();
    }

    public bool Next()
    {
        if (!SaveCurrent()) return false;
        if (_index + 1 < _questions.Count)
        {
            _index++;
            ShowQuestion();
            return true;
        }
        Completed?.Invoke(this, new Dictionary<string, string>(_answers));
        return true;
    }

    public void Previous()
    {
        SaveCurrent(false);
        if (_index > 0) { _index--; ShowQuestion(); }
    }

    private bool SaveCurrent(bool validate = true)
    {
        var current = _questions[_index];
        var value = (_input?.Text ?? _choice?.SelectedItem?.ToString() ?? "").Trim();
        if (validate && current.Required && string.IsNullOrWhiteSpace(value)) return false;
        _answers[current.Id] = value;
        return true;
    }

    private void ShowQuestion()
    {
        _root.Children.Clear();
        _input = null;
        _choice = null;
        var question = _questions[_index];
        var progress = new TextBlock { Text = $"问题 {_index + 1} / {_questions.Count}", FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8) };
        progress.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        _root.Children.Add(progress);
        var title = new TextBlock { Text = question.Title, FontSize = 14,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        _root.Children.Add(title);

        if (question.Kind == YanziQuestionKind.Text)
        {
            _input = YanziUi.WithStyle(new TextBox { Text = _answers.GetValueOrDefault(question.Id, "") },
                YanziUi.Styles.Input);
            _root.Children.Add(_input);
        }
        else
        {
            var options = question.Kind == YanziQuestionKind.YesNo
                ? new[] { "是", "否" }
                : question.Options?.ToArray() ?? Array.Empty<string>();
            if (options.Length == 0) throw new InvalidOperationException("Choice question requires options.");
            _choice = YanziUi.WithStyle(new ComboBox { ItemsSource = options }, YanziUi.Styles.Select);
            var saved = _answers.GetValueOrDefault(question.Id);
            if (saved is not null) _choice.SelectedItem = saved;
            _root.Children.Add(_choice);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var back = YanziUi.WithStyle(new Button { Content = "上一步", IsEnabled = _index > 0,
            Margin = new Thickness(0, 0, 8, 0) }, YanziUi.Styles.OutlineButton);
        back.Click += (_, _) => Previous();
        var next = YanziUi.WithStyle(new Button { Content = _index + 1 == _questions.Count ? "完成" : "下一步" },
            YanziUi.Styles.DefaultButton);
        next.Click += (_, _) => Next();
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        _root.Children.Add(buttons);
    }
}
