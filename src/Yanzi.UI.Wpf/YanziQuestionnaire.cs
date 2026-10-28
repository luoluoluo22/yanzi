using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

public enum YanziQuestionKind { Text, Choice, YesNo, MultipleChoice }

public sealed record YanziQuestion(string Id, string Title, YanziQuestionKind Kind,
    IReadOnlyList<string>? Options = null, bool Required = true, bool AllowSkip = false,
    string? DependsOnId = null, string? DependsOnValue = null);

/// <summary>
/// In-memory native WPF multi-step questionnaire: text, single and multiple
/// choices, optional skip, conditional questions, and previous/next navigation.
/// Hosts decide how or whether answers are persisted.
/// </summary>
public sealed class YanziQuestionnaire : UserControl
{
    private readonly IReadOnlyList<YanziQuestion> _questions;
    private readonly Dictionary<string, string> _answers = new();
    private readonly StackPanel _root = new();
    private readonly List<CheckBox> _multiChoices = new();
    private readonly List<RadioButton> _radioChoices = new();
    private int _index;
    private TextBox? _input;

    public event EventHandler<IReadOnlyDictionary<string, string>>? Completed;
    public IReadOnlyDictionary<string, string> Answers => _answers;
    public int CurrentIndex => _index;
    public int QuestionCount => _questions.Count;

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

    private bool IsApplicable(int index)
    {
        var q = _questions[index];
        if (q.DependsOnId is null) return true;
        return _answers.TryGetValue(q.DependsOnId, out var earlier) &&
            (q.DependsOnValue is null || earlier.Equals(q.DependsOnValue, StringComparison.Ordinal));
    }

    private void PruneInapplicableAnswers()
    {
        foreach (var question in _questions)
        {
            if (question.DependsOnId is not null && !IsApplicable(
                Array.IndexOf(_questions.ToArray(), question)))
                _answers.Remove(question.Id);
        }
    }

    public bool Next()
    {
        if (!SaveCurrent()) return false;
        PruneInapplicableAnswers();
        var next = Enumerable.Range(_index + 1, _questions.Count - _index - 1)
            .FirstOrDefault(i => IsApplicable(i), -1);
        if (next >= 0)
        {
            _index = next;
            ShowQuestion();
            return true;
        }
        Completed?.Invoke(this, new Dictionary<string, string>(_answers));
        return true;
    }

    public bool Skip()
    {
        if (!_questions[_index].AllowSkip) return false;
        _answers.Remove(_questions[_index].Id);
        var next = Enumerable.Range(_index + 1, _questions.Count - _index - 1)
            .FirstOrDefault(i => IsApplicable(i), -1);
        if (next >= 0)
        {
            _index = next;
            ShowQuestion();
        }
        else Completed?.Invoke(this, new Dictionary<string, string>(_answers));
        return true;
    }

    public void Previous()
    {
        SaveCurrent(false);
        var previous = Enumerable.Range(0, _index).Reverse().FirstOrDefault(i => IsApplicable(i), -1);
        if (previous >= 0) { _index = previous; ShowQuestion(); }
    }

    private bool SaveCurrent(bool validate = true)
    {
        var question = _questions[_index];
        string value;
        if (question.Kind == YanziQuestionKind.Text)
            value = (_input?.Text ?? "").Trim();
        else if (question.Kind == YanziQuestionKind.MultipleChoice)
            value = string.Join(";", _multiChoices.Where(c => c.IsChecked == true)
                .Select(c => c.Content?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        else
            value = _radioChoices.FirstOrDefault(r => r.IsChecked == true)?.Content?.ToString() ?? "";
        if (validate && question.Required && string.IsNullOrWhiteSpace(value)) return false;
        if (string.IsNullOrWhiteSpace(value)) _answers.Remove(question.Id);
        else _answers[question.Id] = value;
        return true;
    }

    private void ShowQuestion()
    {
        _root.Children.Clear();
        _input = null;
        _radioChoices.Clear();
        _multiChoices.Clear();

        var question = _questions[_index];
        var progress = new TextBlock
        {
            Text = $"Question {_index + 1} of {_questions.Count}",
            FontSize = 12, Margin = new Thickness(0, 0, 0, 9)
        };
        progress.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        _root.Children.Add(progress);
        var title = new TextBlock
        {
            Text = question.Title, FontSize = 15, TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        _root.Children.Add(title);

        if (question.Kind == YanziQuestionKind.Text)
        {
            _input = YanziUi.WithStyle(new TextBox
            {
                Text = _answers.GetValueOrDefault(question.Id, ""),
                MinWidth = 230, MaxWidth = 460
            }, YanziUi.Styles.Input);
            AutomationProperties.SetName(_input, question.Title);
            _root.Children.Add(_input);
        }
        else
        {
            var options = question.Kind == YanziQuestionKind.YesNo
                ? new[] { "是", "否" } : question.Options?.ToArray() ?? [];
            if (options.Length == 0) throw new InvalidOperationException("Choice question requires options.");
            var saved = _answers.GetValueOrDefault(question.Id, "");
            var selected = new HashSet<string>(saved.Split(';', StringSplitOptions.RemoveEmptyEntries));
            foreach (var value in options)
            {
                if (question.Kind == YanziQuestionKind.MultipleChoice)
                {
                    var checkbox = YanziUi.WithStyle(new CheckBox
                    {
                        Content = value, IsChecked = selected.Contains(value),
                        Margin = new Thickness(0, 0, 0, 9)
                    }, YanziUi.Styles.CheckBoxPreview);
                    _multiChoices.Add(checkbox);
                    _root.Children.Add(checkbox);
                }
                else
                {
                    var radio = YanziUi.WithStyle(new RadioButton
                    {
                        Content = value, GroupName = "YanziQuestion_" + question.Id,
                        IsChecked = value == saved, Margin = new Thickness(0, 0, 0, 9)
                    }, YanziUi.Styles.Radio);
                    _radioChoices.Add(radio);
                    _root.Children.Add(radio);
                }
            }
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0)
        };
        var previous = YanziUi.WithStyle(new Button
        {
            Content = "Previous", IsEnabled = _index > 0,
            Margin = new Thickness(0, 0, 7, 0)
        }, YanziUi.Styles.OutlineButton);
        previous.Click += (_, _) => Previous();
        actions.Children.Add(previous);
        if (question.AllowSkip)
        {
            var skip = YanziUi.WithStyle(new Button
            {
                Content = "Skip", Margin = new Thickness(0, 0, 7, 0)
            }, YanziUi.Styles.GhostButton);
            skip.Click += (_, _) => Skip();
            actions.Children.Add(skip);
        }
        var finalQuestion = !Enumerable.Range(_index + 1, _questions.Count - _index - 1)
            .Any(i => IsApplicable(i));
        var next = YanziUi.WithStyle(new Button
        {
            Content = finalQuestion ? "Save answers" : "Next"
        }, YanziUi.Styles.DefaultButton);
        next.Click += (_, _) => Next();
        actions.Children.Add(next);
        _root.Children.Add(actions);
    }
}
