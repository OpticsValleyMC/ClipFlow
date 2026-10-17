namespace ClipFlow.Core;

// Bound the text presented to a UI control without truncating the stored/copyable content.
public sealed class TextPages
{
    private readonly string _text;
    private readonly List<int> _starts = [0];
    public int Count => _starts.Count;
    public int Length => _text.Length;
    public TextPages(string text, int pageSize = 16_000)
    {
        if (pageSize < 2) throw new ArgumentOutOfRangeException(nameof(pageSize));
        _text = text;
        var start = 0;
        while (_text.Length - start > pageSize)
        {
            var end = start + pageSize;
            if ((char.IsHighSurrogate(_text[end - 1]) && char.IsLowSurrogate(_text[end]))
                || (_text[end - 1] == '\r' && _text[end] == '\n')) end--;
            _starts.Add(end);
            start = end;
        }
    }
    public string GetPage(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        var end = index + 1 < Count ? _starts[index + 1] : _text.Length;
        return _text[_starts[index]..end];
    }
    public static string Preview(string text, int limit = 512)
    {
        if (text.Length <= limit) return text;
        if (char.IsHighSurrogate(text[limit - 1]) && char.IsLowSurrogate(text[limit])) limit--;
        return text[..limit] + "…";
    }
}
