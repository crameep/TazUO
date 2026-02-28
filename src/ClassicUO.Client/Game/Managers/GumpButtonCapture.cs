using System;

namespace ClassicUO.Game.Managers
{
    internal static class GumpButtonCapture
    {
        internal static uint LastGumpId { get; private set; }
        internal static int LastButtonId { get; private set; }
        internal static int Sequence { get; private set; }
        internal static DateTime LastCapturedAtUtc { get; private set; }

        public static void Record(uint gumpId, int buttonId)
        {
            if (buttonId == 0)
                return; // button 0 is "close gump", not a real action

            LastGumpId = gumpId;
            LastButtonId = buttonId;
            LastCapturedAtUtc = DateTime.UtcNow;
            Sequence++;
        }

        internal static void Reset()
        {
            LastGumpId = 0;
            LastButtonId = 0;
            Sequence = 0;
            LastCapturedAtUtc = default;
        }
    }
}
