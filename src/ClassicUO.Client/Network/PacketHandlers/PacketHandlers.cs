// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.IO;
using ClassicUO.Network;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using ClassicUO.Utility.Platforms;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ClassicUO.Game.UI.Gumps.Login;
using ClassicUO.Game.UI.Gumps.GridHighLight;
using ClassicUO.LegionScripting;
using Constants = ClassicUO.Game.Constants;

namespace ClassicUO.Network;
sealed class PacketHandlers
{
    public delegate void OnPacketBufferReader(World world, ref StackDataReader p);

    private static uint _requestedGridLoot;

    [ThreadStatic]
    private static TextFileParser _parser;
    [ThreadStatic]
    private static TextFileParser _cmdparser;

    private static TextFileParser GetParser()
    {
        if (_parser == null)
        {
            _parser = new TextFileParser(
                string.Empty,
                new[] { ' ' },
                new char[] { },
                new[] { '{', '}' }
            );
        }
        return _parser;
    }

    private static TextFileParser GetCmdParser()
    {
        if (_cmdparser == null)
        {
            _cmdparser = new TextFileParser(
                string.Empty,
                new[] { ' ', ',' },
                new char[] { },
                new[] { '@', '@' }
            );
        }
        return _cmdparser;
    }

    private List<uint> _clilocRequests = new List<uint>();
    private List<uint> _customHouseRequests = new List<uint>();
    private readonly OnPacketBufferReader[] _handlers = new OnPacketBufferReader[0x100];

    public static PacketHandlers Handler { get; } = new PacketHandlers();

    public void Add(byte id, OnPacketBufferReader handler) => _handlers[id] = handler;

    // Increased from 4096 to 65536 (64KB) to reduce Array.Resize frequency during packet processing
    private byte[] _readingBuffer = new byte[65536];
    private readonly PacketLogger _packetLogger = new PacketLogger();
    private readonly CircularBuffer _buffer = new CircularBuffer();
    private readonly CircularBuffer _pluginsBuffer = new CircularBuffer();

    public int ParsePackets(World world, Span<byte> data)
    {
        Profiler.EnterContext("APPEND");
        Append(data, false);
        Profiler.ExitContext("APPEND");

#if DEBUG
        string packet = _buffer == null || _buffer.Length == 0 ? "0xFF" : _buffer[0].ToString();

        Profiler.EnterContext(packet);
#endif

        int c = ParsePackets(world, _buffer, true) + ParsePackets(world, _pluginsBuffer, false);

#if DEBUG
        Profiler.ExitContext(packet);
#endif

        return c;
    }

    private int ParsePackets(World world, CircularBuffer stream, bool allowPlugins)
    {
        int packetsCount = 0;

        lock (stream)
        {
            ref byte[] packetBuffer = ref _readingBuffer;

            while (stream.Length > 0)
            {
                if (
                    !GetPacketInfo(
                        stream,
                        stream.Length,
                        out byte packetID,
                        out int offset,
                        out int packetlength
                    )
                )
                {
                    Log.Warn(
                        $"Invalid ID: {packetID:X2} | off: {offset} | len: {packetlength} | stream.pos: {stream.Length}"
                    );

                    break;
                }

                if (stream.Length < packetlength)
                {
                    Log.Warn(
                        $"need more data ID: {packetID:X2} | off: {offset} | len: {packetlength} | stream.pos: {stream.Length}"
                    );

                    // need more data
                    break;
                }

                while (packetlength > packetBuffer.Length)
                {
                    Profiler.EnterContext("PACKET_BUFFER_RESIZE");
                    int oldSize = packetBuffer.Length;
                    int newSize = packetBuffer.Length * 2;

                    Log.Warn($"PacketHandler buffer resize from {oldSize} to {newSize} for packet length {packetlength} (may cause spike)");

                    Array.Resize(ref packetBuffer, newSize);
                    Profiler.ExitContext("PACKET_BUFFER_RESIZE");
                }

                _ = stream.Dequeue(packetBuffer, 0, packetlength);

                PacketLogger.Default?.Log(packetBuffer.AsSpan(0, packetlength), false);

                if (!allowPlugins || Plugin.ProcessRecvPacket(packetBuffer, ref packetlength))
                {
                    AnalyzePacket(world, packetBuffer.AsSpan(0, packetlength), offset);

                    ++packetsCount;
                }
            }
        }

        return packetsCount;
    }

    public void Append(Span<byte> data, bool fromPlugins)
    {
        if (data.IsEmpty)
            return;

        (fromPlugins ? _pluginsBuffer : _buffer).Enqueue(data);
    }

    private void AnalyzePacket(World world, ReadOnlySpan<byte> data, int offset)
    {
        if (data.IsEmpty)
            return;

        OnPacketBufferReader bufferReader = _handlers[data[0]];

        if (bufferReader != null)
        {
            var buffer = new StackDataReader(data);
            buffer.Seek(offset);

            bufferReader(world, ref buffer);
        }
    }

    private static bool GetPacketInfo(
        CircularBuffer buffer,
        int bufferLen,
        out byte packetID,
        out int packetOffset,
        out int packetLen
    )
    {
        if (buffer == null || bufferLen <= 0)
        {
            packetID = 0xFF;
            packetLen = 0;
            packetOffset = 0;

            return false;
        }

        packetLen = AsyncNetClient.PacketsTable.GetPacketLength(packetID = buffer[0]);
        packetOffset = 1;

        if (packetLen == -1)
        {
            if (bufferLen < 3)
            {
                return false;
            }

            byte b0 = buffer[1];
            byte b1 = buffer[2];

            packetLen = (b0 << 8) | b1;
            packetOffset = 3;
        }

        return true;
    }

    public static void SendMegaClilocRequests(World world)
    {
        if (world.ClientFeatures.TooltipsEnabled && Handler._clilocRequests.Count != 0)
        {
            if (Client.Game.UO.Version >= Utility.ClientVersion.CV_5090)
            {
                if (Handler._clilocRequests.Count != 0)
                {
                    AsyncNetClient.Socket.Send_MegaClilocRequest(Handler._clilocRequests);
                }
            }
            else
            {
                foreach (uint serial in Handler._clilocRequests)
                {
                    AsyncNetClient.Socket.Send_MegaClilocRequest_Old(serial);
                }

                Handler._clilocRequests.Clear();
            }
        }

        if (Handler._customHouseRequests.Count > 0)
        {
            for (int i = 0; i < Handler._customHouseRequests.Count; ++i)
            {
                AsyncNetClient.Socket.Send_CustomHouseDataRequest(Handler._customHouseRequests[i]);
            }

            Handler._customHouseRequests.Clear();
        }
    }

    public static void AddMegaClilocRequest(uint serial)
    {
        foreach (uint s in Handler._clilocRequests)
        {
            if (s == serial)
            {
                return;
            }
        }

        Handler._clilocRequests.Add(serial);
    }

    private static void TargetCursor(World world, ref StackDataReader p)
    {
        var cursorTarget = (CursorTarget)p.ReadUInt8();
        uint cursorId = p.ReadUInt32BE();
        var targetType = (TargetType)p.ReadUInt8();

        world.TargetManager.SetTargeting(cursorTarget, cursorId, targetType);

        if (world.Party.PartyHealTimer < Time.Ticks && world.Party.PartyHealTarget != 0)
        {
            world.TargetManager.Target(world.Party.PartyHealTarget);
            world.Party.PartyHealTimer = 0;
            world.Party.PartyHealTarget = 0;
        }
        else if (TargetManager.NextAutoTarget.IsSet && TargetManager.NextAutoTarget.ExpectedTargetType == targetType)
        {
            world.TargetManager.Target(TargetManager.NextAutoTarget.TargetSerial);
        }

        // Always clear after any target cursor (no queuing)
        TargetManager.NextAutoTarget.Clear();
    }

    private static void SecureTrading(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        byte type = p.ReadUInt8();
        uint serial = p.ReadUInt32BE();

        if (type == 0)
        {
            uint id1 = p.ReadUInt32BE();
            uint id2 = p.ReadUInt32BE();

            // standard client doesn't allow the trading system if one of the traders is invisible (=not sent by server)
            if (world.Get(id1) == null || world.Get(id2) == null)
            {
                return;
            }

            bool hasName = p.ReadBool();
            string name = string.Empty;

            if (hasName && p.Position < p.Length)
            {
                name = p.ReadASCII();
            }

            UIManager.Add(new TradingGump(world, serial, name, id1, id2));
        }
        else if (type == 1)
        {
            UIManager.GetTradingGump(serial)?.Dispose();
        }
        else if (type == 2)
        {
            uint id1 = p.ReadUInt32BE();
            uint id2 = p.ReadUInt32BE();

            TradingGump trading = UIManager.GetTradingGump(serial);

            if (trading != null)
            {
                trading.ImAccepting = id1 != 0;
                trading.HeIsAccepting = id2 != 0;

                trading.RequestUpdateContents();
            }
        }
        else if (type == 3 || type == 4)
        {
            TradingGump trading = UIManager.GetTradingGump(serial);

            if (trading != null)
            {
                if (type == 4)
                {
                    trading.Gold = p.ReadUInt32BE();
                    trading.Platinum = p.ReadUInt32BE();
                }
                else
                {
                    trading.HisGold = p.ReadUInt32BE();
                    trading.HisPlatinum = p.ReadUInt32BE();
                }
            }
        }
    }

    private static void PlaySoundEffect(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        p.Skip(1);

        ushort index = p.ReadUInt16BE();
        ushort audio = p.ReadUInt16BE();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        short z = (short)p.ReadUInt16BE();

        Client.Game.Audio.PlaySoundWithDistance(world, index, x, y);
    }

    private static void PlayMusic(World world, ref StackDataReader p)
    {
        if (p.Length == 3) // Play Midi Music packet (0x6D, 0x10, index)
        {
            byte cmd = p.ReadUInt8();
            byte index = p.ReadUInt8();

            // Check for stop music packet (6D 1F FF)
            if (cmd == 0x1F && index == 0xFF)
            {
                Client.Game.Audio.StopMusic();
            }
            else
            {
                Client.Game.Audio.PlayMusic(index);
            }
        }
        else
        {
            ushort index = p.ReadUInt16BE();
            Client.Game.Audio.PlayMusic(index);
        }
    }

    private static void MapData(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();

        MapGump gump = UIManager.GetGump<MapGump>(serial);

        if (gump != null)
        {
            switch ((MapMessageType)p.ReadUInt8())
            {
                case MapMessageType.Add:
                    p.Skip(1);

                    ushort x = p.ReadUInt16BE();
                    ushort y = p.ReadUInt16BE();

                    gump.AddPin(x, y);

                    break;

                case MapMessageType.Insert:
                    break;
                case MapMessageType.Move:
                    break;
                case MapMessageType.Remove:
                    break;

                case MapMessageType.Clear:
                    gump.ClearContainer();

                    break;

                case MapMessageType.Edit:
                    break;

                case MapMessageType.EditResponse:
                    gump.SetPlotState(p.ReadUInt8());

                    break;
            }
        }
    }

    private static void SetTime(World world, ref StackDataReader p) { }

    private static void SetWeather(World world, ref StackDataReader p)
    {
        GameScene scene = Client.Game.GetScene<GameScene>();

        if (scene == null)
        {
            return;
        }

        var type = (WeatherType)p.ReadUInt8();

        if (world.Weather.CurrentWeather != type)
        {
            byte count = p.ReadUInt8();
            byte temp = p.ReadUInt8();

            world.Weather.Generate(type, count, temp);
            EventSink.InvokeOnSetWeather(null, new WeatherEventArgs(type, count, temp));
        }
    }

    private static void BookData(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        ushort pageCnt = p.ReadUInt16BE();

        ModernBookGump gump = UIManager.GetGump<ModernBookGump>(serial);

        if (gump == null || gump.IsDisposed)
        {
            return;
        }

        for (int i = 0; i < pageCnt; i++)
        {
            int pageNum = p.ReadUInt16BE() - 1;
            gump.KnownPages.Add(pageNum);

            if (pageNum < gump.BookPageCount && pageNum >= 0)
            {
                ushort lineCnt = p.ReadUInt16BE();

                for (int line = 0; line < lineCnt; line++)
                {
                    int index = pageNum * ModernBookGump.MAX_BOOK_LINES + line;

                    if (index < gump.BookLines.Length)
                    {
                        gump.BookLines[index] = ModernBookGump.IsNewBook
                            ? p.ReadUTF8(true)
                            : p.ReadASCII();
                    }
                    else
                    {
                        Log.Error(
                            "BOOKGUMP: The server is sending a page number GREATER than the allowed number of pages in BOOK!"
                        );
                    }
                }

                if (lineCnt < ModernBookGump.MAX_BOOK_LINES)
                {
                    for (int line = lineCnt; line < ModernBookGump.MAX_BOOK_LINES; line++)
                    {
                        gump.BookLines[pageNum * ModernBookGump.MAX_BOOK_LINES + line] =
                            string.Empty;
                    }
                }
            }
            else
            {
                Log.Error(
                    "BOOKGUMP: The server is sending a page number GREATER than the allowed number of pages in BOOK!"
                );
            }
        }

        gump.ServerSetBookText();
    }

    private static void CharacterAnimation(World world, ref StackDataReader p)
    {
        Mobile mobile = world.Mobiles.Get(p.ReadUInt32BE());

        if (mobile == null)
        {
            return;
        }

        ushort action = p.ReadUInt16BE();
        ushort frame_count = p.ReadUInt16BE();
        ushort repeat_count = p.ReadUInt16BE();
        bool forward = !p.ReadBool();
        bool repeat = p.ReadBool();
        byte delay = p.ReadUInt8();

        mobile.SetAnimation(
            Mobile.GetReplacedObjectAnimation(mobile.Graphic, action),
            delay,
            (byte)frame_count,
            (byte)repeat_count,
            repeat,
            forward,
            true
        );
    }

