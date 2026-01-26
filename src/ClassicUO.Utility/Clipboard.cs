#nullable enable
using SDL3;

namespace ClassicUO.Utility;

public static class Clipboard
{
    public static string? GetClipboardText()
    {
        if (SDL.SDL_HasClipboardText() != false)
        {
            return SDL.SDL_GetClipboardText() ?? null;
        }

        return null;
    }

    public static void SetClipboardText(string text) => SDL.SDL_SetClipboardText(text);
}

public static partial class Extensions
{
    public static void CopyToClipboard(this string text) => Clipboard.SetClipboardText(text);
}
