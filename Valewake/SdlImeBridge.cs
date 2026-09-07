using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Valewake;

/// <summary>Positions SDL's native IME candidate window beside the active game text box.</summary>
internal sealed class SdlImeBridge : IDisposable
{
    private const string ImeShowUiHint = "SDL_IME_SHOW_UI";
    private bool available;
    private string? previousImeShowUiHint;
    private SdlRect lastRect;
    private bool hasLastRect;

    public SdlImeBridge(bool enabled)
    {
        if (!enabled || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            IntPtr previousHint = SdlGetHint(ImeShowUiHint);
            previousImeShowUiHint = previousHint == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(previousHint);

            SdlSetHint(ImeShowUiHint, "1");
            available = true;
        }
        catch (DllNotFoundException)
        {
            available = false;
        }
        catch (EntryPointNotFoundException)
        {
            available = false;
        }
        catch (BadImageFormatException)
        {
            available = false;
        }
    }

    public void Update(Rectangle textBoxBounds)
    {
        if (!available || Game1.game1?.Window is null)
            return;

        try
        {
            Rectangle clientBounds = Game1.game1.Window.ClientBounds;
            float scaleX = clientBounds.Width / (float)Math.Max(1, Game1.uiViewport.Width);
            float scaleY = clientBounds.Height / (float)Math.Max(1, Game1.uiViewport.Height);
            SdlRect rect = new()
            {
                X = (int)MathF.Round(textBoxBounds.X * scaleX),
                Y = (int)MathF.Round(textBoxBounds.Y * scaleY),
                Width = Math.Max(1, (int)MathF.Round(textBoxBounds.Width * scaleX)),
                Height = Math.Max(1, (int)MathF.Round(textBoxBounds.Height * scaleY))
            };

            if (hasLastRect && rect.Equals(lastRect))
                return;

            SdlSetTextInputRect(ref rect);
            lastRect = rect;
            hasLastRect = true;
        }
        catch (EntryPointNotFoundException)
        {
            available = false;
        }
    }

    public void Dispose()
    {
        if (!available)
            return;

        try
        {
            SdlSetHint(ImeShowUiHint, previousImeShowUiHint ?? "0");
        }
        catch (EntryPointNotFoundException)
        {
            // Older SDL builds simply keep their existing text-input behavior.
        }
        finally
        {
            available = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect : IEquatable<SdlRect>
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public readonly bool Equals(SdlRect other) =>
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    [DllImport("SDL2.dll", EntryPoint = "SDL_GetHint", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SdlGetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("SDL2.dll", EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SdlSetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value
    );

    [DllImport("SDL2.dll", EntryPoint = "SDL_SetTextInputRect", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SdlSetTextInputRect(ref SdlRect rect);
}