    private static void GraphicEffect(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        var type = (GraphicEffectType)p.ReadUInt8();

        if (type > GraphicEffectType.FixedFrom)
        {
            if (type == GraphicEffectType.ScreenFade && p[0] == 0x70)
            {
                p.Skip(8);
                ushort val = p.ReadUInt16BE();

                if (val > 4)
                {
                    val = 4;
                }

                Log.Warn("Effect not implemented");
            }

            return;
        }

        uint source = p.ReadUInt32BE();
        uint target = p.ReadUInt32BE();
        ushort graphic = p.ReadUInt16BE();
        ushort srcX = p.ReadUInt16BE();
        ushort srcY = p.ReadUInt16BE();
        sbyte srcZ = p.ReadInt8();
        ushort targetX = p.ReadUInt16BE();
        ushort targetY = p.ReadUInt16BE();
        sbyte targetZ = p.ReadInt8();
        byte speed = p.ReadUInt8();
        byte duration = p.ReadUInt8();
        ushort unk = p.ReadUInt16BE();
        bool fixedDirection = p.ReadBool();
        bool doesExplode = p.ReadBool();
        uint hue = 0;
        GraphicEffectBlendMode blendmode = 0;

        if (p[0] == 0x70) { }
        else
        {
            hue = p.ReadUInt32BE();
            blendmode = (GraphicEffectBlendMode)(p.ReadUInt32BE() % 7);

            if (p[0] == 0xC7)
            {
                ushort tileID = p.ReadUInt16BE();
                ushort explodeEffect = p.ReadUInt16BE();
                ushort explodeSound = p.ReadUInt16BE();
                uint serial = p.ReadUInt32BE();
                byte layer = p.ReadUInt8();
                p.Skip(2);
            }
        }

        world.SpawnEffect(
            type,
            source,
            target,
            graphic,
            (ushort)hue,
            srcX,
            srcY,
            srcZ,
            targetX,
            targetY,
            targetZ,
            speed,
            duration,
            fixedDirection,
            doesExplode,
            false,
            blendmode
        );
    }

    private static void ClientViewRange(World world, ref StackDataReader p) => world.ClientViewRange = p.ReadUInt8();

    private static void BulletinBoardData(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        switch (p.ReadUInt8())
        {
            case 0: // open

                {
                    uint serial = p.ReadUInt32BE();
                    Item item = world.Items.Get(serial);

                    if (item != null)
                    {
                        BulletinBoardGump bulletinBoard = UIManager.GetGump<BulletinBoardGump>(
                            serial
                        );
                        bulletinBoard?.Dispose();

                        int x = (Client.Game.Window.ClientBounds.Width >> 1) - 245;
                        int y = (Client.Game.Window.ClientBounds.Height >> 1) - 205;

                        bulletinBoard = new BulletinBoardGump(world, item, x, y, p.ReadUTF8(22, true)); //p.ReadASCII(22));
                        UIManager.Add(bulletinBoard);

                        item.Opened = true;
                    }
                }

                break;

            case 1: // summary msg

                {
                    uint boardSerial = p.ReadUInt32BE();
                    BulletinBoardGump bulletinBoard = UIManager.GetGump<BulletinBoardGump>(
                        boardSerial
                    );

                    if (bulletinBoard != null)
                    {
                        uint serial = p.ReadUInt32BE();
                        uint parendID = p.ReadUInt32BE();

                        // poster
                        int len = p.ReadUInt8();
                        string text = (len <= 0 ? string.Empty : p.ReadUTF8(len, true)) + " - ";

                        // subject
                        len = p.ReadUInt8();
                        text += (len <= 0 ? string.Empty : p.ReadUTF8(len, true)) + " - ";

                        // datetime
                        len = p.ReadUInt8();
                        text += (len <= 0 ? string.Empty : p.ReadUTF8(len, true));

                        bulletinBoard.AddBulletinObject(serial, text);
                    }
                }

                break;

            case 2: // message

                {
                    uint boardSerial = p.ReadUInt32BE();
                    BulletinBoardGump bulletinBoard = UIManager.GetGump<BulletinBoardGump>(
                        boardSerial
                    );

                    if (bulletinBoard != null)
                    {
                        uint serial = p.ReadUInt32BE();

                        int len = p.ReadUInt8();
                        string poster = len > 0 ? p.ReadASCII(len) : string.Empty;

                        len = p.ReadUInt8();
                        string subject = len > 0 ? p.ReadUTF8(len, true) : string.Empty;

                        len = p.ReadUInt8();
                        string dataTime = len > 0 ? p.ReadASCII(len) : string.Empty;

                        p.Skip(4);

                        byte unk = p.ReadUInt8();

                        if (unk > 0)
                        {
                            p.Skip(unk * 4);
                        }

                        byte lines = p.ReadUInt8();

                        Span<char> span = stackalloc char[256];
                        var sb = new ValueStringBuilder(span);

                        for (int i = 0; i < lines; i++)
                        {
                            byte lineLen = p.ReadUInt8();

                            if (lineLen > 0)
                            {
                                string putta = p.ReadUTF8(lineLen, true);
                                sb.Append(putta);
                                sb.Append('\n');
                            }
                        }

                        string msg = sb.ToString();
                        byte variant = (byte)(1 + (poster == world.Player.Name ? 1 : 0));

                        UIManager.Add(
                            new BulletinBoardItem(
                                world,
                                boardSerial,
                                serial,
                                poster,
                                subject,
                                dataTime,
                                msg.TrimStart(),
                                variant
                            )
                            {
                                X = 40,
                                Y = 40
                            }
                        );

                        sb.Dispose();
                    }
                }

                break;
        }
    }

    private static void Warmode(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        world.Player.InWarMode = p.ReadBool();
    }

    private static void Ping(World world, ref StackDataReader p) => AsyncNetClient.Socket.Statistics.PingReceived(p.ReadUInt8());

