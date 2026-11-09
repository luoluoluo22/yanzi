using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using EmojiInline = Emoji.Wpf.EmojiInline;

namespace OpenQuickHost;

/// <summary>
/// WPF draws Segoe UI Emoji as a monochrome outline. Chat messages keep the
/// original characters for copy, and each emoji sequence is painted from the
/// system font's color layers. Surrounding text stays on the chat font.
/// </summary>
public static class AiChatEmoji
{
    public readonly record struct Segment(string Text, bool IsEmoji);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(AiChatEmoji),
            new PropertyMetadata(string.Empty, OnTextChanged));

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached(
            "Hooked",
            typeof(bool),
            typeof(AiChatEmoji),
            new PropertyMetadata(false));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) =>
        element.SetValue(TextProperty, value ?? string.Empty);

    public static bool ContainsEmoji(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var index = 0;
        while (index < text.Length)
        {
            if (TryReadCluster(text, index, out var length))
                return true;
            index += Step(text, index);
        }

        return false;
    }

    public static IReadOnlyList<Segment> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var result = new List<Segment>();
        var buffer = new StringBuilder();
        var emoji = false;
        var started = false;
        var index = 0;
        while (index < text.Length)
        {
            if (TryReadCluster(text, index, out var length))
            {
                if (!started || !emoji)
                {
                    Flush();
                    emoji = true;
                    started = true;
                }

                buffer.Append(text, index, length);
                index += length;
                continue;
            }

            var step = Step(text, index);
            if (!started || emoji)
            {
                Flush();
                emoji = false;
                started = true;
            }

            buffer.Append(text, index, step);
            index += step;
        }

        Flush();
        return result;

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            result.Add(new Segment(buffer.ToString(), emoji));
            buffer.Clear();
        }
    }

    public static void AppendRuns(InlineCollection inlines, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var segment in Split(text))
            inlines.Add(CreateInline(segment));
    }

    public static Inline CreateInline(Segment segment)
    {
        if (!segment.IsEmoji)
            return new Run(segment.Text);

        // Black keeps the palette colors. A light chat foreground would tint them.
        return new EmojiInline
        {
            Foreground = Brushes.Black,
            Text = segment.Text
        };
    }

    public static void Apply(FlowDocument? document)
    {
        if (document is null)
            return;
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        if (!ContainsEmoji(text))
            return;
        Emoji.Wpf.FlowDocumentExtensions.SubstituteGlyphs(document);
    }

    public static void Apply(TextBlock block, string? text)
    {
        text ??= string.Empty;
        var preserved = block.Inlines.Where(IsCaret).ToList();
        var content = block.Inlines.Where(inline => !IsCaret(inline)).ToList();
        if (!ContainsEmoji(text) && content.Count == 1 && content[0] is Run run)
        {
            if (run.Text != text)
                run.Text = text;
            return;
        }

        var same = !ContainsEmoji(text)
            ? false
            : SegmentsMatch(content, Split(text));
        if (same)
            return;

        foreach (var inline in content)
            block.Inlines.Remove(inline);
        if (text.Length == 0)
            return;

        var anchor = preserved.FirstOrDefault();
        foreach (var segment in Split(text))
        {
            var inline = CreateInline(segment);
            if (anchor is null)
                block.Inlines.Add(inline);
            else
                block.Inlines.InsertBefore(anchor, inline);
        }
    }

    public static string ReadPlainText(TextPointer start, TextPointer end)
    {
        var builder = new StringBuilder();
        var cursor = start;
        while (cursor != null && cursor.CompareTo(end) < 0)
        {
            switch (cursor.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var chunk = cursor.GetTextInRun(LogicalDirection.Forward);
                    var next = cursor.GetPositionAtOffset(chunk.Length, LogicalDirection.Forward);
                    if (next == null || next.CompareTo(end) > 0)
                    {
                        var count = cursor.GetOffsetToPosition(end);
                        if (count < 0)
                            return builder.ToString();
                        if (count < chunk.Length)
                            chunk = chunk[..count];
                        next = end;
                    }

                    builder.Append(chunk);
                    cursor = next;
                    break;
                case TextPointerContext.ElementStart
                    when cursor.GetAdjacentElement(LogicalDirection.Forward) is EmojiInline emoji &&
                         !string.IsNullOrEmpty(emoji.Text):
                    builder.Append(emoji.Text);
                    cursor = cursor.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                case TextPointerContext.ElementEnd when cursor.Parent is Paragraph:
                    builder.Append("\r\n");
                    cursor = cursor.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                default:
                    var step = cursor.GetNextContextPosition(LogicalDirection.Forward);
                    if (step == null || step.CompareTo(cursor) <= 0)
                        return builder.ToString();
                    cursor = step;
                    break;
            }
        }

        return builder.ToString();
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock block)
            return;

        if (block.GetValue(HookedProperty) is not true)
        {
            block.SetValue(HookedProperty, true);
            block.Loaded += OnLoaded;
        }

        Apply(block, args.NewValue as string);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is TextBlock block)
            Apply(block, GetText(block));
    }

    private static bool IsCaret(Inline inline) =>
        inline is InlineUIContainer and not EmojiInline;

    private static bool SegmentsMatch(IReadOnlyList<Inline> content, IReadOnlyList<Segment> segments)
    {
        if (content.Count != segments.Count)
            return false;
        for (var i = 0; i < segments.Count; i++)
        {
            var actual = content[i] switch
            {
                Run run => run.Text,
                EmojiInline emoji => emoji.Text,
                _ => null
            };
            if (actual != segments[i].Text)
                return false;
            if (segments[i].IsEmoji && content[i] is not EmojiInline)
                return false;
        }

        return true;
    }

    private static int Step(string text, int index) =>
        char.IsSurrogatePair(text, index) ? 2 : 1;

    private static bool TryReadRune(string text, int index, out int value, out int length)
    {
        if ((uint)index >= (uint)text.Length)
        {
            value = 0;
            length = 0;
            return false;
        }

        if (char.IsHighSurrogate(text[index]) &&
            index + 1 < text.Length &&
            char.IsLowSurrogate(text[index + 1]))
        {
            value = char.ConvertToUtf32(text[index], text[index + 1]);
            length = 2;
            return true;
        }

        value = text[index];
        length = 1;
        return true;
    }

    private static bool TryReadCluster(string text, int index, out int length)
    {
        length = 0;
        if (!TryReadRune(text, index, out var codePoint, out var codeLength))
            return false;

        if (IsKeycapBase(codePoint))
        {
            var cursor = index + codeLength;
            if (TryReadRune(text, cursor, out var next, out var nextLength) && next == 0xFE0F)
            {
                cursor += nextLength;
                if (TryReadRune(text, cursor, out var keycap, out var keycapLength) && keycap == 0x20E3)
                {
                    length = cursor + keycapLength - index;
                    return true;
                }

                return false;
            }

            if (TryReadRune(text, cursor, out var bareKeycap, out var bareLength) && bareKeycap == 0x20E3)
            {
                length = cursor + bareLength - index;
                return true;
            }

            return false;
        }

        if (IsRegional(codePoint))
        {
            var cursor = index + codeLength;
            if (TryReadRune(text, cursor, out var next, out var nextLength) && IsRegional(next))
                length = cursor + nextLength - index;
            else
                length = codeLength;
            return true;
        }

        var end = index + codeLength;
        if (!IsEmojiBase(codePoint))
        {
            if (!CanTakeVariationSelector(codePoint) ||
                !TryReadRune(text, end, out var selector, out _) ||
                selector is not (0xFE0F or 0xFE0E))
                return false;
        }

        end = ConsumeModifiers(text, end);
        while (TryReadRune(text, end, out var joiner, out var joinerLength) && joiner == 0x200D)
        {
            var after = end + joinerLength;
            if (!TryReadRune(text, after, out var nextBase, out var nextLength))
                break;
            if (!IsEmojiBase(nextBase) && !IsRegional(nextBase))
                break;
            end = ConsumeModifiers(text, after + nextLength);
        }

        while (TryReadRune(text, end, out var tag, out var tagLength) && tag is >= 0xE0020 and <= 0xE007F)
            end += tagLength;

        length = end - index;
        return length > 0;
    }

    private static int ConsumeModifiers(string text, int index)
    {
        if (TryReadRune(text, index, out var selector, out var selectorLength) && selector is 0xFE0E or 0xFE0F)
            index += selectorLength;
        if (TryReadRune(text, index, out var skin, out var skinLength) && skin is >= 0x1F3FB and <= 0x1F3FF)
            index += skinLength;
        return index;
    }

    private static bool IsKeycapBase(int codePoint) =>
        codePoint is '#' or '*' or (>= '0' and <= '9');

    private static bool IsRegional(int codePoint) =>
        codePoint is >= 0x1F1E6 and <= 0x1F1FF;

    private static bool IsEmojiBase(int codePoint) => codePoint switch
    {
        >= 0x1F300 and <= 0x1FAFF => true,
        >= 0x2600 and <= 0x27BF => true,
        0x231A or 0x231B or 0x2328 or 0x23CF => true,
        >= 0x23E9 and <= 0x23F3 => true,
        >= 0x23F8 and <= 0x23FA => true,
        0x24C2 or 0x25AA or 0x25AB or 0x25B6 or 0x25C0 => true,
        >= 0x25FB and <= 0x25FE => true,
        >= 0x2194 and <= 0x2199 => true,
        0x21A9 or 0x21AA or 0x2934 or 0x2935 => true,
        >= 0x2B05 and <= 0x2B07 => true,
        0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
        0x3030 or 0x303D or 0x3297 or 0x3299 => true,
        _ => false
    };

    private static bool CanTakeVariationSelector(int codePoint)
    {
        if (codePoint is < 0xA0 or > 0xFFFF || codePoint is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0xF900 and <= 0xFAFF)
            return false;
        if (codePoint is >= 0x3000 and <= 0x303F && codePoint is not (0x3030 or 0x303D))
            return false;
        return !char.IsWhiteSpace((char)codePoint);
    }
}
