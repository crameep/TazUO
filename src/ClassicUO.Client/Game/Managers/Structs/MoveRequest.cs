using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers.Structs;

public readonly struct MoveRequest(uint serial, uint destination, ushort amount = 0, int x = 0xFFFF, int y = 0xFFFF, int z = 0, Layer layer = Layer.Invalid)
{
    public uint Serial { get; } = serial;

    /// <summary>
    /// Set to uint.MaxValue to try to equip this instead of moving it.
    /// </summary>
    public uint Destination { get; } = destination;
    public ushort Amount { get; } = amount;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Z { get; } = z;

    public Layer Layer { get; } = layer;

    public void Execute()
    {
        AsyncNetClient.Socket.Send_PickUpRequest(Serial, Amount);

        if(Destination != uint.MaxValue)
            GameActions.DropItem(Serial, X, Y, Z, Destination, true);
        else
            AsyncNetClient.Socket.Send_EquipRequest(Serial, Layer, World.Instance.Player);
    }

    public static MoveRequest? ToLootBag(uint serial)
    {
        if (World.Instance.Items.TryGetValue(serial, out Item item))
        {
            Item backpack = World.Instance.Player.Backpack;

            if (backpack == null) return null;

            uint bag = ProfileManager.CurrentProfile.GrabBagSerial == 0 ? backpack.Serial : ProfileManager.CurrentProfile.GrabBagSerial;

            return new(item.Serial, bag, item.Amount);
        }

        return null;
    }

    public static MoveRequest? EquipItem(uint serial, Layer layer)
    {
        Item i = World.Instance.Items.Get(serial);

        if (i == null) return null;

        return new MoveRequest(serial, uint.MaxValue, 1, 0xFFFF, 0xFFFF, 0, layer);
    }
}

public static class MoveRequestExtensions
{
    public static MoveRequest? ToLootBag(this Item item) => MoveRequest.ToLootBag(item.Serial);

    public static ObjectActionQueueItem FromMoveRequest(this MoveRequest moveRequest) => new(moveRequest.Execute);
}