    private static void BuyList(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        Item container = world.Items.Get(p.ReadUInt32BE());

        if (container == null)
        {
            return;
        }

        Mobile vendor = world.Mobiles.Get(container.Container);

        if (vendor == null)
        {
            return;
        }

        ShopGump gump = UIManager.GetGump<ShopGump>();
        ModernShopGump modernGump = UIManager.GetGump<ModernShopGump>();

        if (ProfileManager.CurrentProfile.UseModernShopGump)
        {
            modernGump?.Dispose();
            UIManager.Add(modernGump = new ModernShopGump(world, vendor, true));
        }
        else
        {
            if (gump != null && (gump.LocalSerial != vendor || !gump.IsBuyGump))
            {
                gump.Dispose();
                gump = null;
            }

            if (gump == null)
            {
                gump = new ShopGump(world, vendor, true, 150, 5);
                UIManager.Add(gump);
            }
        }

        if (container.Layer == Layer.ShopBuyRestock || container.Layer == Layer.ShopBuy)
        {
            byte count = p.ReadUInt8();

            LinkedObject first = container.Items;

            if (first == null)
            {
                return;
            }

            bool reverse = false;

            if (container.Graphic == 0x2AF8) //hardcoded logic in original client that we must match
            {
                //sort the contents
                first = container.SortContents<Item>((x, y) => x.X - y.X);
            }
            else
            {
                //skip to last item and read in reverse later
                reverse = true;

                while (first?.Next != null)
                {
                    first = first.Next;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (first == null)
                {
                    break;
                }

                var it = (Item)first;

                it.Price = p.ReadUInt32BE();
                byte nameLen = p.ReadUInt8();
                string name = p.ReadASCII(nameLen);

                if (world.OPL.TryGetNameAndData(it.Serial, out string s, out _))
                {
                    it.Name = s;
                }
                else if (int.TryParse(name, out int cliloc))
                {
                    it.Name = Client.Game.UO.FileManager.Clilocs.Translate(
                        cliloc,
                        $"\t{it.ItemData.Name}: \t{it.Amount}",
                        true
                    );
                }
                else if (string.IsNullOrEmpty(name))
                {
                    it.Name = it.ItemData.Name;
                }
                else
                {
                    it.Name = name;
                }

                if (reverse)
                {
                    first = first.Previous;
                }
                else
                {
                    first = first.Next;
                }
            }
        }
    }

    private static void UpdateCharacter(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        Mobile mobile = world.Mobiles.Get(serial);

        if (mobile == null)
        {
            return;
        }

        ushort graphic = p.ReadUInt16BE();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        sbyte z = p.ReadInt8();
        var direction = (Direction)p.ReadUInt8();
        ushort hue = p.ReadUInt16BE();
        var flags = (Flags)p.ReadUInt8();
        var notoriety = (NotorietyFlag)p.ReadUInt8();

        mobile.NotorietyFlag = notoriety;

        if (serial == world.Player)
        {
            mobile.Flags = flags;
            mobile.Graphic = graphic;
            mobile.CheckGraphicChange();
            mobile.FixHue(hue);
            // TODO: x,y,z, direction cause elastic effect, ignore 'em for the moment
        }
        else
        {
            UpdateGameObject(world, serial, graphic, 0, 0, x, y, z, direction, hue, flags, 0, 1, 1);
        }
    }

    private static void UpdateObject(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        ushort graphic = p.ReadUInt16BE();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        sbyte z = p.ReadInt8();
        var direction = (Direction)p.ReadUInt8();
        ushort hue = p.ReadUInt16BE();
        var flags = (Flags)p.ReadUInt8();
        var notoriety = (NotorietyFlag)p.ReadUInt8();
        bool oldDead = false;
        //bool alreadyExists =world.Get(serial) != null;

        if (serial == world.Player)
        {
            oldDead = world.Player.IsDead;
            world.Player.Graphic = graphic;
            world.Player.CheckGraphicChange();
            world.Player.FixHue(hue);
            world.Player.Flags = flags;
        }
        else
        {
            UpdateGameObject(world, serial, graphic, 0, 0, x, y, z, direction, hue, flags, 0, 0, 1);
        }

        Entity obj = world.Get(serial);

        if (obj == null)
        {
            return;
        }

        if (!obj.IsEmpty)
        {
            LinkedObject o = obj.Items;

            while (o != null)
            {
                LinkedObject next = o.Next;
                var it = (Item)o;

                if (!it.Opened && it.Layer != Layer.Backpack)
                {
                    world.RemoveItem(it.Serial, true);
                }

                o = next;
            }
        }

        if (SerialHelper.IsMobile(serial) && obj is Mobile mob)
        {
            mob.NotorietyFlag = notoriety;

            UIManager.GetGump<PaperDollGump>(serial)?.RequestUpdateContents();
            UIManager.GetGump<ModernPaperdoll>(serial)?.RequestUpdateContents();
        }

        if (p[0] != 0x78)
        {
            p.Skip(6);
        }

        uint itemSerial = p.ReadUInt32BE();

        while (itemSerial != 0 && p.Position < p.Length)
        {
            //if (!SerialHelper.IsItem(itemSerial))
            //    break;

            ushort itemGraphic = p.ReadUInt16BE();
            byte layer = p.ReadUInt8();
            ushort item_hue = 0;

            if (Client.Game.UO.Version >= Utility.ClientVersion.CV_70331)
            {
                item_hue = p.ReadUInt16BE();
            }
            else if ((itemGraphic & 0x8000) != 0)
            {
                itemGraphic &= 0x7FFF;
                item_hue = p.ReadUInt16BE();
            }

            Item item = world.GetOrCreateItem(itemSerial);
            item.Graphic = itemGraphic;
            item.FixHue(item_hue);
            item.Amount = 1;
            world.RemoveItemFromContainer(item);
            item.Container = serial;
            item.Layer = (Layer)layer;

            if (item.Layer == Layer.Mount && obj is Mobile parMob)
            {
                parMob.Mount = item;
            }

            item.CheckGraphicChange();

            obj.PushToBack(item);

            itemSerial = p.ReadUInt32BE();
        }

        if (serial == world.Player)
        {
            if (oldDead != world.Player.IsDead)
            {
                if (world.Player.IsDead)
                {
                    // NOTE: This packet causes some weird issue on sphere servers.
                    //       When the character dies, this packet trigger a "reset" and
                    //       somehow it messes up the packet reading server side
                    //NetClient.Socket.Send_DeathScreen();
                    world.ChangeSeason(Game.Managers.Season.Desolation, 42);
                }
                else
                {
                    world.ChangeSeason(world.OldSeason, world.OldMusicIndex);
                }
            }

            UIManager.GetGump<PaperDollGump>(serial)?.RequestUpdateContents();
            UIManager.GetGump<ModernPaperdoll>(serial)?.RequestUpdateContents();
            GameActions.RequestEquippedOPL(world);

            world.Player.UpdateAbilities();
        }
    }

    private static void OpenMenu(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        ushort id = p.ReadUInt16BE();
        string name = p.ReadASCII(p.ReadUInt8());
        int count = p.ReadUInt8();

        ushort menuid = p.ReadUInt16BE();
        p.Seek(p.Position - 2);

        if (menuid != 0)
        {
            var gump = new MenuGump(world, serial, id, name) { X = 100, Y = 100 };

            int posX = 0;

            for (int i = 0; i < count; i++)
            {
                ushort graphic = p.ReadUInt16BE();
                ushort hue = p.ReadUInt16BE();
                name = p.ReadASCII(p.ReadUInt8());

                ref readonly SpriteInfo artInfo = ref Client.Game.UO.Arts.GetArt(graphic);

                if (artInfo.UV.Width != 0 && artInfo.UV.Height != 0)
                {
                    int posY = artInfo.UV.Height;

                    if (posY >= 47)
                    {
                        posY = 0;
                    }
                    else
                    {
                        posY = (47 - posY) >> 1;
                    }

                    gump.AddItem(graphic, hue, name, posX, posY, i + 1);

                    posX += artInfo.UV.Width;
                }
            }

            UIManager.Add(gump);
        }
        else
        {
            var gump = new GrayMenuGump(world, serial, id, name)
            {
                X = (Client.Game.Window.ClientBounds.Width >> 1) - 200,
                Y = (Client.Game.Window.ClientBounds.Height >> 1) - ((121 + count * 21) >> 1)
            };

            int offsetY = 35 + gump.Height;
            int gumpHeight = 70 + offsetY;

            for (int i = 0; i < count; i++)
            {
                p.Skip(4);
                name = p.ReadASCII(p.ReadUInt8());

                int addHeight = gump.AddItem(name, offsetY);

                if (addHeight < 21)
                {
                    addHeight = 21;
                }

                offsetY += addHeight - 1;
                gumpHeight += addHeight;
            }

            offsetY += 5;

            gump.Add(
                new Button(0, 0x1450, 0x1451, 0x1450)
                {
                    ButtonAction = ButtonAction.Activate,
                    X = 70,
                    Y = offsetY
                }
            );

            gump.Add(
                new Button(1, 0x13B2, 0x13B3, 0x13B2)
                {
                    ButtonAction = ButtonAction.Activate,
                    X = 200,
                    Y = offsetY
                }
            );

            gump.SetHeight(gumpHeight);
            gump.WantUpdateSize = false;
            UIManager.Add(gump);
        }
    }

    private static void OpenPaperdoll(World world, ref StackDataReader p)
    {
        Mobile mobile = world.Mobiles.Get(p.ReadUInt32BE());

        if (mobile == null)
        {
            return;
        }

        string text = p.ReadASCII(60);
        byte flags = p.ReadUInt8();

        mobile.Title = text;
        if (ProfileManager.CurrentProfile.UseModernPaperdoll && mobile.Serial == world.Player.Serial)
        {
            ModernPaperdoll modernPaperdoll = UIManager.GetGump<ModernPaperdoll>(mobile.Serial);
            if (modernPaperdoll != null)
            {
                modernPaperdoll.UpdateTitle(text);
                modernPaperdoll.SetInScreen();
                modernPaperdoll.BringOnTop();
            }
            else
            {
                UIManager.Add(new ModernPaperdoll(world, mobile.Serial));
            }
            GameActions.RequestEquippedOPL(world);
        }
        else
        {
            PaperDollGump paperdoll = UIManager.GetGump<PaperDollGump>(mobile);

            if (paperdoll == null)
            {
                if (!UIManager.GetGumpCachePosition(mobile, out Point location))
                {
                    location = new Point(100, 100);
                }

                UIManager.Add(
                    new PaperDollGump(world, mobile, (flags & 0x02) != 0) { Location = location }
                );
            }
            else
            {
                bool old = paperdoll.CanLift;
                bool newLift = (flags & 0x02) != 0;

                paperdoll.CanLift = newLift;
                paperdoll.UpdateTitle(text);

                if (old != newLift)
                {
                    paperdoll.RequestUpdateContents();
                }

                paperdoll.SetInScreen();
                paperdoll.BringOnTop();
            }
        }
    }

    private static void CorpseEquipment(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        Entity corpse = world.Get(serial);

        if (corpse == null)
        {
            return;
        }

        // if it's not a corpse we should skip this [?]
        if (corpse.Graphic != 0x2006)
        {
            return;
        }

        var layer = (Layer)p.ReadUInt8();

        while (layer != Layer.Invalid && p.Position < p.Length)
        {
            uint item_serial = p.ReadUInt32BE();

            if (layer - 1 != Layer.Backpack)
            {
                Item item = world.GetOrCreateItem(item_serial);

                world.RemoveItemFromContainer(item);
                item.Container = serial;
                item.Layer = layer - 1;
                corpse.PushToBack(item);
            }

            layer = (Layer)p.ReadUInt8();
        }
    }

    private static void DisplayMap(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();
        ushort gumpid = p.ReadUInt16BE();
        ushort startX = p.ReadUInt16BE();
        ushort startY = p.ReadUInt16BE();
        ushort endX = p.ReadUInt16BE();
        ushort endY = p.ReadUInt16BE();
        ushort width = p.ReadUInt16BE();
        ushort height = p.ReadUInt16BE();

        var gump = new MapGump(world, serial, gumpid, width, height);
        SpriteInfo multiMapInfo;

        if (p[0] == 0xF5 || Client.Game.UO.Version >= Utility.ClientVersion.CV_308Z)
        {
            ushort facet = 0;

            if (p[0] == 0xF5)
            {
                facet = p.ReadUInt16BE();
            }

            multiMapInfo = Client.Game.UO.MultiMaps.GetMap(facet, width, height, startX, startY, endX, endY);

            gump.MapInfos(startX, startY, endX, endY, facet);
        }
        else
        {
            multiMapInfo = Client.Game.UO.MultiMaps.GetMap(null, width, height, startX, startY, endX, endY);

            gump.MapInfos(startX, startY, endX, endY);
        }

        if (multiMapInfo.Texture != null)
            gump.SetMapTexture(multiMapInfo.Texture);

        UIManager.Add(gump);

        Item it = world.Items.Get(serial);

        if (it != null)
        {
            it.Opened = true;
        }
    }

    private static void OpenBook(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();
        bool oldpacket = p[0] == 0x93;
        bool editable = p.ReadBool();

        if (!oldpacket)
        {
            editable = p.ReadBool();
        }
        else
        {
            p.Skip(1);
        }

        ModernBookGump bgump = UIManager.GetGump<ModernBookGump>(serial);

        if (bgump == null || bgump.IsDisposed)
        {
            ushort page_count = p.ReadUInt16BE();
            string title = oldpacket
                ? p.ReadUTF8(60, true)
                : p.ReadUTF8(p.ReadUInt16BE(), true);
            string author = oldpacket
                ? p.ReadUTF8(30, true)
                : p.ReadUTF8(p.ReadUInt16BE(), true);

            UIManager.Add(
                new ModernBookGump(world, serial, page_count, title, author, editable, oldpacket)
                {
                    X = 100,
                    Y = 100
                }
            );

            AsyncNetClient.Socket.Send_BookPageDataRequest(serial, 1);
        }
        else
        {
            p.Skip(2);
            bgump.IsEditable = editable;
            bgump.SetTile(
                oldpacket ? p.ReadUTF8(60, true) : p.ReadUTF8(p.ReadUInt16BE(), true),
                editable
            );
            bgump.SetAuthor(
                oldpacket ? p.ReadUTF8(30, true) : p.ReadUTF8(p.ReadUInt16BE(), true),
                editable
            );
            bgump.UseNewHeader = !oldpacket;
            bgump.SetInScreen();
            bgump.BringOnTop();
        }
    }

    private static void DyeData(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();
        p.Skip(2);
        ushort graphic = p.ReadUInt16BE();

        ref readonly SpriteInfo gumpInfo = ref Client.Game.UO.Gumps.GetGump(0x0906);

        int x = (Client.Game.Window.ClientBounds.Width >> 1) - (gumpInfo.UV.Width >> 1);
        int y = (Client.Game.Window.ClientBounds.Height >> 1) - (gumpInfo.UV.Height >> 1);

        ColorPickerGump gump = UIManager.GetGump<ColorPickerGump>(serial);

        if (gump == null || gump.IsDisposed || gump.Graphic != graphic)
        {
            gump?.Dispose();

            gump = new ColorPickerGump(world, serial, graphic, x, y, null);

            UIManager.Add(gump);
        }
    }

    private static void MovePlayer(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        var direction = (Direction)p.ReadUInt8();
        world.Player.Walk(direction & Direction.Mask, (direction & Direction.Running) != 0);
    }

    private static void UpdateName(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        string name = p.ReadASCII();

        WMapEntity wme = world.WMapManager.GetEntity(serial);

        if (wme != null && !string.IsNullOrEmpty(name))
        {
            wme.Name = name;
        }

        Entity entity = world.Get(serial);

        if (entity != null)
        {
            entity.Name = name;

            if (
                serial == world.Player.Serial
                && !string.IsNullOrEmpty(name)
                && name != world.Player.Name
            )
            {
                Client.Game.SetWindowTitle(name);
                    if (ProfileManager.CurrentProfile?.EnableTitleBarStats == true)
                    {
                        TitleBarStatsManager.ForceUpdate();
                    }
            }

            UIManager.GetGump<NameOverheadGump>(serial)?.SetName();
        }
    }

    private static void MultiPlacement(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        bool allowGround = p.ReadBool();
        uint targID = p.ReadUInt32BE();
        byte flags = p.ReadUInt8();
        p.Seek(18);
        ushort multiID = p.ReadUInt16BE();
        ushort xOff = p.ReadUInt16BE();
        ushort yOff = p.ReadUInt16BE();
        ushort zOff = p.ReadUInt16BE();
        ushort hue = p.ReadUInt16BE();

        world.TargetManager.SetTargetingMulti(targID, multiID, xOff, yOff, zOff, hue);
    }

    private static void ASCIIPrompt(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        world.MessageManager.PromptData = new PromptData
        {
            Prompt = ConsolePrompt.ASCII,
            Data = p.ReadUInt64BE()
        };
    }

    private static void SellList(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        Mobile vendor = world.Mobiles.Get(p.ReadUInt32BE());

        if (vendor == null)
        {
            return;
        }

        ushort countItems = p.ReadUInt16BE();

        if (countItems <= 0)
        {
            return;
        }

        ShopGump gump = UIManager.GetGump<ShopGump>(vendor);
        gump?.Dispose();
        ModernShopGump modernGump = UIManager.GetGump<ModernShopGump>(vendor);
        modernGump?.Dispose();

        if (ProfileManager.CurrentProfile.UseModernShopGump)
            modernGump = new ModernShopGump(world, vendor, false);
        else
            gump = new ShopGump(world, vendor, false, 100, 0);

        for (int i = 0; i < countItems; i++)
        {
            uint serial = p.ReadUInt32BE();
            ushort graphic = p.ReadUInt16BE();
            ushort hue = p.ReadUInt16BE();
            ushort amount = p.ReadUInt16BE();
            ushort price = p.ReadUInt16BE();
            string name = p.ReadASCII(p.ReadUInt16BE());
            bool fromcliloc = false;

            if (int.TryParse(name, out int clilocnum))
            {
                name = Client.Game.UO.FileManager.Clilocs.GetString(clilocnum);
                fromcliloc = true;
            }
            else if (string.IsNullOrEmpty(name))
            {
                bool success = world.OPL.TryGetNameAndData(serial, out name, out _);

                if (!success)
                {
                    name = Client.Game.UO.FileManager.TileData.StaticData[graphic].Name;
                }
            }

            //if (string.IsNullOrEmpty(item.Name))
            //    item.Name = name;
            BuySellAgent.Instance?.HandleSellPacket(vendor, serial, graphic, hue, amount, price);
            if (ProfileManager.CurrentProfile.UseModernShopGump)
                modernGump.AddItem
                    (
                        world,
                    serial,
                    graphic,
                    hue,
                    amount,
                    price,
                    name,
                    fromcliloc
                    );
            else
                gump.AddItem
                    (
                        serial,
                        graphic,
                        hue,
                        amount,
                        price,
                        name,
                        fromcliloc
                    );
        }

        if (ProfileManager.CurrentProfile.UseModernShopGump)
            UIManager.Add(modernGump);
        else
            UIManager.Add(gump);

        BuySellAgent.Instance?.HandleSellPacketFinished(vendor);
    }

    private static void UpdateHitpoints(World world, ref StackDataReader p)
    {
        Entity entity = world.Get(p.ReadUInt32BE());

        if (entity == null)
        {
            return;
        }

        ushort oldHits = entity.Hits;
        entity.HitsMax = p.ReadUInt16BE();
        entity.Hits = p.ReadUInt16BE();

        if (entity.HitsRequest == HitsRequestStatus.Pending)
        {
            entity.HitsRequest = HitsRequestStatus.Received;
        }

        if (entity == world.Player)
        {
            SpellVisualRangeManager.Instance.ClearCasting();
            TitleBarStatsManager.UpdateTitleBar();
        }

        // Check for bandage healing for all mobiles
        if (SerialHelper.IsMobile(entity.Serial) && oldHits != entity.Hits)
        {
            var mobile = entity as Mobile;
            if (mobile != null)
            {
                BandageManager.Instance.OnMobileHpChanged(mobile, oldHits, entity.Hits);
            }
        }
    }

    private static void UpdateMana(World world, ref StackDataReader p)
    {
        Mobile mobile = world.Mobiles.Get(p.ReadUInt32BE());

        if (mobile == null)
        {
            return;
        }

        mobile.ManaMax = p.ReadUInt16BE();
        mobile.Mana = p.ReadUInt16BE();

        if (mobile == world.Player)
        {
            TitleBarStatsManager.UpdateTitleBar();
        }
    }

    private static void UpdateStamina(World world, ref StackDataReader p)
    {
        Mobile mobile = world.Mobiles.Get(p.ReadUInt32BE());

        if (mobile == null)
        {
            return;
        }

        mobile.StaminaMax = p.ReadUInt16BE();
        mobile.Stamina = p.ReadUInt16BE();

        if (mobile == world.Player)
        {
            TitleBarStatsManager.UpdateTitleBar();
        }
    }

    private static void OpenUrl(World world, ref StackDataReader p)
    {
        string url = p.ReadASCII();

        if (!string.IsNullOrEmpty(url))
        {
            PlatformHelper.LaunchBrowser(url);
        }
    }

    private static void TipWindow(World world, ref StackDataReader p)
    {
        byte flag = p.ReadUInt8();

        if (flag == 1)
        {
            return;
        }

        uint tip = p.ReadUInt32BE();
        string str = p.ReadASCII(p.ReadUInt16BE())?.Replace('\r', '\n');

        int x = 20;
        int y = 20;

        if (flag == 0)
        {
            x = 200;
            y = 100;
        }

        UIManager.Add(new TipNoticeGump(world, tip, flag, str) { X = x, Y = y });
    }

    private static void AttackCharacter(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();

        //if (TargetManager.LastAttack != serial && World.InGame)
        //{



        //}

        GameActions.SendCloseStatus(world, world.TargetManager.LastAttack);
        world.TargetManager.LastAttack = serial;
        GameActions.RequestMobileStatus(world, serial);
    }

    private static void TextEntryDialog(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        byte parentID = p.ReadUInt8();
        byte buttonID = p.ReadUInt8();

        ushort textLen = p.ReadUInt16BE();
        string text = p.ReadASCII(textLen);

        bool haveCancel = p.ReadBool();
        byte variant = p.ReadUInt8();
        uint maxLength = p.ReadUInt32BE();

        ushort descLen = p.ReadUInt16BE();
        string desc = p.ReadASCII(descLen);

        var gump = new TextEntryDialogGump(
            world,
            serial,
            143,
            172,
            variant,
            (int)maxLength,
            text,
            desc,
            buttonID,
            parentID
        )
        {
            CanCloseWithRightClick = haveCancel
        };

        UIManager.Add(gump);
    }

    private static void UnicodeTalk(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            LoginScene scene = Client.Game.GetScene<LoginScene>();

            if (scene != null)
            {
                //Serial serial = p.ReadUInt32BE();
                //ushort graphic = p.ReadUInt16BE();
                //MessageType type = (MessageType)p.ReadUInt8();
                //Hue hue = p.ReadUInt16BE();
                //MessageFont font = (MessageFont)p.ReadUInt16BE();
                //string lang = p.ReadASCII(4);
                //string name = p.ReadASCII(30);
                Log.Warn("UnicodeTalk received during LoginScene");

                if (p.Length > 48)
                {
                    p.Seek(48);
                    Log.PushIndent();
                    Log.Warn("Handled UnicodeTalk in LoginScene");
                    Log.PopIndent();
                }
            }

            return;
        }

        uint serial = p.ReadUInt32BE();
        Entity entity = world.Get(serial);
        ushort graphic = p.ReadUInt16BE();
        var type = (MessageType)p.ReadUInt8();
        ushort hue = p.ReadUInt16BE();
        ushort font = p.ReadUInt16BE();
        string lang = p.ReadASCII(4);
        string name = p.ReadASCII();

        if (
            serial == 0
            && graphic == 0
            && type == MessageType.Regular
            && font == 0xFFFF
            && hue == 0xFFFF
            && name.ToLower() == "system"
        )
        {
            Span<byte> buffer =
                stackalloc byte[] {
                    0x03,
                    0x00,
                    0x28,
                    0x20,
                    0x00,
                    0x34,
                    0x00,
                    0x03,
                    0xdb,
                    0x13,
                    0x14,
                    0x3f,
                    0x45,
                    0x2c,
                    0x58,
                    0x0f,
                    0x5d,
                    0x44,
                    0x2e,
                    0x50,
                    0x11,
                    0xdf,
                    0x75,
                    0x5c,
                    0xe0,
                    0x3e,
                    0x71,
                    0x4f,
                    0x31,
                    0x34,
                    0x05,
                    0x4e,
                    0x18,
                    0x1e,
                    0x72,
                    0x0f,
                    0x59,
                    0xad,
                    0xf5,
                    0x00
            };

            AsyncNetClient.Socket.Send(buffer);

            return;
        }

        string text = string.Empty;

        if (p.Length > 48)
        {
            p.Seek(48);
            text = p.ReadUnicodeBE();
        }

        TextType text_type = TextType.SYSTEM;

        if (type == MessageType.Alliance || type == MessageType.Guild)
        {
            text_type = TextType.GUILD_ALLY;
        }
        else if (
            type == MessageType.System
            || serial == 0xFFFF_FFFF
            || serial == 0
            || name.ToLower() == "system" && entity == null
        )
        {
            // do nothing
        }
        else if (entity != null)
        {
            text_type = TextType.OBJECT;

            if (string.IsNullOrEmpty(entity.Name))
            {
                entity.Name = string.IsNullOrEmpty(name) ? text : name;
            }
        }

        world.MessageManager.HandleMessage(
            entity,
            text,
            name,
            hue,
            type,
            ProfileManager.CurrentProfile.ChatFont,
            text_type,
            true,
            lang
        );
    }

