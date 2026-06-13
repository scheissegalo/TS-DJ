using Avalonia.Input;

namespace TS_DJ.App.ViewModels;

internal static class HotkeyFormatter
{
    public static string Format(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Meta");

        parts.Add(NormalizeKey(key));
        return string.Join('+', parts);
    }

    private static string NormalizeKey(Key key) =>
        key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            _ => key.ToString()
        };
}
