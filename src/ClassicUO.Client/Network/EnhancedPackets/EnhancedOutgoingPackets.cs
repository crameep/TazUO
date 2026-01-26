using System.Collections.Generic;
using ClassicUO.IO;

namespace ClassicUO.Network;

internal static class EnhancedOutgoingPackets
{
    public static HashSet<EnhancedPacketType> EnabledPackets = new();
    public static void SendEnhancedPacket(this AsyncNetClient socket)
    {
        EnhancedPacketType id = EnhancedPacketType.EnableEnhancedPacket;

        if (!EnabledPackets.Contains(id))
            return;
        
        StackDataWriter writer = Extensions.GetWriter(id);
        writer.FinalLength();
        socket.Send(writer.BufferWritten);
    }
}