    private static void DisplayDeath(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        uint corpseSerial = p.ReadUInt32BE();
        uint running = p.ReadUInt32BE();

        Mobile owner = world.Mobiles.Get(serial);

        if (owner == null || serial == world.Player)
        {
            return;
        }

        serial |= 0x80000000;

        if (world.Mobiles.Remove(owner.Serial))
        {
            for (LinkedObject i = owner.Items; i != null; i = i.Next)
            {
                var it = (Item)i;
                it.Container = serial;
            }

            world.Mobiles[serial] = owner;
            owner.Serial = serial;
        }

        if (SerialHelper.IsValid(corpseSerial))
        {
            world.CorpseManager.Add(corpseSerial, serial, owner.Direction, running != 0);
        }

        Renderer.Animations.Animations animations = Client.Game.UO.Animations;
        ushort gfx = owner.Graphic;
        animations.ConvertBodyIfNeeded(ref gfx);
        AnimationGroupsType animGroup = animations.GetAnimType(gfx);
        AnimationFlags animFlags = animations.GetAnimFlags(gfx);
        byte group = Client.Game.UO.FileManager.Animations.GetDeathAction(
            gfx,
            animFlags,
            animGroup,
            running != 0,
            true
        );
        owner.SetAnimation(group, 0, 5, 1);
        owner.AnimIndex = 0;

        if (ProfileManager.CurrentProfile.AutoOpenCorpses)
        {
            world.Player.TryOpenCorpses();
        }
    }

    private static void OpenGump(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        uint sender = p.ReadUInt32BE();
        uint gumpID = p.ReadUInt32BE();
        int x = (int)p.ReadUInt32BE();
        int y = (int)p.ReadUInt32BE();

        ushort cmdLen = p.ReadUInt16BE();
        string cmd = p.ReadASCII(cmdLen);

        ushort textLinesCount = p.ReadUInt16BE();

        string[] lines = new string[textLinesCount];

        for (int i = 0; i < textLinesCount; ++i)
        {
            int length = p.ReadUInt16BE();

            if (length > 0)
            {
                lines[i] = p.ReadUnicodeBE(length);
            }
            else
            {
                lines[i] = string.Empty;
            }
        }

        //for (int i = 0, index = p.Position; i < textLinesCount; i++)
        //{
        //    int length = ((p[index++] << 8) | p[index++]) << 1;
        //    int true_length = 0;

        //    while (true_length < length)
        //    {
        //        if (((p[index + true_length++] << 8) | p[index + true_length++]) << 1 == '\0')
        //        {
        //            break;
        //        }
        //    }

        //    unsafe
        //    {

        //        fixed (byte* ptr = &p.Buffer[index])
        //        {
        //            lines[i] = Encoding.BigEndianUnicode.GetString(ptr, true_length);
        //        }
        //    }
        //    index += length;
        //}

        CreateGump(world, sender, gumpID, x, y, cmd, lines);
    }

    private static void ChatMessage(World world, ref StackDataReader p)
    {
        ushort cmd = p.ReadUInt16BE();

        switch (cmd)
        {
            case 0x03E8: // create conference
                p.Skip(4);
                string channelName = p.ReadUnicodeBE();
                bool hasPassword = p.ReadUInt16BE() == 0x31;
                world.ChatManager.CurrentChannelName = channelName;
                world.ChatManager.AddChannel(channelName, hasPassword);

                UIManager.GetGump<ChatGump>()?.RequestUpdateContents();

                break;

            case 0x03E9: // destroy conference
                p.Skip(4);
                channelName = p.ReadUnicodeBE();
                world.ChatManager.RemoveChannel(channelName);

                UIManager.GetGump<ChatGump>()?.RequestUpdateContents();

                break;

            case 0x03EB: // display enter username window
                world.ChatManager.ChatIsEnabled = ChatStatus.EnabledUserRequest;

                break;

            case 0x03EC: // close chat
                world.ChatManager.Clear();
                world.ChatManager.ChatIsEnabled = ChatStatus.Disabled;

                UIManager.GetGump<ChatGump>()?.Dispose();

                break;

            case 0x03ED: // username accepted, display chat
                p.Skip(4);
                string username = p.ReadUnicodeBE();
                world.ChatManager.ChatIsEnabled = ChatStatus.Enabled;
                AsyncNetClient.Socket.Send_ChatJoinCommand("General");

                break;

            case 0x03EE: // add user
                p.Skip(4);
                ushort userType = p.ReadUInt16BE();
                username = p.ReadUnicodeBE();

                break;

            case 0x03EF: // remove user
                p.Skip(4);
                username = p.ReadUnicodeBE();

                break;

            case 0x03F0: // clear all players
                break;

            case 0x03F1: // you have joined a conference
                p.Skip(4);
                channelName = p.ReadUnicodeBE();
                world.ChatManager.CurrentChannelName = channelName;

                UIManager.GetGump<ChatGump>()?.UpdateConference();

                GameActions.Print(
                    world,
                    string.Format(ResGeneral.YouHaveJoinedThe0Channel, channelName),
                    ProfileManager.CurrentProfile.ChatMessageHue,
                    MessageType.Regular,
                    1
                );

                break;

            case 0x03F4:
                p.Skip(4);
                channelName = p.ReadUnicodeBE();

                GameActions.Print(
                    world,
                    string.Format(ResGeneral.YouHaveLeftThe0Channel, channelName),
                    ProfileManager.CurrentProfile.ChatMessageHue,
                    MessageType.Regular,
                    1
                );

                break;

            case 0x0025:
            case 0x0026:
            case 0x0027:
                p.Skip(4);
                ushort msgType = p.ReadUInt16BE();
                username = p.ReadUnicodeBE();
                string msgSent = p.ReadUnicodeBE();

                if (!string.IsNullOrEmpty(msgSent))
                {
                    int idx = msgSent.IndexOf('{');
                    int idxLast = msgSent.IndexOf('}') + 1;

                    if (idxLast > idx && idx > -1)
                    {
                        msgSent = msgSent.Remove(idx, idxLast - idx);
                    }
                }

                //Color c = new Color(49, 82, 156, 0);
                world.MessageManager.HandleMessage(null, msgSent, username, ProfileManager.CurrentProfile.ChatMessageHue, MessageType.ChatSystem, 3, TextType.OBJECT, true);

                //GameActions.Print($"{username}: {msgSent}", ProfileManager.CurrentProfile.ChatMessageHue, MessageType.ChatSystem, 1);
                break;

            default:
                if (cmd >= 0x0001 && cmd <= 0x0024 || cmd >= 0x0028 && cmd <= 0x002C)
                {
                    // TODO: read Chat.enu ?
                    // http://docs.polserver.com/packets/index.php?Packet=0xB2

                    string msg = ChatManager.GetMessage(cmd - 1);

                    if (string.IsNullOrEmpty(msg))
                    {
                        return;
                    }

                    p.Skip(4);
                    string text = p.ReadUnicodeBE();

                    if (!string.IsNullOrEmpty(text))
                    {
                        int idx = msg.IndexOf("%1");

                        if (idx >= 0)
                        {
                            msg = msg.Replace("%1", text);
                        }

                        if (cmd - 1 == 0x000A || cmd - 1 == 0x0017)
                        {
                            idx = msg.IndexOf("%2");

                            if (idx >= 0)
                            {
                                msg = msg.Replace("%2", text);
                            }
                        }
                    }

                    GameActions.Print(world, msg, ProfileManager.CurrentProfile.ChatMessageHue, MessageType.ChatSystem, 1);
                }

                break;
        }
    }

    private static void Help(World world, ref StackDataReader p) { }

    private static void CharacterProfile(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        string header = p.ReadASCII();
        string footer = p.ReadUnicodeBE();

        string body = p.ReadUnicodeBE();

        UIManager.GetGump<ProfileGump>(serial)?.Dispose();

        UIManager.Add(
            new ProfileGump(world, serial, header, footer, body, serial == world.Player.Serial)
        );
    }

    private static void EnableLockedFeatures(World world, ref StackDataReader p)
    {
        LockedFeatureFlags flags = 0;

        if (Client.Game.UO.Version >= Utility.ClientVersion.CV_60142)
        {
            flags = (LockedFeatureFlags)p.ReadUInt32BE();
        }
        else
        {
            flags = (LockedFeatureFlags)p.ReadUInt16BE();
        }

        world.ClientLockedFeatures.SetFlags(flags);

        world.ChatManager.ChatIsEnabled = world.ClientLockedFeatures.Flags.HasFlag(
            LockedFeatureFlags.T2A
        )
            ? ChatStatus.Enabled
            : 0;

        BodyConvFlags bcFlags = 0;
        if (flags.HasFlag(LockedFeatureFlags.UOR))
            bcFlags |= BodyConvFlags.Anim1 | BodyConvFlags.Anim2;
        if (flags.HasFlag(LockedFeatureFlags.LBR))
            bcFlags |= BodyConvFlags.Anim1;
        if (flags.HasFlag(LockedFeatureFlags.AOS))
            bcFlags |= BodyConvFlags.Anim2;
        if (flags.HasFlag(LockedFeatureFlags.SE))
            bcFlags |= BodyConvFlags.Anim3;
        if (flags.HasFlag(LockedFeatureFlags.ML))
            bcFlags |= BodyConvFlags.Anim4;

        Client.Game.UO.Animations.UpdateAnimationTable(bcFlags);
    }

    private static void DisplayQuestArrow(World world, ref StackDataReader p)
    {
        bool display = p.ReadBool();
        ushort mx = p.ReadUInt16BE();
        ushort my = p.ReadUInt16BE();

        uint serial = 0;

        if (Client.Game.UO.Version >= Utility.ClientVersion.CV_7090)
        {
            serial = p.ReadUInt32BE();
        }

        QuestArrowGump arrow = UIManager.GetGump<QuestArrowGump>(serial);

        if (display)
        {
            if (arrow == null)
            {
                UIManager.Add(new QuestArrowGump(world, serial, mx, my));
            }
            else
            {
                arrow.SetRelativePosition(mx, my);
            }
        }
        else
        {
            if (arrow != null)
            {
                arrow.Dispose();
            }
        }
    }

    private static void UltimaMessengerR(World world, ref StackDataReader p) { }

    private static void Season(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        byte season = p.ReadUInt8();
        byte music = p.ReadUInt8();

        if (season > 4)
        {
            season = 0;
        }

        // Apply season filter
        world.RealSeason = (Season)season;
        Season filteredSeason = SeasonFilter.Instance.ApplyFilter((Season)season);

        if (world.Player.IsDead && filteredSeason == Game.Managers.Season.Desolation)
        {
            return;
        }

        world.OldSeason = (Season)season;
        world.OldMusicIndex = music;

        if (world.Season == Game.Managers.Season.Desolation)
        {
            world.OldMusicIndex = 42;
        }

        world.ChangeSeason(filteredSeason, music);
    }

    private static void AssistVersion(World world, ref StackDataReader p)
    {
        //uint version = p.ReadUInt32BE();

        //string[] parts = Service.GetByLocalSerial<Settings>().ClientVersion.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        //byte[] clientVersionBuffer =
        //    {byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), byte.Parse(parts[3])};

        //NetClient.Socket.Send(new PAssistVersion(clientVersionBuffer, version));
    }

