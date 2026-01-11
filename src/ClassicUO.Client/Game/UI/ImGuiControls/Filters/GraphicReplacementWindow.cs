using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClassicUO.Game.Managers;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using ClassicUO.Utility;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class GraphicReplacementWindow : SingletonImGuiWindow<GraphicReplacementWindow>
    {
        private string _newOriginalGraphicInput = "";
        private string _newReplacementGraphicInput = "";
        private string _newHueInput = "";
        private bool _showAddEntry = false;
        private string _validationError = null;
        private byte _newOriginalTypeSelection = 1; // Default Mobile
        private Dictionary<(ushort, byte), string> _entryOriginalInputs = new Dictionary<(ushort, byte), string>();
        private Dictionary<(ushort, byte), string> _entryReplacementInputs = new Dictionary<(ushort, byte), string>();
        private Dictionary<(ushort, byte), string> _entryHueInputs = new Dictionary<(ushort, byte), string>();
        private Dictionary<(ushort, byte), byte> _entryOriginalTypeSelections = new Dictionary<(ushort, byte), byte>();
        private Dictionary<(ushort, byte), ArtPointerStruct> _textureCache = new Dictionary<(ushort, byte), ArtPointerStruct>();

        private GraphicReplacementWindow() : base("Graphic Replacement")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
        }

        public override void DrawContent()
        {
            ImGui.Spacing();
            ImGuiComponents.Tooltip("Replace graphics with other graphics. Mobile = animations (mobiles), Land = terrain tiles, Static = items/statics.");

            ImGui.Spacing();

            ImGui.SeparatorText("Options:");

            // Add entry section
            if (ImGui.Button("Add Entry")) _showAddEntry = !_showAddEntry;

            ImGui.SameLine();
            if (ImGui.Button("Target Entity"))
            {
                // FIX: Add null check for World.Instance
                if (World.Instance == null) return;

                World.Instance.TargetManager.SetTargeting((targetedEntity) =>
                {
                    if (targetedEntity == null) return;

                    bool processed = false;
                    ushort graphic = 0;
                    ushort hue = 0;

                    if (targetedEntity is Entity entity)
                    {
                        graphic = entity.Graphic;
                        hue = entity.Hue;
                        processed = true;
                    }
                    else if (targetedEntity is Static stat)
                    {
                        graphic = stat.Graphic;
                        hue = stat.Hue;
                        processed = true;
                    }
                    else if (targetedEntity is Land land)
                    {
                        graphic = land.Graphic;
                        hue = land.Hue;
                        processed = true;
                    }
                    else if (targetedEntity is GameObject obj)
                    {
                        graphic = obj.Graphic;
                        hue = obj.Hue;
                        processed = true;
                    }

                    if (!processed) return;

                    // Determine type based on entity class
                    // Mobile class uses Animation system (type 1)
                    // Land class uses Arts.GetLand() (type 2)
                    // Item/Static classes use Arts.GetArt() (type 3)
                    byte entityType = 3; // Default to Static (art files)
                    if (targetedEntity is Mobile)
                        entityType = 1; // Mobile (animation files)
                    else if (targetedEntity is Land)
                        entityType = 2; // Land tiles

                    GraphicChangeFilter filter = GraphicsReplacement.NewFilter(graphic, entityType, graphic, entityType, hue);
                    if (filter != null)
                    {
                        (ushort OriginalGraphic, byte OriginalType) key = (filter.OriginalGraphic, filter.OriginalType);
                        // Initialize input strings for the new entry
                        _entryOriginalInputs[key] = $"0x{filter.OriginalGraphic:X4}";
                        _entryReplacementInputs[key] = $"0x{filter.ReplacementGraphic:X4}";
                        _entryHueInputs[key] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();
                        _entryOriginalTypeSelections[key] = filter.OriginalType;
                    }
                });
            }

            ImGui.SameLine();
            if (ImGui.Button("Import"))
            {
                string json = Utility.Clipboard.GetClipboardText();

                if(json.NotNullNotEmpty() && GraphicsReplacement.ImportFromJson(json))
                {
                    // Clear input dictionaries to refresh with new data
                    _entryOriginalInputs.Clear();
                    _entryReplacementInputs.Clear();
                    _entryHueInputs.Clear();
                    _entryOriginalTypeSelections.Clear();
                    return;
                }

                GameActions.Print("Your clipboard does not have a valid export copied.", Constants.HUE_ERROR);
            }
            ImGuiComponents.Tooltip("Import from your clipboard, must have a valid export copied.");

            Dictionary<(ushort, byte), GraphicChangeFilter> filters = GraphicsReplacement.GraphicFilters;
            if (filters.Count > 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("Export"))
                {
                    GraphicsReplacement.GetJsonExport()?.CopyToClipboard();
                    GameActions.Print("Exported graphic filters to your clipboard!", Constants.HUE_SUCCESS);
                }
                ImGuiComponents.Tooltip("Export your filters to your clipboard.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Apply to All Entities")) ForceRefreshAllEntities();
            ImGuiComponents.Tooltip("Reapply graphic replacements to all entities currently in the world");

            if (_showAddEntry)
            {
                ImGui.Spacing();
                ImGui.SeparatorText("New Entry:");
                ImGui.Spacing();

                if (!string.IsNullOrEmpty(_validationError))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Current.Error);
                    ImGui.TextWrapped(_validationError);
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }

                ImGui.BeginGroup();
                ImGui.Text("Original Graphic:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewOriginalGraphic", ref _newOriginalGraphicInput, 10);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                // Mobile uses animations, Land uses GetLand(), Static uses GetArt()
                string[] typeNames = { "Mobile", "Land", "Static" };
                byte[] typeValues = { 1, 2, 3 }; // Mobile=1 (animations), Land=2 (land tiles), Static=3 (items/statics)
                int origTypeIdx = Array.IndexOf(typeValues, _newOriginalTypeSelection);
                if (ImGui.Combo("##OriginalType", ref origTypeIdx, typeNames, typeNames.Length))
                    _newOriginalTypeSelection = typeValues[origTypeIdx];
                ImGui.EndGroup();

                ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.Text("Replacement Graphic:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewReplacementGraphic", ref _newReplacementGraphicInput, 10);
                ImGui.EndGroup();

                ImGui.Spacing();
                ImGui.Text("New Hue (-1 to leave original):");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewHue", ref _newHueInput, 10);

                ImGui.Spacing();
                if (ImGui.Button("Add##AddEntry"))
                    if (StringHelper.TryParseInt(_newOriginalGraphicInput, out int originalGraphic) &&
                        StringHelper.TryParseInt(_newReplacementGraphicInput, out int replacementGraphic))
                    {
                        // No cross-type validation - users explicitly select types
                        _validationError = null;

                        ushort newHue = ushort.MaxValue;
                        if (!string.IsNullOrEmpty(_newHueInput) && _newHueInput != "-1")
                            if (!ushort.TryParse(_newHueInput, out newHue))
                                _validationError = $"Invalid hue value: '{_newHueInput}'. Must be a number between 0-65535 or -1";

                        if (_validationError == null)
                        {
                            // Replacement type is always same as original type
                            GraphicChangeFilter filter = GraphicsReplacement.NewFilter(
                                (ushort)originalGraphic,
                                _newOriginalTypeSelection,
                                (ushort)replacementGraphic,
                                _newOriginalTypeSelection, // Same type as original
                                newHue
                            );
                            if (filter != null)
                            {
                                (ushort OriginalGraphic, byte OriginalType) key = (filter.OriginalGraphic, filter.OriginalType);
                                // Initialize input strings for the new entry
                                _entryOriginalInputs[key] = $"0x{filter.OriginalGraphic:X4}";
                                _entryReplacementInputs[key] = $"0x{filter.ReplacementGraphic:X4}";
                                _entryHueInputs[key] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();
                                _entryOriginalTypeSelections[key] = filter.OriginalType;

                                // Reset inputs
                                _newOriginalGraphicInput = "";
                                _newReplacementGraphicInput = "";
                                _newHueInput = "";
                                _newOriginalTypeSelection = 1;
                                _showAddEntry = false;
                            }
                        }
                    }

                ImGui.SameLine();
                if (ImGui.Button("Cancel##AddEntry"))
                {
                    _showAddEntry = false;
                    _newOriginalGraphicInput = "";
                    _newReplacementGraphicInput = "";
                    _newHueInput = "";
                    _validationError = null; // FIX: Clear validation error on cancel
                }
            }

            ImGui.Separator();

            // List of current filters
            ImGui.Text("Current Graphic Replacements:");

            if (filters.Count == 0)
                ImGui.Text("No replacements configured");
            else
            {
                // Table headers - 6 columns (replacement type is always same as original)
                if (ImGui.BeginTable("GraphicReplacementTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT)))
                {
                    ImGui.TableSetupColumn("Original", ImGuiTableColumnFlags.WidthFixed, 105f);
                    ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100f);
                    ImGui.TableSetupColumn("Replacement", ImGuiTableColumnFlags.WidthFixed, 105f);
                    ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 100f);
                    ImGui.TableSetupColumn("New Hue", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableHeadersRow();

                    var filterList = filters.Values.ToList();
                    for (int i = filterList.Count - 1; i >= 0; i--)
                    {
                        GraphicChangeFilter filter = filterList[i];
                        (ushort OriginalGraphic, byte OriginalType) key = (filter.OriginalGraphic, filter.OriginalType);
                        ImGui.TableNextRow();

                        // Column 1: Original Graphic
                        ImGui.TableNextColumn();
                        if (!_entryOriginalInputs.ContainsKey(key))
                            _entryOriginalInputs[key] = $"0x{filter.OriginalGraphic:X4}";
                        string originalStr = _entryOriginalInputs[key];
                        if (ImGui.InputText($"##Original{i}", ref originalStr, 10))
                        {
                            _entryOriginalInputs[key] = originalStr;
                            if (StringHelper.TryParseInt(originalStr, out int newOriginal) && newOriginal != filter.OriginalGraphic)
                            {
                                // Delete old, create new with new key
                                GraphicsReplacement.DeleteFilter(filter.OriginalGraphic, filter.OriginalType);
                                GraphicChangeFilter newFilter = GraphicsReplacement.NewFilter(
                                    (ushort)newOriginal,
                                    filter.OriginalType,
                                    filter.ReplacementGraphic,
                                    filter.ReplacementType,
                                    filter.NewHue
                                );
                                if (newFilter != null)
                                {
                                    // Update dictionaries
                                    (ushort OriginalGraphic, byte OriginalType) newKey = (newFilter.OriginalGraphic, newFilter.OriginalType);
                                    _entryOriginalInputs.Remove(key);
                                    _entryReplacementInputs.Remove(key);
                                    _entryHueInputs.Remove(key);
                                    _entryOriginalTypeSelections.Remove(key);

                                    _entryOriginalInputs[newKey] = originalStr;
                                    _entryReplacementInputs[newKey] = $"0x{newFilter.ReplacementGraphic:X4}";
                                    _entryHueInputs[newKey] = newFilter.NewHue == ushort.MaxValue ? "-1" : newFilter.NewHue.ToString();
                                    _entryOriginalTypeSelections[newKey] = newFilter.OriginalType;
                                }
                            }
                        }

                        // Column 2: Type dropdown (applies to both original and replacement)
                        ImGui.TableNextColumn();
                        if (!_entryOriginalTypeSelections.ContainsKey(key))
                            _entryOriginalTypeSelections[key] = filter.OriginalType;
                        string[] typeNames = { "Mobile", "Land", "Static" };
                        byte[] typeValues = { 1, 2, 3 };
                        int origTypeIdx = Array.IndexOf(typeValues, _entryOriginalTypeSelections[key]);
                        ImGui.SetNextItemWidth(80);
                        if (ImGui.Combo($"##Type{i}", ref origTypeIdx, typeNames, typeNames.Length))
                        {
                            byte newType = typeValues[origTypeIdx];
                            if (newType != filter.OriginalType)
                            {
                                // Delete and recreate with new type (applies to both original and replacement)
                                GraphicsReplacement.DeleteFilter(filter.OriginalGraphic, filter.OriginalType);
                                GraphicChangeFilter newFilter = GraphicsReplacement.NewFilter(
                                    filter.OriginalGraphic,
                                    newType,
                                    filter.ReplacementGraphic,
                                    newType, // Same type for replacement
                                    filter.NewHue
                                );
                                if (newFilter != null)
                                {
                                    // Update dictionaries
                                    (ushort OriginalGraphic, byte OriginalType) newKey = (newFilter.OriginalGraphic, newFilter.OriginalType);
                                    _entryOriginalInputs.Remove(key);
                                    _entryReplacementInputs.Remove(key);
                                    _entryHueInputs.Remove(key);
                                    _entryOriginalTypeSelections.Remove(key);

                                    _entryOriginalInputs[newKey] = $"0x{newFilter.OriginalGraphic:X4}";
                                    _entryReplacementInputs[newKey] = $"0x{newFilter.ReplacementGraphic:X4}";
                                    _entryHueInputs[newKey] = newFilter.NewHue == ushort.MaxValue ? "-1" : newFilter.NewHue.ToString();
                                    _entryOriginalTypeSelections[newKey] = newFilter.OriginalType;
                                }
                            }
                        }

                        // Column 3: Replacement Graphic
                        ImGui.TableNextColumn();
                        if (!_entryReplacementInputs.ContainsKey(key))
                            _entryReplacementInputs[key] = $"0x{filter.ReplacementGraphic:X4}";
                        string replacementStr = _entryReplacementInputs[key];
                        if (ImGui.InputText($"##Replacement{i}", ref replacementStr, 10))
                        {
                            _entryReplacementInputs[key] = replacementStr;
                            if (StringHelper.TryParseInt(replacementStr, out int newReplacement))
                            {
                                filter.ReplacementGraphic = (ushort)newReplacement;
                                // Keep replacement type same as original
                                filter.ReplacementType = filter.OriginalType;
                            }
                        }

                        // Column 4: Preview
                        ImGui.TableNextColumn();
                        bool drewOriginal = false;
                        bool drewReplacement = false;

                        // Use explicit type from filter (same for both original and replacement)
                        if (filter.OriginalType == 1) // Mobile - use animations
                        {
                            drewOriginal = DrawStaticAnim(filter.OriginalGraphic, filter.OriginalType, 32);
                            if (!drewOriginal)
                                ImGui.TextDisabled($"0x{filter.OriginalGraphic:X4}");

                            ImGui.SameLine();

                            drewReplacement = DrawStaticAnim(filter.ReplacementGraphic, filter.ReplacementType, 32);
                            if (!drewReplacement)
                                ImGui.TextDisabled($"0x{filter.ReplacementGraphic:X4}");
                        }
                        else if (filter.OriginalType == 2) // Land - use GetLand()
                        {
                            drewOriginal = DrawLand(filter.OriginalGraphic, 32);
                            if (!drewOriginal)
                                ImGui.TextDisabled($"0x{filter.OriginalGraphic:X4}");

                            ImGui.SameLine();

                            drewReplacement = DrawLand(filter.ReplacementGraphic, 32);
                            if (!drewReplacement)
                                ImGui.TextDisabled($"0x{filter.ReplacementGraphic:X4}");
                        }
                        else // Static (type 3) - use GetArt()
                        {
                            drewOriginal = DrawArt(filter.OriginalGraphic, 32, false);
                            if (!drewOriginal)
                                ImGui.TextDisabled($"0x{filter.OriginalGraphic:X4}");

                            ImGui.SameLine();

                            drewReplacement = DrawArt(filter.ReplacementGraphic, 32, false);
                            if (!drewReplacement)
                                ImGui.TextDisabled($"0x{filter.ReplacementGraphic:X4}");
                        }

                        // Column 5: Hue
                        ImGui.TableNextColumn();
                        if (!_entryHueInputs.ContainsKey(key))
                            _entryHueInputs[key] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();
                        string hueStr = _entryHueInputs[key];
                        if (ImGui.InputText($"##Hue{i}", ref hueStr, 10))
                        {
                            _entryHueInputs[key] = hueStr;
                            if (hueStr == "-1")
                                filter.NewHue = ushort.MaxValue;
                            else if (ushort.TryParse(hueStr, out ushort newHue))
                                filter.NewHue = newHue;
                        }
                        ImGuiComponents.Tooltip("-1 will not change the hue");

                        // Column 6: Actions
                        ImGui.TableNextColumn();
                        if (ImGui.Button($"Delete##Delete{i}"))
                        {
                            GraphicsReplacement.DeleteFilter(filter.OriginalGraphic, filter.OriginalType);
                            _entryOriginalInputs.Remove(key);
                            _entryReplacementInputs.Remove(key);
                            _entryHueInputs.Remove(key);
                            _entryOriginalTypeSelections.Remove(key);
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        private void ForceRefreshAllEntities()
        {
            World world = World.Instance;
            if (world == null) return;

            int count = 0;

            // THREAD SAFETY FIX: Take snapshot to avoid collection modification during iteration
            var mobiles = world.Mobiles.Values.ToList();
            var items = world.Items.Values.ToList();

            // Refresh all mobiles
            foreach (Mobile mobile in mobiles)
                if (!mobile.IsDestroyed && mobile.OriginalGraphic != 0)
                {
                    mobile.Graphic = mobile.OriginalGraphic;
                    count++;
                }

            // Refresh all items
            foreach (Item item in items)
                if (!item.IsDestroyed && item.OriginalGraphic != 0)
                {
                    item.Graphic = item.OriginalGraphic;
                    count++;
                }

            // Show feedback
            GameActions.Print($"Refreshed {count} entities with graphic replacements");
        }

        private bool DrawStaticAnim(ushort graphic, byte type, float maxSize)
        {
            Vector2 size;
            (ushort graphic, byte type) cacheKey = (graphic, type);

            // Check if we already have this cached
            if (_textureCache.TryGetValue(cacheKey, out ArtPointerStruct cached))
            {
                // Calculate scaled size based on cached sprite dimensions
                size = CalculateScaledSize(cached.SpriteInfo.UV.Width, cached.SpriteInfo.UV.Height, maxSize);
                ImGui.Image(cached.Pointer, size, cached.UV0, cached.UV1);
                return true;
            }

            // Get animation frames for this mobile graphic
            // Use standing animation (group 0) and direction 0
            Span<SpriteInfo> frames = Client.Game.UO.Animations.GetAnimationFrames(
                graphic,
                0, // Standing animation
                0, // Direction 0
                out ushort _,
                out bool _,
                false,
                false
            );

            if (frames.IsEmpty || frames[0].Texture == null)
                return false;

            SpriteInfo spriteInfo = frames[0];

            // Calculate scaled size to fit within maxSize
            size = CalculateScaledSize(spriteInfo.UV.Width, spriteInfo.UV.Height, maxSize);

            // Cache the first frame
            var uv0 = new Vector2(spriteInfo.UV.X / (float)spriteInfo.Texture.Width, spriteInfo.UV.Y / (float)spriteInfo.Texture.Height);
            var uv1 = new Vector2((spriteInfo.UV.X + spriteInfo.UV.Width) / (float)spriteInfo.Texture.Width, (spriteInfo.UV.Y + spriteInfo.UV.Height) / (float)spriteInfo.Texture.Height);
            nint pnt = ImGuiManager.Renderer.BindTexture(spriteInfo.Texture);

            _textureCache.Add(cacheKey, new ArtPointerStruct(pnt, spriteInfo, uv0, uv1, size));

            ImGui.Image(pnt, size, uv0, uv1);
            return true;
        }

        private bool DrawLand(ushort graphic, float maxSize)
        {
            Vector2 size;
            (ushort graphic, byte type) cacheKey = (graphic, 2); // Type 2 = Land

            // Check if we already have this cached
            if (_textureCache.TryGetValue(cacheKey, out ArtPointerStruct cached))
            {
                // Calculate scaled size based on cached sprite dimensions
                size = CalculateScaledSize(cached.SpriteInfo.UV.Width, cached.SpriteInfo.UV.Height, maxSize);
                ImGui.Image(cached.Pointer, size, cached.UV0, cached.UV1);
                return true;
            }

            // Get land tile graphic using GetLand()
            ref readonly SpriteInfo spriteInfo = ref Client.Game.UO.Arts.GetLand(graphic);

            if (spriteInfo.Texture == null)
                return false;

            // Calculate scaled size to fit within maxSize
            size = CalculateScaledSize(spriteInfo.UV.Width, spriteInfo.UV.Height, maxSize);

            // Calculate UV coordinates and bind texture
            var uv0 = new Vector2(spriteInfo.UV.X / (float)spriteInfo.Texture.Width, spriteInfo.UV.Y / (float)spriteInfo.Texture.Height);
            var uv1 = new Vector2((spriteInfo.UV.X + spriteInfo.UV.Width) / (float)spriteInfo.Texture.Width, (spriteInfo.UV.Y + spriteInfo.UV.Height) / (float)spriteInfo.Texture.Height);
            nint pnt = ImGuiManager.Renderer.BindTexture(spriteInfo.Texture);

            // Cache it
            _textureCache.Add(cacheKey, new ArtPointerStruct(pnt, spriteInfo, uv0, uv1, size));

            ImGui.Image(pnt, size, uv0, uv1);
            return true;
        }

        private bool DrawArt(ushort graphic, float maxSize, bool useSmallerIfGfxSmaller = true)
        {
            Vector2 size;
            (ushort graphic, byte type) cacheKey = (graphic, 3); // Type 3 = Static/Art

            // Check if we already have this cached
            if (_textureCache.TryGetValue(cacheKey, out ArtPointerStruct cached))
            {
                // Calculate scaled size based on cached sprite dimensions
                size = CalculateScaledSize(cached.SpriteInfo.UV.Width, cached.SpriteInfo.UV.Height, maxSize);

                if (useSmallerIfGfxSmaller && cached.SpriteInfo.UV.Width < maxSize && cached.SpriteInfo.UV.Height < maxSize)
                    size = new Vector2(cached.SpriteInfo.UV.Width, cached.SpriteInfo.UV.Height);

                ImGui.Image(cached.Pointer, size, cached.UV0, cached.UV1);
                return true;
            }

            SpriteInfo artInfo = Client.Game.UO.Arts.GetArt(graphic);

            if (artInfo.Texture == null)
                return false;

            size = CalculateScaledSize(artInfo.UV.Width, artInfo.UV.Height, maxSize);

            if (useSmallerIfGfxSmaller && artInfo.UV.Width < maxSize && artInfo.UV.Height < maxSize)
                size = new Vector2(artInfo.UV.Width, artInfo.UV.Height);

            // Calculate UV coordinates and bind texture
            var uv0 = new Vector2(artInfo.UV.X / (float)artInfo.Texture.Width, artInfo.UV.Y / (float)artInfo.Texture.Height);
            var uv1 = new Vector2((artInfo.UV.X + artInfo.UV.Width) / (float)artInfo.Texture.Width, (artInfo.UV.Y + artInfo.UV.Height) / (float)artInfo.Texture.Height);
            nint pnt = ImGuiManager.Renderer.BindTexture(artInfo.Texture);

            // Cache it
            _textureCache.Add(cacheKey, new ArtPointerStruct(pnt, artInfo, uv0, uv1, size));

            ImGui.Image(pnt, size, uv0, uv1);
            return true;
        }

        private Vector2 CalculateScaledSize(int width, int height, float maxSize)
        {
            if (width <= 0 || height <= 0)
                return new Vector2(maxSize, maxSize);

            float aspectRatio = (float)width / height;

            if (width >= height)
            {
                // Width is larger, scale based on width
                return new Vector2(maxSize, maxSize / aspectRatio);
            }
            else
            {
                // Height is larger, scale based on height
                return new Vector2(maxSize * aspectRatio, maxSize);
            }
        }

        protected override void OnWindowClosed()
        {
            base.OnWindowClosed();

            // Clean up texture cache
            foreach (KeyValuePair<(ushort, byte), ArtPointerStruct> item in _textureCache)
                if (item.Value.Pointer != IntPtr.Zero)
                    ImGuiManager.Renderer.UnbindTexture(item.Value.Pointer);
            _textureCache.Clear();

            _entryOriginalInputs = new Dictionary<(ushort, byte), string>();
            _entryReplacementInputs = new Dictionary<(ushort, byte), string>();
            _entryHueInputs = new Dictionary<(ushort, byte), string>();
            _entryOriginalTypeSelections = new Dictionary<(ushort, byte), byte>();
            _textureCache = new Dictionary<(ushort, byte), ArtPointerStruct>();
        }

    }
}
