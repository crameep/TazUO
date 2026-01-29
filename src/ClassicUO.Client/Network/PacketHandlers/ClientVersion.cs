using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.Gumps.Login;
using ClassicUO.IO;
using ClassicUO.Utility;

namespace ClassicUO.Network.PacketHandlers;

internal static class ClientVersion
{
    public static void Receive(World world, ref StackDataReader p) => AsyncNetClient.Socket.Send_ClientVersion(Settings.GlobalSettings.ClientVersion);
}