    private static void ExtendedCommand(World world, ref StackDataReader p)
    {
        ushort cmd = p.ReadUInt16BE();

        switch (cmd)
        {
            case 0:
                break;

            //===========================================================================================
            //===========================================================================================
            case 1: // fast walk prevention
                for (int i = 0; i < 6; i++)
                {
                    world.Player.Walker.FastWalkStack.SetValue(i, p.ReadUInt32BE());
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 2: // add key to fast walk stack
                world.Player.Walker.FastWalkStack.AddValue(p.ReadUInt32BE());

                break;

            //===========================================================================================
            //===========================================================================================
            case 4: // close generic gump
                uint ser = p.ReadUInt32BE();
                int button = (int)p.ReadUInt32BE();

                LinkedListNode<Gump> first = UIManager.Gumps.First;

                while (first != null)
                {
                    LinkedListNode<Gump> nextGump = first.Next;

                    if (first.Value.ServerSerial == ser && first.Value.IsFromServer)
                    {
                        if (button != 0)
                        {
                            (first.Value as Gump)?.OnButtonClick(button);
                        }
                        else
                        {
                            if (first.Value.CanMove)
                            {
                                UIManager.SavePosition(ser, first.Value.Location);
                            }
                            else
                            {
                                UIManager.RemovePosition(ser);
                            }
                        }

                        first.Value.Dispose();
                    }

                    first = nextGump;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 6: //party
                world.Party.ParsePacket(ref p);

                break;

            //===========================================================================================
            //===========================================================================================
            case 8: // map change
                world.MapIndex = p.ReadUInt8();

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x0C: // close statusbar gump
                UIManager.GetGump<HealthBarGump>(p.ReadUInt32BE())?.Dispose();

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x10: // display equip info
                Item item = world.Items.Get(p.ReadUInt32BE());

                if (item == null)
                {
                    return;
                }

                uint cliloc = p.ReadUInt32BE();
                string str = string.Empty;

                if (cliloc > 0)
                {
                    str = Client.Game.UO.FileManager.Clilocs.GetString((int)cliloc, true);

                    if (!string.IsNullOrEmpty(str))
                    {
                        item.Name = str;
                    }

                    world.MessageManager.HandleMessage(
                        item,
                        str,
                        item.Name,
                        0x3B2,
                        MessageType.Regular,
                        3,
                        TextType.OBJECT,
                        true
                    );
                }

                str = string.Empty;
                ushort crafterNameLen = 0;
                uint next = p.ReadUInt32BE();

                Span<char> span = stackalloc char[256];
                var strBuffer = new ValueStringBuilder(span);
                if (next == 0xFFFFFFFD)
                {
                    crafterNameLen = p.ReadUInt16BE();

                    if (crafterNameLen > 0)
                    {
                        strBuffer.Append(ResGeneral.CraftedBy);
                        strBuffer.Append(p.ReadASCII(crafterNameLen));
                    }
                }

                if (crafterNameLen != 0)
                {
                    next = p.ReadUInt32BE();
                }

                if (next == 0xFFFFFFFC)
                {
                    strBuffer.Append("[Unidentified");
                }

                byte count = 0;

                while (p.Position < p.Length - 4)
                {
                    if (count != 0 || next == 0xFFFFFFFD || next == 0xFFFFFFFC)
                    {
                        next = p.ReadUInt32BE();
                    }

                    short charges = (short)p.ReadUInt16BE();
                    string attr = Client.Game.UO.FileManager.Clilocs.GetString((int)next);

                    if (attr != null)
                    {
                        if (charges == -1)
                        {
                            if (count > 0)
                            {
                                strBuffer.Append("/");
                                strBuffer.Append(attr);
                            }
                            else
                            {
                                strBuffer.Append(" [");
                                strBuffer.Append(attr);
                            }
                        }
                        else
                        {
                            strBuffer.Append("\n[");
                            strBuffer.Append(attr);
                            strBuffer.Append(" : ");
                            strBuffer.Append(charges.ToString());
                            strBuffer.Append("]");
                            count += 20;
                        }
                    }

                    count++;
                }

                if (count < 20 && count > 0 || next == 0xFFFFFFFC && count == 0)
                {
                    strBuffer.Append(']');
                }

                if (strBuffer.Length != 0)
                {
                    world.MessageManager.HandleMessage(
                        item,
                        strBuffer.ToString(),
                        item.Name,
                        0x3B2,
                        MessageType.Regular,
                        3,
                        TextType.OBJECT,
                        true
                    );
                }

                strBuffer.Dispose();

                AsyncNetClient.Socket.Send_MegaClilocRequest_Old(item);

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x11:
                break;

            //===========================================================================================
            //===========================================================================================
            case 0x14: // display popup/context menu
                UIManager.ShowGamePopup(
                    new PopupMenuGump(world, PopupMenuData.Parse(ref p))
                    {
                        X = world.DelayedObjectClickManager.LastMouseX,
                        Y = world.DelayedObjectClickManager.LastMouseY
                    }
                );

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x16: // close user interface windows
                uint id = p.ReadUInt32BE();
                uint serial = p.ReadUInt32BE();

                switch (id)
                {
                    case 1: // paperdoll
                        UIManager.GetGump<PaperDollGump>(serial)?.Dispose();
                        UIManager.GetGump<ModernPaperdoll>(serial)?.Dispose();

                        break;

                    case 2: //statusbar
                        UIManager.GetGump<HealthBarGump>(serial)?.Dispose();

                        if (serial == world.Player.Serial)
                        {
                            StatusGumpBase.GetStatusGump()?.Dispose();
                        }

                        break;

                    case 8: // char profile
                        UIManager.GetGump<ProfileGump>()?.Dispose();

                        break;

                    case 0x0C: //container
                        UIManager.GetGump<ContainerGump>(serial)?.Dispose();

                        break;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x18: // enable map patches

                if (Client.Game.UO.FileManager.Maps.ApplyPatches(ref p))
                {
                    //List<GameObject> list = new List<GameObject>();

                    //foreach (int i in World.Map.GetUsedChunks())
                    //{
                    //    Chunk chunk = World.Map.Chunks[i];

                    //    for (int xx = 0; xx < 8; xx++)
                    //    {
                    //        for (int yy = 0; yy < 8; yy++)
                    //        {
                    //            Tile tile = chunk.Tiles[xx, yy];

                    //            for (GameObject obj = tile.FirstNode; obj != null; obj = obj.Right)
                    //            {
                    //                if (!(obj is Static) && !(obj is Land))
                    //                {
                    //                    list.Add(obj);
                    //                }
                    //            }
                    //        }
                    //    }
                    //}


                    int map = world.MapIndex;
                    world.MapIndex = -1;
                    world.MapIndex = map;

                    Log.Trace("Map Patches applied.");
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x19: //extened stats
                byte version = p.ReadUInt8();
                serial = p.ReadUInt32BE();

                switch (version)
                {
                    case 0:
                        Mobile bonded = world.Mobiles.Get(serial);

                        if (bonded == null)
                        {
                            break;
                        }

                        bool dead = p.ReadBool();
                        bonded.IsDead = dead;

                        break;

                    case 2:

                        if (serial == world.Player)
                        {
                            byte updategump = p.ReadUInt8();
                            byte state = p.ReadUInt8();

                            world.Player.StrLock = (Lock)((state >> 4) & 3);
                            world.Player.DexLock = (Lock)((state >> 2) & 3);
                            world.Player.IntLock = (Lock)(state & 3);

                            StatusGumpBase.GetStatusGump()?.RequestUpdateContents();
                        }

                        break;

                    case 5:

                        int pos = p.Position;
                        byte zero = p.ReadUInt8();
                        byte type2 = p.ReadUInt8();

                        if (type2 == 0xFF)
                        {
                            byte status = p.ReadUInt8();
                            ushort animation = p.ReadUInt16BE();
                            ushort frame = p.ReadUInt16BE();

                            if (status == 0 && animation == 0 && frame == 0)
                            {
                                p.Seek(pos);
                                goto case 0;
                            }

                            Mobile mobile = world.Mobiles.Get(serial);

                            if (mobile != null)
                            {
                                mobile.SetAnimation(
                                    Mobile.GetReplacedObjectAnimation(mobile.Graphic, animation)
                                );
                                mobile.ExecuteAnimation = false;
                                mobile.AnimIndex = (byte)frame;
                            }
                        }
                        else if (world.Player != null && serial == world.Player)
                        {
                            p.Seek(pos);
                            goto case 2;
                        }

                        break;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x1B: // new spellbook content
                p.Skip(2);
                Item spellbook = world.GetOrCreateItem(p.ReadUInt32BE());
                spellbook.Graphic = p.ReadUInt16BE();
                spellbook.Clear();
                ushort type = p.ReadUInt16BE();

                for (int j = 0; j < 2; j++)
                {
                    uint spells = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        spells |= (uint)(p.ReadUInt8() << (i * 8));
                    }

                    for (int i = 0; i < 32; i++)
                    {
                        if ((spells & (1 << i)) != 0)
                        {
                            ushort cc = (ushort)(j * 32 + i + 1);
                            // FIXME: should i call Item.Create ?
                            var spellItem = Item.Create(world, cc); // new Item()
                            spellItem.Serial = cc;
                            spellItem.Graphic = 0x1F2E;
                            spellItem.Amount = cc;
                            spellItem.Container = spellbook;
                            spellbook.PushToBack(spellItem);
                        }
                    }
                }

                UIManager.GetGump<SpellbookGump>(spellbook)?.RequestUpdateContents();

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x1D: // house revision state
                serial = p.ReadUInt32BE();
                uint revision = p.ReadUInt32BE();

                Item multi = world.Items.Get(serial);

                if (multi == null)
                {
                    world.HouseManager.Remove(serial);
                }

                if (
                    !world.HouseManager.TryGetHouse(serial, out House house)
                    || !house.IsCustom
                    || house.Revision != revision
                )
                {
                    Handler._customHouseRequests.Add(serial);
                }
                else
                {
                    house.Generate();
                    world.BoatMovingManager.ClearSteps(serial);

                    UIManager.GetGump<MiniMapGump>()?.RequestUpdateContents();

                    if (world.HouseManager.EntityIntoHouse(serial, world.Player))
                    {
                        Client.Game.GetScene<GameScene>()?.UpdateMaxDrawZ(true);
                    }
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x20:
                serial = p.ReadUInt32BE();
                type = p.ReadUInt8();
                ushort graphic = p.ReadUInt16BE();
                ushort x = p.ReadUInt16BE();
                ushort y = p.ReadUInt16BE();
                sbyte z = p.ReadInt8();

                switch (type)
                {
                    case 1: // update
                        break;

                    case 2: // remove
                        break;

                    case 3: // update multi pos
                        break;

                    case 4: // begin
                        HouseCustomizationGump gump = UIManager.GetGump<HouseCustomizationGump>();

                        if (gump != null)
                        {
                            break;
                        }

                        gump = new HouseCustomizationGump(world, serial, 50, 50);
                        UIManager.Add(gump);

                        break;

                    case 5: // end
                        UIManager.GetGump<HouseCustomizationGump>(serial)?.Dispose();

                        break;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x21:

                for (int i = 0; i < 2; i++)
                {
                    world.Player.Abilities[i] &= (Ability)0x7F;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x22:
                p.Skip(1);

                Entity en = world.Get(p.ReadUInt32BE());

                if (en != null)
                {
                    byte damage = p.ReadUInt8();

                    if (damage > 0)
                    {
                        world.WorldTextManager.AddDamage(en, damage);
                    }
                }

                break;

            case 0x25:

                ushort spell = p.ReadUInt16BE();
                bool active = p.ReadBool();

                for (LinkedListNode<Gump> last = UIManager.Gumps.Last; last != null; last = last.Previous)
                {
                    Control c = last.Value;

                    if (c.IsDisposed || !c.IsVisible) continue;

                    if (c is not UseSpellButtonGump spellButton || spellButton.SpellID != spell) continue;

                    if (active)
                    {
                        spellButton.Hue = 38;
                        world.ActiveSpellIcons.Add(spell);
                    }
                    else
                    {
                        spellButton.Hue = 0;
                        world.ActiveSpellIcons.Remove(spell);
                    }

                    break;
                }

                break;

            //===========================================================================================
            //===========================================================================================
            case 0x26:
                byte val = p.ReadUInt8();

                if (val > (int)CharacterSpeedType.FastUnmountAndCantRun)
                {
                    val = 0;
                }

                if (world.Player == null) break;

                world.Player.SpeedMode = (CharacterSpeedType)val;

                break;

            case 0x2A:
                bool isfemale = p.ReadBool();
                byte race = p.ReadUInt8();

                UIManager.GetGump<RaceChangeGump>()?.Dispose();
                UIManager.Add(new RaceChangeGump(world, isfemale, race));
                break;

            case 0x2B:
                serial = p.ReadUInt16BE();
                byte animID = p.ReadUInt8();
                byte frameCount = p.ReadUInt8();

                foreach (Mobile m in world.Mobiles.Values)
                {
                    if ((m.Serial & 0xFFFF) == serial)
                    {
                        m.SetAnimation(animID);
                        m.AnimIndex = frameCount;
                        m.ExecuteAnimation = false;

                        break;
                    }
                }

                break;

            case 0xBEEF: // ClassicUO commands

                type = p.ReadUInt16BE();

                break;

            default:
                Log.Warn($"Unhandled 0xBF - sub: {cmd.ToHex()}");

                break;
        }
    }

    private static void DisplayClilocString(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        Entity entity = world.Get(serial);
        ushort graphic = p.ReadUInt16BE();
        var type = (MessageType)p.ReadUInt8();
        ushort hue = p.ReadUInt16BE();
        ushort font = p.ReadUInt16BE();
        uint cliloc = p.ReadUInt32BE();
        AffixType flags = p[0] == 0xCC ? (AffixType)p.ReadUInt8() : 0x00;
        string name = p.ReadASCII(30);
        string affix = p[0] == 0xCC ? p.ReadASCII() : string.Empty;

        string arguments = null;

        SpellVisualRangeManager.Instance.OnClilocReceived((int)cliloc);

        if (cliloc == 1008092 || cliloc == 1005445) // value for "You notify them you don't want to join the party" || "You have been added to the party"
        {
            for (LinkedListNode<Gump> g = UIManager.Gumps.Last; g != null; g = g.Previous)
            {
                if (g.Value is PartyInviteGump pg)
                {
                    pg.Dispose();
                }
            }
        }

        int remains = p.Remaining;

        if (remains > 0)
        {
            if (p[0] == 0xCC)
            {
                arguments = p.ReadUnicodeBE(remains);
            }
            else
            {
                arguments = p.ReadUnicodeLE(remains / 2);
            }
        }

        string text = Client.Game.UO.FileManager.Clilocs.Translate((int)cliloc, arguments);

        if (text == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(affix))
        {
            if ((flags & AffixType.Prepend) != 0)
            {
                text = $"{affix}{text}";
            }
            else
            {
                text = $"{text}{affix}";
            }
        }

        if ((flags & AffixType.System) != 0)
        {
            type = MessageType.System;
        }

        if (!Client.Game.UO.FileManager.Fonts.UnicodeFontExists((byte)font))
        {
            font = 0;
        }

        TextType text_type = TextType.SYSTEM;

        if (
            serial == 0xFFFF_FFFF
            || serial == 0
            || !string.IsNullOrEmpty(name)
                && string.Equals(name, "system", StringComparison.InvariantCultureIgnoreCase)
        )
        {
            // do nothing
        }
        else if (entity != null)
        {
            //entity.Graphic = graphic;
            text_type = TextType.OBJECT;

            if (string.IsNullOrEmpty(entity.Name))
            {
                entity.Name = name;
            }
        }

        EventSink.InvokeClilocMessageReceived(entity, new MessageEventArgs(entity, text, name, hue, type, (byte)font, text_type, true) { Cliloc = cliloc });

        world.MessageManager.HandleMessage(
            entity,
            text,
            name,
            hue,
            type,
            (byte)font,
            text_type,
            true
        );
    }

    private static void UnicodePrompt(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        world.MessageManager.PromptData = new PromptData
        {
            Prompt = ConsolePrompt.Unicode,
            Data = p.ReadUInt64BE()
        };
    }

    private static void Semivisible(World world, ref StackDataReader p) { }

    private static void InvalidMapEnable(World world, ref StackDataReader p) { }

    private static void ParticleEffect3D(World world, ref StackDataReader p) { }

    private static void GetUserServerPingGodClientR(World world, ref StackDataReader p) { }

    private static void GlobalQueCount(World world, ref StackDataReader p) { }

    private static void ConfigurationFileR(World world, ref StackDataReader p) { }

    private static void Logout(World world, ref StackDataReader p)
    {
        // http://docs.polserver.com/packets/index.php?Packet=0xD1

            if (
                Client.Game.GetScene<GameScene>().DisconnectionRequested
                && (
                    world.ClientFeatures.Flags
                    & CharacterListFlags.CLF_OWERWRITE_CONFIGURATION_BUTTON
                ) != 0
            )
            {
                if (p.ReadBool())
                {
                    // client can disconnect
                    AsyncNetClient.Socket.Disconnect().Wait();
                    Client.Game.SetScene(new LoginScene(world));
                }
                else
                {
                    Log.Warn("0x1D - client asked to disconnect but server answered 'NO!'");
                }
            }
        }

    private static void GenericAOSCommandsR(World world, ref StackDataReader p) { }

    private static unsafe void ReadUnsafeCustomHouseData(
        ReadOnlySpan<byte> source,
        int sourcePosition,
        int dlen,
        int clen,
        int planeZ,
        int planeMode,
        short minX,
        short minY,
        short maxY,
        Item item,
        House house
    )
    {
        byte[] buffer = null;

        try
        {
            bool ismovable = item.ItemData.IsMultiMovable;

            Span<byte> span = dlen <= 1024
                            ? stackalloc byte[dlen]
                            : (buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(dlen));

            ZLib.ZLibError result = ZLib.Decompress(source.Slice(sourcePosition, clen), span.Slice(0, dlen));
            var reader = new StackDataReader(span.Slice(0, dlen));

            ushort id = 0;
            sbyte x = 0,
                y = 0,
                z = 0;

            switch (planeMode)
            {
                case 0:
                    int c = dlen / 5;

                    for (uint i = 0; i < c; i++)
                    {
                        id = reader.ReadUInt16BE();
                        x = reader.ReadInt8();
                        y = reader.ReadInt8();
                        z = reader.ReadInt8();

                        if (id != 0)
                        {
                            house.Add(
                                id,
                                0,
                                (ushort)(item.X + x),
                                (ushort)(item.Y + y),
                                (sbyte)(item.Z + z),
                                true,
                                ismovable
                            );
                        }
                    }

                    break;

                case 1:

                    if (planeZ > 0)
                    {
                        z = (sbyte)((planeZ - 1) % 4 * 20 + 7);
                    }
                    else
                    {
                        z = 0;
                    }

                    c = dlen >> 2;

                    for (uint i = 0; i < c; i++)
                    {
                        id = reader.ReadUInt16BE();
                        x = reader.ReadInt8();
                        y = reader.ReadInt8();

                        if (id != 0)
                        {
                            house.Add(
                                id,
                                0,
                                (ushort)(item.X + x),
                                (ushort)(item.Y + y),
                                (sbyte)(item.Z + z),
                                true,
                                ismovable
                            );
                        }
                    }

                    break;

                case 2:
                    short offX = 0,
                        offY = 0;
                    short multiHeight = 0;

                    if (planeZ > 0)
                    {
                        z = (sbyte)((planeZ - 1) % 4 * 20 + 7);
                    }
                    else
                    {
                        z = 0;
                    }

                    if (planeZ <= 0)
                    {
                        offX = minX;
                        offY = minY;
                        multiHeight = (short)(maxY - minY + 2);
                    }
                    else if (planeZ <= 4)
                    {
                        offX = (short)(minX + 1);
                        offY = (short)(minY + 1);
                        multiHeight = (short)(maxY - minY);
                    }
                    else
                    {
                        offX = minX;
                        offY = minY;
                        multiHeight = (short)(maxY - minY + 1);
                    }

                    c = dlen >> 1;

                    for (uint i = 0; i < c; i++)
                    {
                        id = reader.ReadUInt16BE();
                        x = (sbyte)(i / multiHeight + offX);
                        y = (sbyte)(i % multiHeight + offY);

                        if (id != 0)
                        {
                            house.Add(
                                id,
                                0,
                                (ushort)(item.X + x),
                                (ushort)(item.Y + y),
                                (sbyte)(item.Z + z),
                                true,
                                ismovable
                            );
                        }
                    }

                    break;
            }

            reader.Release();
        }
        finally
        {
            if (buffer != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static void CustomHouse(World world, ref StackDataReader p)
    {
        bool compressed = p.ReadUInt8() == 0x03;
        bool enableReponse = p.ReadBool();
        uint serial = p.ReadUInt32BE();
        Item foundation = world.Items.Get(serial);
        uint revision = p.ReadUInt32BE();

        if (foundation == null)
        {
            return;
        }

        Rectangle? multi = foundation.MultiInfo;

        if (!foundation.IsMulti || multi == null)
        {
            return;
        }

        p.Skip(4);

        if (!world.HouseManager.TryGetHouse(foundation, out House house))
        {
            house = new House(world, foundation, revision, true);
            world.HouseManager.Add(foundation, house);
        }
        else
        {
            house.ClearComponents(true);
            house.Revision = revision;
            house.IsCustom = true;
        }

        short minX = (short)multi.Value.X;
        short minY = (short)multi.Value.Y;
        short maxY = (short)multi.Value.Height;

        if (minX == 0 && minY == 0 && maxY == 0 && multi.Value.Width == 0)
        {
            Log.Warn(
                "[CustomHouse (0xD8) - Invalid multi dimentions. Maybe missing some installation required files"
            );

            return;
        }

        byte planes = p.ReadUInt8();

        house.ClearCustomHouseComponents(0);

        for (int plane = 0; plane < planes; plane++)
        {
            uint header = p.ReadUInt32BE();
            int dlen = (int)(((header & 0xFF0000) >> 16) | ((header & 0xF0) << 4));
            int clen = (int)(((header & 0xFF00) >> 8) | ((header & 0x0F) << 8));
            int planeZ = (int)((header & 0x0F000000) >> 24);
            int planeMode = (int)((header & 0xF0000000) >> 28);

            if (clen <= 0)
            {
                continue;
            }

            try
            {
                ReadUnsafeCustomHouseData(
                    p.Buffer,
                    p.Position,
                    dlen,
                    clen,
                    planeZ,
                    planeMode,
                    minX,
                    minY,
                    maxY,
                    foundation,
                    house
                );
            }
            catch (Exception e)
            {
                Log.Error($"Failed to read custom house data: {e}");
            }

            p.Skip(clen);
        }

        if (world.CustomHouseManager != null)
        {
            world.CustomHouseManager.GenerateFloorPlace();

            UIManager.GetGump<HouseCustomizationGump>(house.Serial)?.Update();
        }

        UIManager.GetGump<MiniMapGump>()?.RequestUpdateContents();

        if (world.HouseManager.EntityIntoHouse(serial, world.Player))
        {
            Client.Game.GetScene<GameScene>()?.UpdateMaxDrawZ(true);
        }

        world.BoatMovingManager.ClearSteps(serial);
    }

    private static void CharacterTransferLog(World world, ref StackDataReader p) { }

    private static void OPLInfo(World world, ref StackDataReader p)
    {
        if (world.ClientFeatures.TooltipsEnabled)
        {
            uint serial = p.ReadUInt32BE();
            uint revision = p.ReadUInt32BE();

            if (!world.OPL.IsRevisionEquals(serial, revision))
            {
                AddMegaClilocRequest(serial);
            }
        }
    }

    private static void OpenCompressedGump(World world, ref StackDataReader p)
    {
        uint sender = p.ReadUInt32BE();
        uint gumpID = p.ReadUInt32BE();
        uint x = p.ReadUInt32BE();
        uint y = p.ReadUInt32BE();
        uint layoutCompressedLen = p.ReadUInt32BE() - 4;
        int layoutDecompressedLen = (int)p.ReadUInt32BE();

        if (layoutDecompressedLen < 1)
        {
            Log.Error("[Initial]A bad compressed gump packet was received. Unable to process.");
            return;
        }

        byte[] layoutBuffer = new byte[layoutDecompressedLen]; //System.Buffers.ArrayPool<byte>.Shared.Rent(layoutDecompressedLen);
        string layout = null;

        try
        {
            ZLib.Decompress(p.Buffer.Slice(p.Position, (int)layoutCompressedLen), layoutBuffer.AsSpan(0, layoutDecompressedLen));
            layout = Encoding.UTF8.GetString(layoutBuffer.AsSpan(0, layoutDecompressedLen));
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to decompress or decode gump layout: {ex.Message}");
            return;
        }
        // finally
        // {
        //     System.Buffers.ArrayPool<byte>.Shared.Return(layoutBuffer);
        // }

        p.Skip((int)layoutCompressedLen);

        uint linesNum = p.ReadUInt32BE();
        string[] lines = new string[linesNum];

        try
        {
            if (linesNum != 0)
            {
                uint linesCompressedLen = p.ReadUInt32BE() - 4;
                int linesDecompressedLen = (int)p.ReadUInt32BE();

                if (linesDecompressedLen < 1)
                {
                    Log.Error("A bad compressed gump packet was received. Unable to process.");
                    return;
                }

                byte[] linesBuffer = new byte[linesDecompressedLen]; //System.Buffers.ArrayPool<byte>.Shared.Rent(linesDecompressedLen);

                ZLib.Decompress(p.Buffer.Slice(p.Position, (int)linesCompressedLen), linesBuffer.AsSpan(0, linesDecompressedLen));
                p.Skip((int)linesCompressedLen);

                var reader = new StackDataReader(linesBuffer.AsSpan(0, linesDecompressedLen));

                for (int i = 0; i < linesNum; ++i)
                {
                    int remaining = reader.Remaining;

                    if (remaining >= 2)
                    {
                        int length = reader.ReadUInt16BE();

                        if (length > 0)
                        {
                            lines[i] = reader.ReadUnicodeBE(length);
                        }
                        else
                        {
                            lines[i] = string.Empty;
                        }
                    }
                    else
                    {
                        lines[i] = string.Empty;
                    }
                }

                reader.Release();

                // finally
                // {
                //     System.Buffers.ArrayPool<byte>.Shared.Return(linesBuffer);
                // }
            }

            if (string.IsNullOrEmpty(layout))
            {
                Log.Error("Gump layout is null or empty. Unable to create gump.");
                return;
            }

            CreateGump(world, sender, gumpID, (int)x, (int)y, layout, lines);
        }
        catch (Exception e)
        {
            HtmlCrashLogGen.Generate(e.ToString(), description: "TazUO almost crashed, it was prevented but this was put in place for debugging, please post this on our discord.");
        }
    }

    private static void UpdateMobileStatus(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();
        byte status = p.ReadUInt8();

        if (status == 1)
        {
            uint attackerSerial = p.ReadUInt32BE();
        }
    }

    private static void BuffDebuff(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        const ushort BUFF_ICON_START = 0x03E9;
        const ushort BUFF_ICON_START_NEW = 0x466;

        uint serial = p.ReadUInt32BE();
        var ic = (BuffIconType)p.ReadUInt16BE();

        ushort iconID =
            (ushort)ic >= BUFF_ICON_START_NEW
                ? (ushort)(ic - (BUFF_ICON_START_NEW - 125))
                : (ushort)((ushort)ic - BUFF_ICON_START);

        if (iconID < BuffTable.Table.Length)
        {
            BuffGump gump = UIManager.GetGump<BuffGump>();
            ushort count = p.ReadUInt16BE();

            if (count == 0)
            {
                world.Player.RemoveBuff(ic);
                gump?.RequestUpdateContents();
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    ushort source_type = p.ReadUInt16BE();
                    p.Skip(2);
                    ushort icon = p.ReadUInt16BE();
                    ushort queue_index = p.ReadUInt16BE();
                    p.Skip(4);
                    ushort timer = p.ReadUInt16BE();
                    p.Skip(3);

                    uint titleCliloc = p.ReadUInt32BE();
                    uint descriptionCliloc = p.ReadUInt32BE();
                    uint wtfCliloc = p.ReadUInt32BE();

                    ushort arg_length = p.ReadUInt16BE();
                    string str = p.ReadUnicodeLE(2);
                    string args = str + p.ReadUnicodeLE();
                    string title = Client.Game.UO.FileManager.Clilocs.Translate(
                        (int)titleCliloc,
                        args,
                        true
                    );

                    arg_length = p.ReadUInt16BE();
                    string args_2 = p.ReadUnicodeLE();
                    string description = string.Empty;

                    if (descriptionCliloc != 0)
                    {
                        description =
                            "\n"
                            + Client.Game.UO.FileManager.Clilocs.Translate(
                                (int)descriptionCliloc,
                                String.IsNullOrEmpty(args_2) ? args : args_2,
                                true
                            );

                        if (description.Length < 2)
                        {
                            description = string.Empty;
                        }
                    }

                    arg_length = p.ReadUInt16BE();
                    string args_3 = p.ReadUnicodeLE();
                    string wtf = string.Empty;

                    if (wtfCliloc != 0)
                    {
                        wtf = Client.Game.UO.FileManager.Clilocs.Translate(
                            (int)wtfCliloc,
                            String.IsNullOrEmpty(args_3) ? args : args_3,
                            true
                        );

                        if (!string.IsNullOrWhiteSpace(wtf))
                        {
                            wtf = $"\n{wtf}";
                        }
                    }

                    string text = $"<left>{title}{description}{wtf}</left>";
                    bool alreadyExists = world.Player.IsBuffIconExists(ic);
                    world.Player.AddBuff(ic, BuffTable.Table[iconID], timer, text, title);

                    if (!alreadyExists)
                    {
                        gump?.RequestUpdateContents();
                    }
                }
            }
        }
    }

    private static void NewCharacterAnimation(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        Mobile mobile = world.Mobiles.Get(p.ReadUInt32BE());

        if (mobile == null)
        {
            return;
        }

        ushort type = p.ReadUInt16BE();
        ushort action = p.ReadUInt16BE();
        byte mode = p.ReadUInt8();
        byte group = Mobile.GetObjectNewAnimation(mobile, type, action, mode);

        mobile.SetAnimation(
            group,
            repeatCount: 1,
            repeat: (type == 1 || type == 2) && mobile.Graphic == 0x0015,
            forward: true,
            fromServer: true
        );
    }

    private static void KREncryptionResponse(World world, ref StackDataReader p) { }

    private static void DisplayWaypoint(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        sbyte z = p.ReadInt8();
        byte map = p.ReadUInt8();
        var type = (WaypointsType)p.ReadUInt16BE();
        bool ignoreobject = p.ReadUInt16BE() != 0;
        uint cliloc = p.ReadUInt32BE();
        string name = p.ReadUnicodeLE();

        Log.Info($"Waypoint received: {type} - {name}");

        switch (type)
        {
            case WaypointsType.Corpse:
                world.WMapManager.AddOrUpdate(serial, x, y, 0, map, true, "Corpse");
                break;
            case WaypointsType.PartyMember:
                break;
            case WaypointsType.RallyPoint:
                break;
            case WaypointsType.QuestGiver:
                break;
            case WaypointsType.QuestDestination:
                break;
            case WaypointsType.Resurrection:
                world.WMapManager.AddOrUpdate(serial, x, y, 0, map, true, "Resurrection");
                break;
            case WaypointsType.PointOfInterest:
                break;
            case WaypointsType.Landmark:
                break;
            case WaypointsType.Town:
                break;
            case WaypointsType.Dungeon:
                break;
            case WaypointsType.Moongate:
                break;
            case WaypointsType.Shop:
                break;
            case WaypointsType.Player:
                break;
        }
    }

    private static void RemoveWaypoint(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();

        world.WMapManager.Remove(serial);
    }

    private static void KrriosClientSpecial(World world, ref StackDataReader p)
    {
        byte type = p.ReadUInt8();

        switch (type)
        {
            case 0x00: // accepted
                Log.Trace("Krrios special packet accepted");
                world.WMapManager.SetACKReceived();
                world.WMapManager.SetEnable(true);

                break;

            case 0x01: // custom party info
            case 0x02: // guild track info
                bool locations = type == 0x01 || p.ReadBool();

                uint serial;

                while ((serial = p.ReadUInt32BE()) != 0)
                {
                    if (locations)
                    {
                        ushort x = p.ReadUInt16BE();
                        ushort y = p.ReadUInt16BE();
                        byte map = p.ReadUInt8();
                        int hits = type == 1 ? 0 : p.ReadUInt8();

                        world.WMapManager.AddOrUpdate(
                            serial,
                            x,
                            y,
                            hits,
                            map,
                            type == 0x02,
                            null,
                            true
                        );

                        if (type == 0x02) //is guild member
                        {
                            Entity ent = world.Get(serial);
                            if (ent != null && !string.IsNullOrEmpty(ent.Name))
                                _ = FriendliesSQLManager.Instance.AddAsync(ent.Serial, ent.Name);

                        }
                    }
                }

                world.WMapManager.RemoveUnupdatedWEntity();

                break;

            case 0x03: // runebook contents
                break;

            case 0x04: // guardline data
                break;

            case 0xF0:
                break;

            case 0xFE:

                Client.Game.EnqueueAction(5000, () =>
                {
                    Log.Info("Razor ACK sent");
                    AsyncNetClient.Socket.Send_RazorACK();
                });

                break;
        }
    }

    private static void FreeshardListR(World world, ref StackDataReader p) { }

    private static void UpdateItemSA(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        p.Skip(2);
        byte type = p.ReadUInt8();
        uint serial = p.ReadUInt32BE();
        ushort graphic = p.ReadUInt16BE();
        byte graphicInc = p.ReadUInt8();
        ushort amount = p.ReadUInt16BE();
        ushort unk = p.ReadUInt16BE();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        sbyte z = p.ReadInt8();
        var dir = (Direction)p.ReadUInt8();
        ushort hue = p.ReadUInt16BE();
        var flags = (Flags)p.ReadUInt8();
        ushort unk2 = p.ReadUInt16BE();

        if (serial != world.Player)
        {
            UpdateGameObject(
                world,
                serial,
                graphic,
                graphicInc,
                amount,
                x,
                y,
                z,
                dir,
                hue,
                flags,
                unk,
                type,
                unk2
            );

            if (graphic == 0x2006 && ProfileManager.CurrentProfile.AutoOpenCorpses)
            {
                world.Player.TryOpenCorpses();
            }
        }
        else if (p[0] == 0xF7)
        {
            UpdatePlayer(world, serial, graphic, graphicInc, hue, flags, x, y, z, 0, dir);
        }
    }

    private static void BoatMoving(World world, ref StackDataReader p)
    {
        if (!world.InGame)
        {
            return;
        }

        uint serial = p.ReadUInt32BE();
        byte boatSpeed = p.ReadUInt8();
        Direction movingDirection = (Direction)p.ReadUInt8() & Direction.Mask;
        Direction facingDirection = (Direction)p.ReadUInt8() & Direction.Mask;
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        ushort z = p.ReadUInt16BE();

        Item multi = world.Items.Get(serial);

        if (multi == null)
        {
            return;
        }

        //multi.LastX = x;
        //multi.LastY = y;

        //if (World.HouseManager.TryGetHouse(serial, out var house))
        //{
        //    foreach (Multi component in house.Components)
        //    {
        //        component.LastX = (ushort) (x + component.MultiOffsetX);
        //        component.LastY = (ushort) (y + component.MultiOffsetY);
        //    }
        //}

        bool smooth =
            ProfileManager.CurrentProfile != null
            && ProfileManager.CurrentProfile.UseSmoothBoatMovement;

        if (smooth)
        {
            world.BoatMovingManager.AddStep(
                serial,
                boatSpeed,
                movingDirection,
                facingDirection,
                x,
                y,
                (sbyte)z
            );
        }
        else
        {
            //UpdateGameObject(serial,
            //                 multi.Graphic,
            //                 0,
            //                 multi.Amount,
            //                 x,
            //                 y,
            //                 (sbyte) z,
            //                 facingDirection,
            //                 multi.Hue,
            //                 multi.Flags,
            //                 0,
            //                 2,
            //                 1);

            multi.SetInWorldTile(x, y, (sbyte)z);

            if (world.HouseManager.TryGetHouse(serial, out House house))
            {
                house.Generate(true, true, true);
            }
        }

        int count = p.ReadUInt16BE();

        for (int i = 0; i < count; i++)
        {
            uint cSerial = p.ReadUInt32BE();
            ushort cx = p.ReadUInt16BE();
            ushort cy = p.ReadUInt16BE();
            ushort cz = p.ReadUInt16BE();

            if (cSerial == world.Player)
            {
                world.RangeSize.X = cx;
                world.RangeSize.Y = cy;
            }

            Entity ent = world.Get(cSerial);

            if (ent == null)
            {
                continue;
            }

            //if (SerialHelper.IsMobile(cSerial))
            //{
            //    Mobile m = (Mobile) ent;

            //    if (m.Steps.Count != 0)
            //    {
            //        ref var step = ref m.Steps.Back();

            //        step.X = cx;
            //        step.Y = cy;
            //    }
            //}

            //ent.LastX = cx;
            //ent.LastY = cy;

            if (smooth)
            {
                world.BoatMovingManager.PushItemToList(
                    serial,
                    cSerial,
                    x - cx,
                    y - cy,
                    (sbyte)(z - cz)
                );
            }
            else
            {
                if (cSerial == world.Player)
                {
                    UpdatePlayer(
                        world,
                        cSerial,
                        ent.Graphic,
                        0,
                        ent.Hue,
                        ent.Flags,
                        cx,
                        cy,
                        (sbyte)cz,
                        0,
                        world.Player.Direction
                    );
                }
                else
                {
                    UpdateGameObject(
                        world,
                        cSerial,
                        ent.Graphic,
                        0,
                        (ushort)(ent.Graphic == 0x2006 ? ((Item)ent).Amount : 0),
                        cx,
                        cy,
                        (sbyte)cz,
                        SerialHelper.IsMobile(ent) ? ent.Direction : 0,
                        ent.Hue,
                        ent.Flags,
                        0,
                        0,
                        1
                    );
                }
            }
        }
    }

    private static void PacketList(World world, ref StackDataReader p)
    {
        if (world.Player == null)
        {
            return;
        }

        int count = p.ReadUInt16BE();

        for (int i = 0; i < count; i++)
        {
            byte id = p.ReadUInt8();

            if (id == 0xF3)
            {
                UpdateItemSA(world, ref p);
            }
            else
            {
                Log.Warn($"Unknown packet ID: [0x{id:X2}] in 0xF7");

                break;
            }
        }
    }

    private static void ServerListReceived(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.ServerListReceived(ref p);
    }

    private static void ReceiveServerRelay(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.HandleRelayServerPacket(ref p, Settings.GlobalSettings.IgnoreRelayIp);
    }

    private static void UpdateCharacterList(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.UpdateCharacterList(ref p);
    }

    private static void ReceiveCharacterList(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.ReceiveCharacterList(ref p);
    }

    private static void LoginDelay(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.HandleLoginDelayPacket(ref p);
    }

    private static void ReceiveLoginRejection(World world, ref StackDataReader p)
    {
        if (world.InGame)
        {
            return;
        }

        LoginHandshake.Instance?.HandleErrorCode(ref p);
    }

    private static Gump CreateGump(
        World world,
        uint sender,
        uint gumpID,
        int x,
        int y,
        string layout,
        string[] lines
    )
    {
        ScriptRecorder.Instance.RecordWaitForGump(gumpID.ToString());
        ScriptingInfoGump.AddOrUpdateInfo("Last Gump Opened", $"0x{gumpID:X}");

        if (string.IsNullOrEmpty(layout))
            return null;

        List<string> cmdlist = GetParser().GetTokens(layout);
        int cmdlen = cmdlist.Count;

        if (cmdlen <= 0)
            return null;

        Gump gump = UIManager.GetGumpServer(gumpID);

        if (gump != null && (gump.IsDisposed || gump.LocalSerial != sender))
            gump = null;

        bool mustBeAdded = gump == null;

        if (UIManager.GetGumpCachePosition(gumpID, out Point pos))
        {
            x = pos.X;
            y = pos.Y;
        }
        else
            UIManager.SavePosition(gumpID, new Point(x, y));

        if(mustBeAdded)
            gump = new Gump(world, sender, gumpID)
            {
                X = x,
                Y = y,
                CanMove = true,
                CanCloseWithRightClick = true,
                CanCloseWithEsc = true,
                InvalidateContents = false,
                IsFromServer = true
            };
        else
        {
            //Reusing existing gump, need to clear it out
            gump.Clear();
            gump.CleanUpDisposedChildren();
        }

        var gumpTextBuilder = new StringBuilder(string.Join("\n", lines));

        int group = 0;
        int page = 0;

        bool textBoxFocused = false;

        for (int cnt = 0; cnt < cmdlen; cnt++)
        {
            List<string> gparams = GetCmdParser().GetTokens(cmdlist[cnt], false);

            if (gparams.Count == 0)
            {
                continue;
            }

            string entry = gparams[0];
            gumpTextBuilder.Append(string.Join(" ", gparams)).Append('\n');

            if (string.Equals(entry, "button", StringComparison.InvariantCultureIgnoreCase))
            {
                gump.Add(new Button(gparams), page);
            }
            else if (
                string.Equals(
                    entry,
                    "buttontileart",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                gump.Add(new ButtonTileArt(gparams), page);
            }
            else if (
                string.Equals(
                    entry,
                    "checkertrans",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                var checkerTrans = new CheckerTrans(gparams);
                gump.Add(checkerTrans, page);
                ApplyTrans(
                    gump,
                    page,
                    checkerTrans.X,
                    checkerTrans.Y,
                    checkerTrans.Width,
                    checkerTrans.Height
                );
            }
            else if (
                string.Equals(entry, "croppedtext", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(new CroppedText(gparams, lines), page);
            }
            else if (
                string.Equals(entry, "tilepicasgumppic", StringComparison.InvariantCultureIgnoreCase) ||
                string.Equals(entry, "gumppic", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                GumpPic pic;
                bool isVirtue = gparams.Count >= 6
                    && gparams[5].IndexOf(
                        "virtuegumpitem",
                        StringComparison.InvariantCultureIgnoreCase
                    ) >= 0;

                if (isVirtue)
                {
                    pic = new VirtueGumpPic(world, gparams);
                    pic.ContainsByBounds = true;

                    string s,
                        lvl;

                    switch (pic.Hue)
                    {
                        case 2403:
                            lvl = "";

                            break;

                        case 1154:
                        case 1547:
                        case 2213:
                        case 235:
                        case 18:
                        case 2210:
                        case 1348:
                            lvl = "Seeker of ";

                            break;

                        case 2404:
                        case 1552:
                        case 2216:
                        case 2302:
                        case 2118:
                        case 618:
                        case 2212:
                        case 1352:
                            lvl = "Follower of ";

                            break;

                        case 43:
                        case 53:
                        case 1153:
                        case 33:
                        case 318:
                        case 67:
                        case 98:
                            lvl = "Knight of ";

                            break;

                        case 2406:
                            if (pic.Graphic == 0x6F)
                            {
                                lvl = "Seeker of ";
                            }
                            else
                            {
                                lvl = "Knight of ";
                            }

                            break;

                        default:
                            lvl = "";

                            break;
                    }

                    switch (pic.Graphic)
                    {
                        case 0x69:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 2);

                            break;

                        case 0x6A:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 7);

                            break;

                        case 0x6B:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 5);

                            break;

                        case 0x6D:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 6);

                            break;

                        case 0x6E:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 1);

                            break;

                        case 0x6F:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 3);

                            break;

                        case 0x70:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000 + 4);

                            break;

                        case 0x6C:
                        default:
                            s = Client.Game.UO.FileManager.Clilocs.GetString(1051000);

                            break;
                    }

                    if (string.IsNullOrEmpty(s))
                    {
                        s = "Unknown virtue";
                    }

                    pic.SetTooltip(lvl + s, 100);
                }
                else
                {
                    pic = new GumpPic(gparams);
                }

                gump.Add(pic, page);
            }
            else if (
                string.Equals(
                    entry,
                    "gumppictiled",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                gump.Add(new GumpPicTiled(gparams), page);
            }
            else if (
                string.Equals(entry, "htmlgump", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(new HtmlControl(gparams, lines), page);
            }
            else if (
                string.Equals(entry, "xmfhtmlgump", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(
                    new HtmlControl(
                        int.Parse(gparams[1]),
                        int.Parse(gparams[2]),
                        int.Parse(gparams[3]),
                        int.Parse(gparams[4]),
                        int.Parse(gparams[6]) == 1,
                        int.Parse(gparams[7]) != 0,
                        gparams[6] != "0" && gparams[7] == "2",
                        Client.Game.UO.FileManager.Clilocs.GetString(int.Parse(gparams[5].Replace("#", ""))),
                        0,
                        true
                    )
                    {
                        IsFromServer = true
                    },
                    page
                );
            }
            else if (
                string.Equals(
                    entry,
                    "xmfhtmlgumpcolor",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                int color = int.Parse(gparams[8]);

                if (color == 0x7FFF)
                {
                    color = 0x00FFFFFF;
                }

                gump.Add(
                    new HtmlControl(
                        int.Parse(gparams[1]),
                        int.Parse(gparams[2]),
                        int.Parse(gparams[3]),
                        int.Parse(gparams[4]),
                        int.Parse(gparams[6]) == 1,
                        int.Parse(gparams[7]) != 0,
                        gparams[6] != "0" && gparams[7] == "2",
                        Client.Game.UO.FileManager.Clilocs.GetString(int.Parse(gparams[5].Replace("#", ""))),
                        color,
                        true
                    )
                    {
                        IsFromServer = true
                    },
                    page
                );
            }
            else if (
                string.Equals(entry, "xmfhtmltok", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                int color = int.Parse(gparams[7]);

                if (color == 0x7FFF)
                {
                    color = 0x00FFFFFF;
                }

                StringBuilder sb = null;

                if (gparams.Count >= 9)
                {
                    sb = new StringBuilder();

                    for (int i = 9; i < gparams.Count; i++)
                    {
                        sb.Append('\t');
                        sb.Append(gparams[i]);
                    }
                }

                gump.Add(
                    new HtmlControl(
                        int.Parse(gparams[1]),
                        int.Parse(gparams[2]),
                        int.Parse(gparams[3]),
                        int.Parse(gparams[4]),
                        int.Parse(gparams[5]) == 1,
                        int.Parse(gparams[6]) != 0,
                        gparams[5] != "0" && gparams[6] == "2",
                        sb == null
                            ? Client.Game.UO.FileManager.Clilocs.GetString(
                                int.Parse(gparams[8].Replace("#", ""))
                            )
                            : Client.Game.UO.FileManager.Clilocs.Translate(
                                int.Parse(gparams[8].Replace("#", "")),
                                sb.ToString().Trim('@').Replace('@', '\t')
                            ),
                        color,
                        true
                    )
                    {
                        IsFromServer = true
                    },
                    page
                );
            }
            else if (string.Equals(entry, "page", StringComparison.InvariantCultureIgnoreCase))
            {
                if (gparams.Count >= 2)
                {
                    page = int.Parse(gparams[1]);
                }
            }
            else if (
                string.Equals(entry, "resizepic", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(new ResizePic(gparams), page);
            }
            else if (string.Equals(entry, "text", StringComparison.InvariantCultureIgnoreCase))
            {
                if (gparams.Count >= 5)
                {
                    gump.Add(new Label(gparams, lines), page);
                }
            }
            else if (
                string.Equals(
                    entry,
                    "textentrylimited",
                    StringComparison.InvariantCultureIgnoreCase
                )
                || string.Equals(
                    entry,
                    "textentry",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                var textBox = new StbTextBox(gparams, lines);

                if (!textBoxFocused)
                {
                    textBox.SetKeyboardFocus();
                    textBoxFocused = true;
                }

                gump.Add(textBox, page);
            }
            else if (
                string.Equals(entry, "tilepichue", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(entry, "tilepic", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(new StaticPic(gparams), page);
            }
            else if (
                string.Equals(entry, "noclose", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.CanCloseWithRightClick = false;
            }
            else if (
                string.Equals(entry, "nodispose", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.CanCloseWithEsc = false;
            }
            else if (
                string.Equals(entry, "nomove", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.CanMove = false;
            }
            else if (
                string.Equals(entry, "group", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(entry, "endgroup", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                group++;
            }
            else if (string.Equals(entry, "radio", StringComparison.InvariantCultureIgnoreCase))
            {
                gump.Add(new RadioButton(group, gparams, lines), page);
            }
            else if (
                string.Equals(entry, "checkbox", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.Add(new Checkbox(gparams, lines), page);
            }
            else if (
                string.Equals(entry, "tooltip", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                string text = null;

                if (gparams.Count > 2 && gparams[2].Length != 0)
                {
                    string args = gparams[2];

                    for (int i = 3; i < gparams.Count; i++)
                    {
                        args += '\t' + gparams[i];
                    }

                    if (args.Length == 0)
                    {
                        text = Client.Game.UO.FileManager.Clilocs.GetString(int.Parse(gparams[1]));
                        Log.Error(
                            $"String '{args}' too short, something wrong with gump tooltip: {text}"
                        );
                    }
                    else
                    {
                        text = Client.Game.UO.FileManager.Clilocs.Translate(
                            int.Parse(gparams[1]),
                            args,
                            false
                        );
                    }
                }
                else
                {
                    text = Client.Game.UO.FileManager.Clilocs.GetString(int.Parse(gparams[1]));
                }

                Control last =
                    gump.Children.Count != 0 ? gump.Children[gump.Children.Count - 1] : null;

                if (last != null)
                {
                    if (last.HasTooltip)
                    {
                        if (last.Tooltip is string s)
                        {
                            s += '\n' + text;
                            last.SetTooltip(s);
                        }
                    }
                    else
                    {
                        last.SetTooltip(text);
                    }

                    last.Priority = ClickPriority.High;
                    last.AcceptMouseInput = true;
                }
            }
            else if (
                string.Equals(
                    entry,
                    "itemproperty",
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                if (world.ClientFeatures.TooltipsEnabled && gump.Children.Count != 0)
                {
                    gump.Children[gump.Children.Count - 1].SetTooltip(
                        SerialHelper.Parse(gparams[1])
                    );

                    if (
                        uint.TryParse(gparams[1], out uint s)
                        && (!world.OPL.TryGetRevision(s, out uint rev) || rev == 0)
                    )
                    {
                        AddMegaClilocRequest(s);
                    }
                }
            }
            else if (
                string.Equals(entry, "noresize", StringComparison.InvariantCultureIgnoreCase)
            ) { }
            else if (
                string.Equals(entry, "mastergump", StringComparison.InvariantCultureIgnoreCase)
            )
            {
                gump.MasterGumpSerial = gparams.Count > 0 ? SerialHelper.Parse(gparams[1]) : 0;
            }
            else if (string.Equals(entry, "picinpichued", StringComparison.InvariantCultureIgnoreCase) ||
                     string.Equals(entry, "picinpicphued", StringComparison.InvariantCultureIgnoreCase) ||
                     string.Equals(entry, "picinpic", StringComparison.InvariantCultureIgnoreCase)
                    )
            {
                if (gparams.Count > 7)
                {
                    gump.Add(new GumpPicInPic(gparams), page);
                }
            }
            else if (string.Equals(entry, "\0", StringComparison.InvariantCultureIgnoreCase))
            {
                //This gump is null terminated: Breaking
                break;
            }
            else if (string.Equals(entry, "gumppichued", StringComparison.InvariantCultureIgnoreCase) ||
                     string.Equals(entry, "gumppicphued", StringComparison.InvariantCultureIgnoreCase))
            {
                if (gparams.Count >= 3)
                    gump.Add(new GumpPic(gparams));
            }
            else if (string.Equals(entry, "togglelimitgumpscale", StringComparison.InvariantCultureIgnoreCase))
            {
                // ??
            }
            else if (string.Equals(entry, "maparea", StringComparison.InvariantCultureIgnoreCase))
            {
                if(gparams.Count >= 10)
                    if(int.TryParse(gparams[1], out int cx) &&
                        int.TryParse(gparams[2], out int cy) &&
                        int.TryParse(gparams[3], out int width) &&
                        int.TryParse(gparams[4], out int height) &&
                        int.TryParse(gparams[5], out int mapindex) &&
                        int.TryParse(gparams[6], out int mapx)&&
                        int.TryParse(gparams[7], out int mapy)&&
                        int.TryParse(gparams[8], out int mapex)&&
                        int.TryParse(gparams[9], out int mapey))
                    {
                        gump.Add(new RenderedMapArea(mapindex, new Rectangle(mapx, mapy, mapex - mapx, mapey - mapy), cx, cy, width, height), page);
                    }
            }
            else
            {
                Log.Warn($"Invalid Gump Command: \"{gparams[0]}\"");
            }
        }

        gump.PacketGumpText = gumpTextBuilder.ToString();

        if (mustBeAdded)
        {
            UIManager.Add(gump);
        }

        gump.Update();
        gump.SetInScreen();

        if (CUOEnviroment.Debug)
        {
            GameActions.Print(world, $"GumpID: {gumpID}");
        }

        if (ProfileManager.CurrentProfile != null)
        {
            if (gumpID == ProfileManager.CurrentProfile.SOSGumpID) //SOS message gump
            {
                for (int i = 0; i < gump.Children.Count; i++)
                {
                    if (gump.Children[i] is HtmlControl)
                    {
                        string pattern = @"(\d+('N)?('S)?('E)?('W)?)";

                        string[] loc = new string[4];

                        int c = 0;
                        foreach (Match m in Regex.Matches(((HtmlControl)gump.Children[i]).Text, pattern, RegexOptions.Multiline))
                        {
                            if (c > 3)
                                break;
                            loc[c] = m.Value.Replace("'", "");
                            c++;
                        }

                        if (loc[0] == null || loc[1] == null || loc[2] == null || loc[3] == null)
                            break;

                        int xlong = 0, ylat = 0, xmins = 0, ymins = 0;
                        bool xeast = true, ysouth = true;

                        if (loc[1].Contains("N"))
                            ysouth = false;

                        if (loc[3].Contains("W"))
                            xeast = false;

                        xlong = int.Parse(loc[2]);
                        ylat = int.Parse(loc[0]);
                        xmins = int.Parse(loc[3].Substring(0, loc[3].Length - 1)); ;
                        ymins = int.Parse(loc[1].Substring(0, loc[1].Length - 1));
                        Vector3 location = ReverseLookup(xlong, ylat, xmins, ymins, xeast, ysouth);
                        GameActions.Print(world, $"If I am on the correct facet I think these coords should be somewhere near.. {location.X} and {location.Y}..");

                        var menu = new MenuButton(25, Color.Black.PackedValue, 0.75f, "Menu") { X = gump.Width - 46, Y = 6 };
                        menu.MouseUp += (s, e) =>
                        {
                            menu.ContextMenu?.Show();
                        };

                        menu.ContextMenu = new ContextMenuControl(gump);
                        menu.ContextMenu.Add(new ContextMenuItemEntry("Locate on world map", () =>
                        {
                            WorldMapGump gump = UIManager.GetGump<WorldMapGump>();
                            if (gump == null)
                            {
                                gump = new WorldMapGump(world);
                                UIManager.Add(gump);
                            }
                            gump.GoToMarker((int)location.X, (int)location.Y, true);
                        }));

                        menu.ContextMenu.Add(new ContextMenuItemEntry("Add marker on world map", () =>
                        {
                            WorldMapGump gump = UIManager.GetGump<WorldMapGump>();
                            if (gump == null)
                            {
                                gump = new WorldMapGump(world);
                                UIManager.Add(gump);
                            }
                            gump.AddUserMarker("SOS", (int)location.X, (int)location.Y, world.Map.Index);
                        }));

                        menu.ContextMenu.Add(new ContextMenuItemEntry("Close", () =>
                        {
                            gump.Dispose();
                        }));
                        gump.Add(menu);
                    }
                }
            }
        }

        if (world.Player != null)
        {
            world.Player.LastGumpID = gumpID;
        }

        if (gump.X == 0 && gump.Y == 0)
        {
            gump.CenterXInViewPort();
            gump.CenterYInViewPort();
        }

        NextGumpConfig.Apply(gump);

        return gump;
    }

    public static Vector3 ReverseLookup(int xLong, int yLat, int xMins, int yMins, bool xEast, bool ySouth)
    {
        int xCenter, yCenter;
        int xWidth, yHeight;

        xCenter = 1323;
        yCenter = 1624;
        xWidth = 5120;
        yHeight = 4096;

        double absLong = xLong + ((double)xMins / 60);
        double absLat = yLat + ((double)yMins / 60);

        if (!xEast)
            absLong = 360.0 - absLong;

        if (!ySouth)
            absLat = 360.0 - absLat;

        int x, y;

        x = xCenter + (int)((absLong * xWidth) / 360);
        y = yCenter + (int)((absLat * yHeight) / 360);

        if (x < 0)
            x += xWidth;
        else if (x >= xWidth)
            x -= xWidth;

        if (y < 0)
            y += yHeight;
        else if (y >= yHeight)
            y -= yHeight;

        return new Vector3(x, y, 0);
    }

    private static void ApplyTrans(Gump gump, int current_page, int x, int y, int width, int height)
    {
        int x2 = x + width;
        int y2 = y + height;
        for (int i = 0; i < gump.Children.Count; i++)
        {
            Control child = gump.Children[i];
            bool canDraw = child.Page == 0 || current_page == child.Page;

            bool overlap =
                (x < child.X + child.Width)
                && (child.X < x2)
                && (y < child.Y + child.Height)
                && (child.Y < y2);

            if (canDraw && child.IsVisible && overlap)
            {
                child.Alpha = 0.5f;
            }
        }
    }

    [Flags]
    private enum AffixType
    {
        Append = 0x00,
        Prepend = 0x01,
        System = 0x02
    }
}
