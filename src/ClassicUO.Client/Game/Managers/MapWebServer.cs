using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.Managers
{
    internal class MapWebServer : IDisposable
    {
        private HttpListener _httpListener;
        private Thread _listenerThread;
        private volatile bool _isRunning;
        private int _port = 8088;
        private readonly object _clientsLock = new object();
        private readonly List<HttpListenerResponse> _activeClients = new List<HttpListenerResponse>();
        private byte[] _cachedMapPng = null;
        private readonly object _cacheLock = new object();

        public bool IsRunning => _isRunning;
        public int Port => _port;

        public void SetCachedMapPng(byte[] pngData, int mapIndex)
        {
            lock (_cacheLock)
            {
                _cachedMapPng = pngData;
            }
            Log.Info($"Map PNG cached: {pngData?.Length ?? 0} bytes for map {mapIndex}");
        }

        public bool Start(int port = 8088)
        {
            if (_isRunning)
                return false;

            _port = port;

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{_port}/");
                _httpListener.Start();
                _isRunning = true;

                _listenerThread = new Thread(ListenerLoop)
                {
                    IsBackground = true,
                    Name = "MapWebServer"
                };
                _listenerThread.Start();

                Log.Info($"Map Web Server started on http://localhost:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start Map Web Server: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch (Exception ex)
            {
                Log.Error($"Error stopping Map Web Server: {ex.Message}");
            }

            Log.Info("Map Web Server stopped");
        }

        private void ListenerLoop()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = _httpListener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Expected when stopping the listener
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"Map Web Server error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath;

                switch (path)
                {
                    case "/":
                        ServeHtmlPage(context.Response);
                        break;
                    case "/api/mapdata":
                        ServeMapData(context.Response);
                        break;
                    case "/api/maptexture":
                        ServeMapTexture(context.Response);
                        break;
                    case "/api/events":
                        ServeEventStream(context.Response);
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling request: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        private void ServeHtmlPage(HttpListenerResponse response)
        {
            string html = GetHtmlPage();
            byte[] buffer = Encoding.UTF8.GetBytes(html);

            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private void ServeMapData(HttpListenerResponse response)
        {
            if (World.Instance == null || !World.Instance.InGame)
            {
                response.StatusCode = 503;
                response.Close();
                return;
            }

            Texture2D mapTexture = UI.Gumps.WorldMapGump.GetMapTextureForMap(World.Instance.MapIndex);

            var data = new
            {
                mapIndex = World.Instance.MapIndex,
                mapWidth = mapTexture?.Width ?? 0,
                mapHeight = mapTexture?.Height ?? 0,
                player = new
                {
                    x = World.Instance.Player?.X ?? 0,
                    y = World.Instance.Player?.Y ?? 0,
                    name = World.Instance.Player?.Name ?? ""
                },
                party = GetPartyData(),
                guild = GetGuildData(),
                markers = GetMarkersData()
            };

            string json = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private void ServeMapTexture(HttpListenerResponse response)
        {
            try
            {
                byte[] imageData = null;

                lock (_cacheLock)
                {
                    imageData = _cachedMapPng;
                }

                if (imageData == null)
                {
                    Log.Warn("Map texture not cached");
                    response.StatusCode = 404;
                    byte[] errorMsg = Encoding.UTF8.GetBytes("Map texture not loaded. Please close and reopen the web map.");
                    response.ContentType = "text/plain";
                    response.ContentLength64 = errorMsg.Length;
                    response.OutputStream.Write(errorMsg, 0, errorMsg.Length);
                    response.Close();
                    return;
                }

                response.ContentType = "image/png";
                response.ContentLength64 = imageData.Length;
                response.OutputStream.Write(imageData, 0, imageData.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                Log.Error($"Error serving map texture: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedMapPng = null;
            }
        }

        private void ServeEventStream(HttpListenerResponse response)
        {
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            lock (_clientsLock)
            {
                _activeClients.Add(response);
            }

            try
            {
                // Keep connection alive and send updates
                while (_isRunning && World.Instance != null && World.Instance.InGame)
                {
                    var data = new
                    {
                        player = new
                        {
                            x = World.Instance.Player?.X ?? 0,
                            y = World.Instance.Player?.Y ?? 0,
                            name = World.Instance.Player?.Name ?? ""
                        },
                        party = GetPartyData(),
                        guild = GetGuildData(),
                        markers = GetMarkersData()
                    };

                    string json = JsonSerializer.Serialize(data);
                    string message = $"data: {json}\n\n";
                    byte[] buffer = Encoding.UTF8.GetBytes(message);

                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Flush();

                    Thread.Sleep(500); // Update twice per second
                }
            }
            catch
            {
                // Client disconnected
            }
            finally
            {
                lock (_clientsLock)
                {
                    _activeClients.Remove(response);
                }
                try { response.Close(); } catch { }
            }
        }

        private object GetPartyData()
        {
            var partyMembers = new List<object>();

            if (World.Instance?.Party != null && World.Instance.Party.Members != null)
            {
                foreach (PartyMember member in World.Instance.Party.Members)
                {
                    if (member == null || member.Serial == World.Instance.Player?.Serial)
                        continue;

                    Mobile mobile = World.Instance.Mobiles.Get(member.Serial);
                    if (mobile != null && !mobile.IsDestroyed)
                    {
                        WMapEntity wme = World.Instance.WMapManager.GetEntity(member.Serial);
                        partyMembers.Add(new
                        {
                            x = mobile.X,
                            y = mobile.Y,
                            name = member.Name,
                            isGuild = wme != null && wme.IsGuild,
                            map = World.Instance.MapIndex
                        });
                    }
                }
            }

            return partyMembers;
        }

        private List<object> GetGuildData()
        {
            var guildMembers = new List<object>();

            if (World.Instance?.WMapManager != null && World.Instance.WMapManager.Entities != null)
            {
                foreach (WMapEntity wme in World.Instance.WMapManager.Entities.Values)
                {
                    if (wme.IsGuild && !World.Instance.Party.Contains(wme.Serial))
                    {
                        guildMembers.Add(new
                        {
                            x = wme.X,
                            y = wme.Y,
                            name = wme.Name ?? "<out of range>",
                            map = wme.Map
                        });
                    }
                }
            }

            return guildMembers;
        }

        private List<object> GetMarkersData()
        {
            var markers = new List<object>();

            if (UI.Gumps.WorldMapGump._markerFiles != null)
            {
                foreach (UI.Gumps.WorldMapGump.WMapMarkerFile markerFile in UI.Gumps.WorldMapGump._markerFiles)
                {
                    if (markerFile.Hidden || markerFile.Markers == null)
                        continue;

                    foreach (UI.Gumps.WorldMapGump.WMapMarker marker in markerFile.Markers)
                    {
                        if (marker.MapId == World.Instance.MapIndex)
                        {
                            markers.Add(new
                            {
                                x = marker.X,
                                y = marker.Y,
                                name = marker.Name,
                                color = new
                                {
                                    r = marker.Color.R,
                                    g = marker.Color.G,
                                    b = marker.Color.B,
                                    a = marker.Color.A
                                },
                                iconName = marker.MarkerIconName
                            });
                        }
                    }
                }
            }

            return markers;
        }

        private string GetHtmlPage() => @"<!DOCTYPE html>
<html>
<head>
    <title>TazUO World Map</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: Arial, sans-serif;
            background: #1a1a1a;
            color: #fff;
            overflow: hidden;
        }
        #controls {
            position: fixed;
            top: 10px;
            left: 10px;
            background: rgba(0,0,0,0.8);
            padding: 15px;
            border-radius: 8px;
            z-index: 1000;
            box-shadow: 0 4px 6px rgba(0,0,0,0.5);
        }
        #controls h2 {
            margin-bottom: 10px;
            font-size: 16px;
            color: #4CAF50;
        }
        #controls label {
            display: block;
            margin: 8px 0;
            cursor: pointer;
        }
        #controls input[type=""checkbox""] {
            margin-right: 8px;
        }
        #controls button {
            margin: 5px 5px 5px 0;
            padding: 8px 15px;
            background: #4CAF50;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
        }
        #controls button:hover {
            background: #45a049;
        }
        #info {
            position: fixed;
            bottom: 10px;
            left: 10px;
            background: rgba(0,0,0,0.8);
            padding: 10px 15px;
            border-radius: 8px;
            font-size: 12px;
            z-index: 1000;
        }
        #mapCanvas {
            display: block;
            cursor: grab;
            image-rendering: pixelated;
        }
        #mapCanvas:active {
            cursor: grabbing;
        }
        #status {
            position: fixed;
            top: 10px;
            right: 10px;
            background: rgba(0,0,0,0.8);
            padding: 10px 15px;
            border-radius: 8px;
            font-size: 12px;
        }
        .status-indicator {
            display: inline-block;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            margin-right: 5px;
        }
        .status-connected { background: #4CAF50; }
        .status-disconnected { background: #f44336; }
    </style>
</head>
<body>
    <div id=""controls"">
        <h2>TazUO World Map</h2>
        <button onclick=""zoomIn()"">Zoom In (+)</button>
        <button onclick=""zoomOut()"">Zoom Out (-)</button>
        <button onclick=""centerOnPlayer()"">Center</button>
        <br>
        <label><input type=""checkbox"" id=""followPlayer"" checked> Follow Player</label>
        <label><input type=""checkbox"" id=""showParty"" checked> Show Party</label>
        <label><input type=""checkbox"" id=""showGuild"" checked> Show Guild</label>
        <label><input type=""checkbox"" id=""showMarkers"" checked> Show Markers</label>
        <label><input type=""checkbox"" id=""showNames"" checked> Show Names</label>
        <label><input type=""checkbox"" id=""showGrid"" checked> Show Grid</label>
    </div>
    <div id=""status"">
        <span class=""status-indicator status-disconnected"" id=""statusIndicator""></span>
        <span id=""statusText"">Connecting...</span>
    </div>
    <div id=""info"">
        <div>Zoom: <span id=""zoomLevel"">1.0x</span></div>
        <div>Player: <span id=""playerPos"">0, 0</span></div>
        <div>Mouse: <span id=""mousePos"">-</span></div>
    </div>
    <canvas id=""mapCanvas""></canvas>

    <script>
        const canvas = document.getElementById('mapCanvas');
        const ctx = canvas.getContext('2d');

        let mapImage = null;
        let mapData = null;
        let zoom = 1.0;
        let targetZoom = 1.0;
        let offsetX = 0;
        let offsetY = 0;
        let targetOffsetX = 0;
        let targetOffsetY = 0;
        let isDragging = false;
        let lastMouseX = 0;
        let lastMouseY = 0;
        let eventSource = null;
        let animationFrameId = null;
        let mouseZoomPoint = null; // Track the world position to keep under cursor during zoom

        const zoomLevels = [0.125, 0.25, 0.5, 0.75, 1, 1.5, 2, 4, 6, 8];
        let zoomIndex = 4;
        const ZOOM_SPEED = 0.15;

        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;

        window.addEventListener('resize', () => {
            canvas.width = window.innerWidth;
            canvas.height = window.innerHeight;
            draw();
        });

        function animate() {
            let needsRedraw = false;

            // Smooth zoom interpolation
            if (Math.abs(zoom - targetZoom) > 0.001) {
                const oldZoom = zoom;
                zoom += (targetZoom - zoom) * ZOOM_SPEED;

                // If we're zooming to a mouse point, recalculate offset to keep that point stable
                if (mouseZoomPoint) {
                    offsetX = mouseZoomPoint.canvasX - canvas.width / 2 - mouseZoomPoint.worldX * zoom;
                    offsetY = mouseZoomPoint.canvasY - canvas.height / 2 - mouseZoomPoint.worldY * zoom;
                } else {
                    // For button zoom, maintain the center point
                    const zoomRatio = zoom / oldZoom;
                    offsetX *= zoomRatio;
                    offsetY *= zoomRatio;
                }

                needsRedraw = true;
            } else if (zoom !== targetZoom) {
                zoom = targetZoom;
                mouseZoomPoint = null; // Clear mouse zoom point when animation completes
                needsRedraw = true;
            }

            if (needsRedraw) {
                draw();
            }

            animationFrameId = requestAnimationFrame(animate);
        }

        animate();

        async function loadMapTexture() {
            try {
                updateStatus(false, 'Loading map...');
                const response = await fetch('/api/maptexture');

                if (!response.ok) {
                    throw new Error(`Map not loaded (${response.status})`);
                }

                const blob = await response.blob();
                const img = new Image();
                img.onload = () => {
                    mapImage = img;
                    updateStatus(true, 'Connected');
                    draw();
                };
                img.onerror = (err) => {
                    console.error('Image load error:', err);
                    updateStatus(false, 'Image failed to load');
                };
                img.src = URL.createObjectURL(blob);
            } catch (err) {
                console.error('Failed to load map texture:', err);
                updateStatus(false, 'Retrying...');
                setTimeout(loadMapTexture, 2000);
            }
        }

        async function loadMapData() {
            try {
                const response = await fetch('/api/mapdata');
                if (!response.ok) throw new Error('Not in game');

                mapData = await response.json();
                updateStatus(true);

                if (document.getElementById('followPlayer').checked) {
                    centerOnPlayer();
                }

                draw();
            } catch (err) {
                console.error('Failed to load map data:', err);
                updateStatus(false);
            }
        }

        function connectEventStream() {
            if (eventSource) {
                eventSource.close();
            }

            eventSource = new EventSource('/api/events');

            eventSource.onmessage = (event) => {
                const data = JSON.parse(event.data);
                if (mapData) {
                    mapData.player = data.player;
                    mapData.party = data.party;

                    if (document.getElementById('followPlayer').checked) {
                        centerOnPlayer();
                    } else {
                        draw();
                    }
                }
            };

            eventSource.onerror = () => {
                updateStatus(false);
                setTimeout(connectEventStream, 5000);
            };
        }

        function updateStatus(connected, message) {
            const indicator = document.getElementById('statusIndicator');
            const text = document.getElementById('statusText');

            if (connected) {
                indicator.className = 'status-indicator status-connected';
                text.textContent = message || 'Connected';
            } else {
                indicator.className = 'status-indicator status-disconnected';
                text.textContent = message || 'Disconnected';
            }
        }

        function drawLabel(ctx, text, x, y, color, zoom) {
            const fontSize = Math.max(10, 12 / zoom);
            ctx.font = `${fontSize}px Arial`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';

            const metrics = ctx.measureText(text);
            const textWidth = metrics.width;
            const textHeight = fontSize;
            const padding = 2 / zoom;

            const labelX = x;
            const labelY = y - (6 / zoom);

            // Draw background
            ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
            ctx.fillRect(
                labelX - textWidth / 2 - padding,
                labelY - textHeight,
                textWidth + (padding * 2),
                textHeight + padding
            );

            // Draw text
            ctx.fillStyle = color;
            ctx.fillText(text, labelX, labelY);
        }

        function draw() {
            ctx.fillStyle = '#000';
            ctx.fillRect(0, 0, canvas.width, canvas.height);

            if (!mapImage || !mapData) return;

            const centerX = canvas.width / 2;
            const centerY = canvas.height / 2;

            const drawWidth = mapImage.width * zoom;
            const drawHeight = mapImage.height * zoom;

            ctx.save();
            ctx.translate(centerX + offsetX, centerY + offsetY);
            ctx.scale(zoom, zoom);
            ctx.translate(-mapImage.width / 2, -mapImage.height / 2);

            ctx.drawImage(mapImage, 0, 0);

            // Draw grid
            if (document.getElementById('showGrid').checked && zoom >= 2) {
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
                ctx.lineWidth = 1 / zoom;
                for (let x = 0; x < mapImage.width; x += 100) {
                    ctx.beginPath();
                    ctx.moveTo(x, 0);
                    ctx.lineTo(x, mapImage.height);
                    ctx.stroke();
                }
                for (let y = 0; y < mapImage.height; y += 100) {
                    ctx.beginPath();
                    ctx.moveTo(0, y);
                    ctx.lineTo(mapImage.width, y);
                    ctx.stroke();
                }
            }

            // Draw markers
            if (document.getElementById('showMarkers').checked && mapData.markers) {
                const showNames = document.getElementById('showNames').checked;
                mapData.markers.forEach(marker => {
                    const markerColor = `rgba(${marker.color.r}, ${marker.color.g}, ${marker.color.b}, ${marker.color.a / 255})`;
                    ctx.fillStyle = markerColor;
                    ctx.strokeStyle = '#ffffff';
                    ctx.lineWidth = 1 / zoom;
                    ctx.beginPath();
                    ctx.arc(marker.x, marker.y, 3 / zoom, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.stroke();

                    if (showNames && marker.name) {
                        drawLabel(ctx, marker.name, marker.x, marker.y, markerColor, zoom);
                    }
                });
            }

            // Draw guild members
            if (document.getElementById('showGuild').checked && mapData.guild) {
                const showNames = document.getElementById('showNames').checked;
                ctx.fillStyle = '#00ff00';
                ctx.strokeStyle = '#ffffff';
                ctx.lineWidth = 1 / zoom;
                mapData.guild.forEach(member => {
                    ctx.beginPath();
                    ctx.arc(member.x, member.y, 4 / zoom, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.stroke();

                    if (showNames && member.name) {
                        drawLabel(ctx, member.name, member.x, member.y, '#00ff00', zoom);
                    }
                });
            }

            // Draw party members
            if (document.getElementById('showParty').checked && mapData.party) {
                const showNames = document.getElementById('showNames').checked;
                mapData.party.forEach(member => {
                    // Color based on guild/party status
                    const color = member.isGuild ? '#00ff00' : '#ffff00';
                    ctx.fillStyle = color;
                    ctx.strokeStyle = '#ffffff';
                    ctx.lineWidth = 1 / zoom;
                    ctx.beginPath();
                    ctx.arc(member.x, member.y, 4 / zoom, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.stroke();

                    if (showNames && member.name) {
                        drawLabel(ctx, member.name, member.x, member.y, color, zoom);
                    }
                });
            }

            // Draw player
            if (mapData.player) {
                ctx.fillStyle = '#ff0000';
                ctx.strokeStyle = '#ffffff';
                ctx.lineWidth = 2 / zoom;
                ctx.beginPath();
                ctx.arc(mapData.player.x, mapData.player.y, 5 / zoom, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();

                const showNames = document.getElementById('showNames').checked;
                if (showNames && mapData.player.name) {
                    drawLabel(ctx, mapData.player.name, mapData.player.x, mapData.player.y, '#ff0000', zoom);
                }

                document.getElementById('playerPos').textContent =
                    `${mapData.player.x}, ${mapData.player.y}`;
            }

            ctx.restore();

            document.getElementById('zoomLevel').textContent = zoom.toFixed(2) + 'x';
        }

        function centerOnPlayer() {
            if (!mapData || !mapData.player || !mapImage) return;

            // Calculate offset to center player position on screen
            // The map coordinate system has (0,0) at top-left
            // We need to offset so player appears at canvas center
            offsetX = (mapImage.width / 2 - mapData.player.x) * zoom;
            offsetY = (mapImage.height / 2 - mapData.player.y) * zoom;
            draw();
        }

        function zoomIn() {
            if (zoomIndex < zoomLevels.length - 1) {
                zoomIndex++;
                targetZoom = zoomLevels[zoomIndex];
                mouseZoomPoint = null; // Button zoom uses center, not mouse position
            }
        }

        function zoomOut() {
            if (zoomIndex > 0) {
                zoomIndex--;
                targetZoom = zoomLevels[zoomIndex];
                mouseZoomPoint = null; // Button zoom uses center, not mouse position
            }
        }

        function zoomToMouse(mouseX, mouseY, zoomDelta) {
            // Apply zoom change
            const newZoomIndex = Math.max(0, Math.min(zoomLevels.length - 1, zoomIndex + zoomDelta));
            if (newZoomIndex === zoomIndex) return;

            zoomIndex = newZoomIndex;
            const newZoom = zoomLevels[zoomIndex];

            // Calculate world position under mouse at current zoom level
            const worldX = (mouseX - canvas.width / 2 - offsetX) / zoom;
            const worldY = (mouseY - canvas.height / 2 - offsetY) / zoom;

            // Store the point to keep stable during zoom animation
            mouseZoomPoint = {
                canvasX: mouseX,
                canvasY: mouseY,
                worldX: worldX,
                worldY: worldY
            };

            targetZoom = newZoom;
            // Let the animate() loop smoothly interpolate to targetZoom
        }

        canvas.addEventListener('mousedown', (e) => {
            isDragging = true;
            lastMouseX = e.clientX;
            lastMouseY = e.clientY;
            document.getElementById('followPlayer').checked = false;
        });

        canvas.addEventListener('mousemove', (e) => {
            if (isDragging) {
                const dx = e.clientX - lastMouseX;
                const dy = e.clientY - lastMouseY;
                offsetX += dx;
                offsetY += dy;
                lastMouseX = e.clientX;
                lastMouseY = e.clientY;
                draw();
            }

            // Update mouse world coordinates
            if (mapImage && mapData) {
                const rect = canvas.getBoundingClientRect();
                const mouseCanvasX = e.clientX - rect.left;
                const mouseCanvasY = e.clientY - rect.top;

                const centerX = canvas.width / 2;
                const centerY = canvas.height / 2;

                const worldX = (mouseCanvasX - centerX - offsetX) / zoom + mapImage.width / 2;
                const worldY = (mouseCanvasY - centerY - offsetY) / zoom + mapImage.height / 2;

                document.getElementById('mousePos').textContent =
                    `${Math.floor(worldX)}, ${Math.floor(worldY)}`;
            }
        });

        canvas.addEventListener('mouseup', () => {
            isDragging = false;
        });

        canvas.addEventListener('wheel', (e) => {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;
            const mouseY = e.clientY - rect.top;

            zoomToMouse(mouseX, mouseY, e.deltaY < 0 ? 1 : -1);
        });

        // Keyboard shortcuts
        window.addEventListener('keydown', (e) => {
            switch(e.key) {
                case '+':
                case '=':
                    zoomIn();
                    break;
                case '-':
                case '_':
                    zoomOut();
                    break;
                case 'c':
                case 'C':
                    centerOnPlayer();
                    break;
                case 'f':
                case 'F':
                    const followCheckbox = document.getElementById('followPlayer');
                    followCheckbox.checked = !followCheckbox.checked;
                    if (followCheckbox.checked) centerOnPlayer();
                    break;
            }
        });

        // Initialize
        loadMapTexture();
        loadMapData();
        connectEventStream();

        // Reload map texture every 30 seconds in case it changes
        setInterval(loadMapTexture, 30000);
    </script>
</body>
</html>";

        public void Dispose() => Stop();
    }
}
