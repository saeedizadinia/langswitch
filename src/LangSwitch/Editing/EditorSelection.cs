using System.Windows.Automation.Text;

namespace LangSwitch.Editing;

internal enum SelectionKind
{
    Unavailable,
    Rejected,
    Text
}

internal sealed record EditorSelection(int[]? RuntimeId, SelectionKind Kind, string? Text, TextPatternRange? Range = null)
{
    public bool Matches(EditorSelection? other)
    {
        if (other is null || Kind != other.Kind || Text != other.Text)
            return false;

        bool sameElement = RuntimeId is null
            ? other.RuntimeId is null
            : other.RuntimeId is not null && RuntimeId.AsSpan().SequenceEqual(other.RuntimeId);
        return sameElement && (Range is null || (other.Range is not null && Range.Compare(other.Range)));
    }
}
