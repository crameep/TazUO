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
        private enum GraphicType
        {
            Unknown,
            Mobile,
            Land,
            Static
        }

        private string newOriginalGraphicInput = "";
        private string newReplacementGraphicInput = "";
        private string newHueInput = "";
        private bool showAddEntry = false;
        private string _validationError = null;
        private Dictionary<ushort, string> entryOriginalInputs = new Dictionary<ushort, string>();
        private Dictionary<ushort, string> entryReplacementInputs = new Dictionary<ushort, string>();
        private Dictionary<ushort, string> entryHueInputs = new Dictionary<ushort, string>();
        private Dictionary<ushort, ArtPointerStruct> _mobileTextureCache = new Dictionary<ushort, ArtPointerStruct>();

        private GraphicReplacementWindow() : base("Mobile Graphics Replacement")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
        }

        public override void DrawContent()
        {
            ImGui.Spacing();
            ImGuiComponents.Tooltip("This can be used to replace graphics of mobiles with other graphics (For example if dragons are too big, replace them with wyverns).");

            ImGui.Spacing();

            ImGui.SeparatorText("Options:");

            // Add entry section
            if (ImGui.Button("Add Entry")) showAddEntry = !showAddEntry;

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

                    if (!processed) return;

                    GraphicChangeFilter filter = GraphicsReplacement.NewFilter(graphic, graphic, hue);
                    if (filter != null)
                    {
                        // Initialize input strings for the new entry
                        entryOriginalInputs[filter.OriginalGraphic] = $"0x{filter.OriginalGraphic:X4}";
                        entryReplacementInputs[filter.OriginalGraphic] = $"0x{filter.ReplacementGraphic:X4}";
                        entryHueInputs[filter.OriginalGraphic] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();
                    }
                });
            }

            ImGui.SameLine();
            if (ImGui.Button("Apply to All Entities")) ForceRefreshAllEntities();
            ImGuiComponents.Tooltip("Reapply graphic replacements to all entities currently in the world");

            if (showAddEntry)
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
                ImGui.InputText("##NewOriginalGraphic", ref newOriginalGraphicInput, 10);
                ImGui.EndGroup();

                ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.Text("Replacement Graphic:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewReplacementGraphic", ref newReplacementGraphicInput, 10);
                ImGui.EndGroup();

                ImGui.Spacing();
                ImGui.Text("New Hue (-1 to leave original):");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewHue", ref newHueInput, 10);

                ImGui.Spacing();
                if (ImGui.Button("Add##AddEntry"))
                    if (StringHelper.TryParseInt(newOriginalGraphicInput, out int originalGraphic) &&
                        StringHelper.TryParseInt(newReplacementGraphicInput, out int replacementGraphic))
                    {
                        // Type validation
                        GraphicType origType = DetermineGraphicType((ushort)originalGraphic);
                        GraphicType replType = DetermineGraphicType((ushort)replacementGraphic);

                        if (origType != GraphicType.Unknown &&
                            replType != GraphicType.Unknown &&
                            origType != replType)
                            _validationError = $"Type mismatch: Cannot replace {origType} (0x{originalGraphic:X4}) with {replType} (0x{replacementGraphic:X4})";
                        // FIX: Don't return early - let UI continue rendering
                        else
                        {
                            // Only add if validation passed
                            _validationError = null;

                            ushort newHue = ushort.MaxValue;
                            if (!string.IsNullOrEmpty(newHueInput) && newHueInput != "-1")
                                if (!ushort.TryParse(newHueInput, out newHue))
                                    _validationError = $"Invalid hue value: '{newHueInput}'. Must be a number between 0-65535 or -1";

                            GraphicChangeFilter filter = GraphicsReplacement.NewFilter((ushort)originalGraphic, (ushort)replacementGraphic, newHue);
                            if (filter != null)
                            {
                                // Initialize input strings for the new entry
                                entryOriginalInputs[filter.OriginalGraphic] = $"0x{filter.OriginalGraphic:X4}";
                                entryReplacementInputs[filter.OriginalGraphic] = $"0x{filter.ReplacementGraphic:X4}";
                                entryHueInputs[filter.OriginalGraphic] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();

                                newOriginalGraphicInput = "";
                                newReplacementGraphicInput = "";
                                newHueInput = "";
                                showAddEntry = false;
                            }
                        }
                    }

                ImGui.SameLine();
                if (ImGui.Button("Cancel##AddEntry"))
                {
                    showAddEntry = false;
                    newOriginalGraphicInput = "";
                    newReplacementGraphicInput = "";
                    newHueInput = "";
                    _validationError = null; // FIX: Clear validation error on cancel
                }
            }

            ImGui.Separator();

            // List of current filters
            ImGui.Text("Current Graphic Replacements:");

            Dictionary<ushort, GraphicChangeFilter> filters = GraphicsReplacement.GraphicFilters;
            if (filters.Count == 0)
                ImGui.Text("No replacements configured");
            else
            {
                // Table headers
                if (ImGui.BeginTable("GraphicReplacementTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT)))
                {
                    ImGui.TableSetupColumn("Original Graphic", ImGuiTableColumnFlags.WidthFixed, 105f);
                    ImGui.TableSetupColumn("Replacement Graphic", ImGuiTableColumnFlags.WidthFixed, 105f);
                    ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 100f);
                    ImGui.TableSetupColumn("New Hue", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableHeadersRow();

                    var filterList = filters.Values.ToList();
                    for (int i = filterList.Count - 1; i >= 0; i--)
                    {
                        GraphicChangeFilter filter = filterList[i];
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        // Initialize input string if not exists
                        if (!entryOriginalInputs.ContainsKey(filter.OriginalGraphic)) entryOriginalInputs[filter.OriginalGraphic] = $"0x{filter.OriginalGraphic:X4}";
                        string originalStr = entryOriginalInputs[filter.OriginalGraphic];
                        if (ImGui.InputText($"##Original{i}", ref originalStr, 10))
                        {
                            entryOriginalInputs[filter.OriginalGraphic] = originalStr;
                            if (StringHelper.TryParseInt(originalStr, out int newOriginal) && newOriginal != filter.OriginalGraphic)
                            {
                                // CRITICAL FIX: Must delete and recreate filter when key changes
                                ushort oldKey = filter.OriginalGraphic;
                                ushort newKey = (ushort)newOriginal;

                                // Delete old filter
                                GraphicsReplacement.DeleteFilter(oldKey);

                                // Create new filter with new key
                                GraphicChangeFilter newFilter = GraphicsReplacement.NewFilter(
                                    newKey,
                                    filter.ReplacementGraphic,
                                    filter.NewHue
                                );

                                // Clean up old input dictionaries
                                entryOriginalInputs.Remove(oldKey);
                                entryReplacementInputs.Remove(oldKey);
                                entryHueInputs.Remove(oldKey);

                                // Initialize new input dictionaries if filter was created
                                if (newFilter != null)
                                {
                                    entryOriginalInputs[newKey] = originalStr;
                                    entryReplacementInputs[newKey] = $"0x{newFilter.ReplacementGraphic:X4}";
                                    entryHueInputs[newKey] = newFilter.NewHue == ushort.MaxValue ? "-1" : newFilter.NewHue.ToString();
                                }
                            }
                        }

                        ImGui.TableNextColumn();
                        // Initialize input string if not exists
                        if (!entryReplacementInputs.ContainsKey(filter.OriginalGraphic)) entryReplacementInputs[filter.OriginalGraphic] = $"0x{filter.ReplacementGraphic:X4}";
                        string replacementStr = entryReplacementInputs[filter.OriginalGraphic];
                        if (ImGui.InputText($"##Replacement{i}", ref replacementStr, 10))
                        {
                            entryReplacementInputs[filter.OriginalGraphic] = replacementStr;
                            if (StringHelper.TryParseInt(replacementStr, out int newReplacement)) filter.ReplacementGraphic = (ushort)newReplacement;
                        }

                        ImGui.TableNextColumn(); // Preview column

                        // Determine type based on ORIGINAL graphic - both should use same rendering method
                        GraphicType origType = DetermineGraphicType(filter.OriginalGraphic);
                        bool drewOriginal = false;
                        bool drewReplacement = false;

                        // Use the same drawing method for both based on original type
                        if (origType == GraphicType.Mobile)
                            // Both should be drawn as mobiles
                            drewOriginal = DrawStaticAnim(filter.OriginalGraphic, new Vector2(32, 32));
                        else
                            // Both should be drawn as art (static/item/land)
                            drewOriginal = DrawArt(filter.OriginalGraphic, new Vector2(32, 32), false);

                        if (!drewOriginal)
                            // If drawing fails, show graphic ID as fallback
                            ImGui.TextDisabled($"0x{filter.OriginalGraphic:X4}");

                        ImGui.SameLine();

                        // Draw replacement using SAME method as original
                        if (origType == GraphicType.Mobile)
                            drewReplacement = DrawStaticAnim(filter.ReplacementGraphic, new Vector2(32, 32));
                        else
                            drewReplacement = DrawArt(filter.ReplacementGraphic, new Vector2(32, 32), false);

                        if (!drewReplacement)
                            // If drawing fails, show graphic ID as fallback
                            ImGui.TextDisabled($"0x{filter.ReplacementGraphic:X4}");

                        ImGui.TableNextColumn();
                        // Initialize input string if not exists
                        if (!entryHueInputs.ContainsKey(filter.OriginalGraphic)) entryHueInputs[filter.OriginalGraphic] = filter.NewHue == ushort.MaxValue ? "-1" : filter.NewHue.ToString();
                        string hueStr = entryHueInputs[filter.OriginalGraphic];
                        if (ImGui.InputText($"##Hue{i}", ref hueStr, 10))
                        {
                            entryHueInputs[filter.OriginalGraphic] = hueStr;
                            if (hueStr == "-1")
                                filter.NewHue = ushort.MaxValue;
                            else if (ushort.TryParse(hueStr, out ushort newHue)) filter.NewHue = newHue;
                        }
                        ImGuiComponents.Tooltip("-1 will not change the hue");

                        ImGui.TableNextColumn();
                        if (ImGui.Button($"Delete##Delete{i}"))
                        {
                            GraphicsReplacement.DeleteFilter(filter.OriginalGraphic);
                            // Clean up input dictionaries
                            entryOriginalInputs.Remove(filter.OriginalGraphic);
                            entryReplacementInputs.Remove(filter.OriginalGraphic);
                            entryHueInputs.Remove(filter.OriginalGraphic);
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        private static GraphicType DetermineGraphicType(ushort graphic)
        {
            // Use heuristics to determine graphic type:
            // - Mobiles are typically < 2000
            // - Land tiles are 0 - 0x3FFF (16383)
            // - Static/Item art is typically >= 0x4000

            // Mobile range (most mobiles are under 2000)
            if (graphic < 2000)
                return GraphicType.Mobile;

            // Land tiles range
            if (graphic < 0x4000) // 16384
                return GraphicType.Land;

            // Static/Item range (0x4000 to 0x13FFF)
            if (graphic <= 0xFFFF) // All remaining ushort values
                return GraphicType.Static;

            return GraphicType.Unknown;
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

        protected bool DrawStaticAnim(ushort graphic, Vector2 size)
        {
            // Check if we already have this cached
            if (_mobileTextureCache.TryGetValue(graphic, out ArtPointerStruct cached))
            {
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

            // Cache the first frame
            var uv0 = new Vector2(spriteInfo.UV.X / (float)spriteInfo.Texture.Width, spriteInfo.UV.Y / (float)spriteInfo.Texture.Height);
            var uv1 = new Vector2((spriteInfo.UV.X + spriteInfo.UV.Width) / (float)spriteInfo.Texture.Width, (spriteInfo.UV.Y + spriteInfo.UV.Height) / (float)spriteInfo.Texture.Height);
            nint pnt = ImGuiManager.Renderer.BindTexture(spriteInfo.Texture);

            _mobileTextureCache.Add(graphic, new ArtPointerStruct(pnt, spriteInfo, uv0, uv1, size));

            ImGui.Image(pnt, size, uv0, uv1);
            return true;
        }

        protected override void OnWindowClosed()
        {
            base.OnWindowClosed();

            // Clean up mobile texture cache
            foreach (KeyValuePair<ushort, ArtPointerStruct> item in _mobileTextureCache)
                if (item.Value.Pointer != IntPtr.Zero)
                    ImGuiManager.Renderer.UnbindTexture(item.Value.Pointer);
            _mobileTextureCache.Clear();
        }

    }
}
