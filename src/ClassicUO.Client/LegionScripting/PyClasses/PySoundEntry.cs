using System;
using ClassicUO.Game.Managers;

namespace ClassicUO.LegionScripting.PyClasses;

public class PySoundEntry(SoundEventArgs entry)
{
    public int ID = entry.Index;
    public int X = entry.X;
    public int Y = entry.Y;
    public DateTime Time = entry.Time;
}
