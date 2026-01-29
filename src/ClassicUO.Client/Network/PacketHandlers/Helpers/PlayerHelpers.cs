using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Network.PacketHandlers.Helpers;

internal static class PlayerHelpers
{
    public static void UpdatePlayer(
        World world,
        uint serial,
        ushort graphic,
        byte graph_inc,
        ushort hue,
        Flags flags,
        ushort x,
        ushort y,
        sbyte z,
        ushort serverID,
        Direction direction
    )
    {
        if (serial == world.Player)
        {
            world.RangeSize.X = x;
            world.RangeSize.Y = y;

            bool olddead = world.Player.IsDead;
            ushort old_graphic = world.Player.Graphic;

            world.Player.CloseBank();
            world.Player.Walker.WalkingFailed = false;
            world.Player.Graphic = graphic;
            world.Player.Direction = direction & Direction.Mask;
            world.Player.FixHue(hue);
            world.Player.Flags = flags;
            world.Player.Walker.DenyWalk(0xFF, -1, -1, -1);

            GameScene gs = Client.Game.GetScene<GameScene>();

            if (gs != null)
            {
                world.Weather.Reset();
                gs.UpdateDrawPosition = true;
            }

            // std client keeps the target open!
            /*if (old_graphic != 0 && old_graphic != world.Player.Graphic)
            {
                if (world.Player.IsDead)
                {
                    TargetManager.Reset();
                }
            }*/

            if (olddead != world.Player.IsDead)
            {
                if (world.Player.IsDead)
                    world.ChangeSeason(Season.Desolation, 42);
                else
                    world.ChangeSeason(world.OldSeason, world.OldMusicIndex);
            }

            world.Player.Walker.ResendPacketResync = false;
            world.Player.CloseRangedGumps();
            world.Player.SetInWorldTile(x, y, z);
            world.Player.UpdateAbilities();
        }
    }
}
