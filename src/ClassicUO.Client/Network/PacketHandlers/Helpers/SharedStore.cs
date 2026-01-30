using System.Collections.Generic;
using System.Linq;

namespace ClassicUO.Network.PacketHandlers.Helpers;

internal static class SharedStore
{
    private static readonly List<uint> _cliLocRequests = [];
    private static readonly List<uint> _customHouseRequests = [];

    public static uint RequestedGridLoot { get; set; }

    public static void AddMegaCliLocRequest(uint serial)
    {
        if (_cliLocRequests.Any(s => s == serial))
            return;

        _cliLocRequests.Add(serial);
    }

    public static void AddCustomHouseRequest(uint serial)
    {
        if (_customHouseRequests.Any(s => s == serial))
            return;

        _customHouseRequests.Add(serial);
    }
}
