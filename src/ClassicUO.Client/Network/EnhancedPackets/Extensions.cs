using System.IO;
using ClassicUO.IO;
using ClassicUO.Network;

public static class Extensions
{
    public static StackDataWriter GetWriter(EnhancedPacketType type)
    {
        StackDataWriter writer = new(3);
        writer.SetHeader(type);
        return writer;
    }

    private static void SetHeader(this StackDataWriter writer, EnhancedPacketType type)
    {
        writer.WriteUInt8(EnhancedPacketHandler.EPID); //Enhanced Packet ID
        writer.WriteZero(2); //Length - will update later
        writer.WriteUInt16BE((ushort)type); //Packet ID;
    }

    /// <summary>
    /// Set the length byte after writing all the data.
    /// </summary>
    public static void FinalLength(this StackDataWriter writer)
    {
        writer.Seek(1, SeekOrigin.Begin);
        writer.WriteUInt16BE((ushort)writer.BytesWritten);
    }
}