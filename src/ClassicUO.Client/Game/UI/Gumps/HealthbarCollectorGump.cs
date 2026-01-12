// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    public class HealthbarCollectorGump : Gump
    {
        private const int WIDTH = 120;
        private const int MIN_HEIGHT = 70;
        private const int TOP_SECTION_HEIGHT = 50;
        private const int BORDER_WIDTH = 2;

        private readonly AlphaBlendControl _background;
        private readonly Label _titleLabel;
        private readonly NiceButton _notorietiesButton;
        private readonly NiceButton _sortButton;
        private ClickableColorBox _borderColorBox;
        private ModernScrollArea _scrollArea;
        private VBoxContainer _container;
        private ResizeHandle _resizeHandle;

        private readonly HashSet<NotorietyFlag> _enabledNotorieties = new();
        private readonly Dictionary<uint, CompactHealthBar> _healthbars = new();
        private readonly Dictionary<uint, NotorietyFlag> _trackedMobiles = new();
        private ushort _borderHue = 0;
        private bool _sortByDistance = false;
        private int _sortUpdateCounter = 0;
        private const int SORT_UPDATE_INTERVAL = 10; // Update sorting every 10 frames

        public HealthbarCollectorGump(World world) : base(world, 0, 0)
        {
            Width = WIDTH;
            Height = 300;

            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = true;

            // Create alpha blend background - full width behind border
            _background = new AlphaBlendControl(0.75f)
            {
                X = 0,
                Y = 0,
                Width = Width,
                Height = Height,
                Hue = 0,
                AcceptMouseInput = true,
                CanMove = true
            };
            Add(_background);

            // Title label
            _titleLabel = new Label("Healthbar Collector", true, 0x0481, font: 1)
            {
                X = 5,
                Y = 8
            };
            Add(_titleLabel);

            // Notorieties button
            _notorietiesButton = new NiceButton(5, 28, 55, 20, ButtonAction.Activate, "Filter")
            {
                IsSelectable = false,
                ButtonParameter = 0
            };
            _notorietiesButton.MouseUp += OnNotorietiesButtonClick;
            Add(_notorietiesButton);

            // Sort button
            _sortButton = new NiceButton(62, 28, 40, 20, ButtonAction.Activate, "Sort")
            {
                IsSelectable = true,
                ButtonParameter = 1
            };
            _sortButton.MouseUp += OnSortButtonClick;
            Add(_sortButton);

            // Create border color picker
            _borderColorBox = new ClickableColorBox(world, WIDTH - 22, 28, 16, 16, _borderHue, true)
            {
                AcceptMouseInput = true
            };
            Add(_borderColorBox);

            // Create scroll area with VBoxContainer
            _container = new VBoxContainer(WIDTH - BORDER_WIDTH * 2 - 4, 2, 2);

            _scrollArea = new ModernScrollArea(
                BORDER_WIDTH + 2,
                TOP_SECTION_HEIGHT,
                WIDTH - BORDER_WIDTH * 2 - 4,
                Height - TOP_SECTION_HEIGHT - BORDER_WIDTH - 20,
                -1
            );
            _scrollArea.Add(_container);
            Add(_scrollArea);

            // Add resize handle at bottom
            _resizeHandle = new ResizeHandle(WIDTH / 2 - 8, Height - 18);
            Add(_resizeHandle);
        }

        public override GumpType GumpType => GumpType.HealthBarCollector;

        private void OnNotorietiesButtonClick(object sender, Input.MouseEventArgs e)
        {
            if (e.Button != Input.MouseButtonType.Left)
                return;

            // Create context menu with all notoriety types
            var contextMenu = new ContextMenuControl(this);

            NotorietyFlag[] notorieties =
            {
                NotorietyFlag.Innocent,
                NotorietyFlag.Ally,
                NotorietyFlag.Gray,
                NotorietyFlag.Criminal,
                NotorietyFlag.Enemy,
                NotorietyFlag.Murderer,
                NotorietyFlag.Invulnerable,
                NotorietyFlag.Unknown
            };

            foreach (NotorietyFlag flag in notorieties)
            {
                NotorietyFlag capturedFlag = flag;
                bool isSelected = _enabledNotorieties.Contains(capturedFlag);

                contextMenu.Add(
                    capturedFlag.ToString(),
                    () => ToggleNotoriety(capturedFlag),
                    canBeSelected: true,
                    defaultValue: isSelected
                );
            }

            contextMenu.Show();
        }

        private void OnSortButtonClick(object sender, Input.MouseEventArgs e)
        {
            if (e.Button != Input.MouseButtonType.Left)
                return;

            _sortByDistance = !_sortByDistance;
            _sortButton.IsSelected = _sortByDistance;

            if (_sortByDistance)
                SortHealthbarsByDistance();
        }

        private void ToggleNotoriety(NotorietyFlag flag)
        {
            if (_enabledNotorieties.Contains(flag))
                _enabledNotorieties.Remove(flag);
            else
                _enabledNotorieties.Add(flag);

            // Rebuild the healthbar list when notorieties are changed
            RebuildHealthbarList();
        }

        private void SortHealthbarsByDistance()
        {
            if (_healthbars.Count == 0)
                return;

            // Get all healthbars and sort by distance
            var sortedBars = _healthbars.Values
                .OrderBy(bar => bar.Distance)
                .ToList();

            // Remove all from container without disposing
            foreach (CompactHealthBar bar in sortedBars)
            {
                _container.Remove(bar);
            }

            // Re-add in sorted order
            foreach (CompactHealthBar bar in sortedBars)
            {
                _container.Add(bar);
            }
        }

        private void RebuildHealthbarList()
        {
            // Clear existing healthbars
            foreach (CompactHealthBar bar in _healthbars.Values) bar.Dispose();
            _healthbars.Clear();
            _trackedMobiles.Clear();

            // Add healthbars for mobiles that match enabled notorieties
            if (_enabledNotorieties.Count > 0)
                foreach (Mobile mobile in World.Mobiles.Values)
                    if (mobile != null && !mobile.IsDestroyed && mobile.Serial != World.Player?.Serial)
                        if (_enabledNotorieties.Contains(mobile.NotorietyFlag))
                            AddMobileHealthbar(mobile);
        }

        public static void CheckAndAddMobile(World world, uint serial)
        {
            Entity ent = world.Get(serial);

            if (ent is not Mobile mob) return;

            foreach (HealthbarCollectorGump collectorGump in UIManager.Gumps.OfType<HealthbarCollectorGump>())
                if (collectorGump._enabledNotorieties.Contains(mob.NotorietyFlag))
                    collectorGump.AddMobileHealthbar(mob);
        }

        public static void MobileDestroyed(uint serial)
        {
            foreach (HealthbarCollectorGump collectorGump in UIManager.Gumps.OfType<HealthbarCollectorGump>()) collectorGump.RemoveMobile(serial);
        }

        private void AddMobileHealthbar(Mobile mobile)
        {
            if (mobile == null || _healthbars.ContainsKey(mobile.Serial))
                return;

            // Don't add the player
            if (mobile.Serial == World.Player?.Serial)
                return;

            // Create compact healthbar
            var compactBar = new CompactHealthBar(World, mobile.Serial);
            _healthbars[mobile.Serial] = compactBar;
            _trackedMobiles[mobile.Serial] = mobile.NotorietyFlag;
            _container.Add(compactBar);
        }

        public void RemoveMobile(uint serial)
        {
            if (_healthbars.TryGetValue(serial, out CompactHealthBar bar))
            {
                bar.Dispose();
                _healthbars.Remove(serial);
                _trackedMobiles.Remove(serial);
            }
        }

        public override void PreDraw()
        {
            base.PreDraw();

            // Update border hue from color box
            if (_borderColorBox != null && _borderColorBox.Hue != _borderHue) _borderHue = _borderColorBox.Hue;

            // Handle resize
            if (_resizeHandle != null && _resizeHandle.IsDragging)
            {
                int newHeight = _resizeHandle.Y + 18;
                if (newHeight >= MIN_HEIGHT)
                {
                    Height = newHeight;

                    if (_background != null) _background.Height = Height;

                    if (_scrollArea != null) _scrollArea.UpdateHeight(Height - TOP_SECTION_HEIGHT - BORDER_WIDTH - 20);
                }
            }

            // Update sorting periodically if enabled
            if (_sortByDistance)
            {
                _sortUpdateCounter++;
                if (_sortUpdateCounter >= SORT_UPDATE_INTERVAL)
                {
                    _sortUpdateCounter = 0;
                    SortHealthbarsByDistance();
                }
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            base.Draw(batcher, x, y);

            // Draw border as plain lines
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(_borderHue, false, 1.0f);

            // Top border
            batcher.Draw(
                SolidColorTextureCache.GetTexture(Color.White),
                new Rectangle(x, y, Width, BORDER_WIDTH),
                hueVector
            );

            // Bottom border
            batcher.Draw(
                SolidColorTextureCache.GetTexture(Color.White),
                new Rectangle(x, y + Height - BORDER_WIDTH, Width, BORDER_WIDTH),
                hueVector
            );

            // Left border
            batcher.Draw(
                SolidColorTextureCache.GetTexture(Color.White),
                new Rectangle(x, y, BORDER_WIDTH, Height),
                hueVector
            );

            // Right border
            batcher.Draw(
                SolidColorTextureCache.GetTexture(Color.White),
                new Rectangle(x + Width - BORDER_WIDTH, y, BORDER_WIDTH, Height),
                hueVector
            );

            return true;
        }

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);

            // Save enabled notorieties as comma-separated flags
            if (_enabledNotorieties.Count > 0)
                writer.WriteAttributeString("notorieties",
                    string.Join(",", _enabledNotorieties.Select(n => (int)n)));

            // Save border hue
            writer.WriteAttributeString("borderHue", _borderHue.ToString());

            // Save sort state
            writer.WriteAttributeString("sortByDistance", _sortByDistance.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            // Restore notorieties
            string notorietiesStr = xml.GetAttribute("notorieties");
            if (!string.IsNullOrEmpty(notorietiesStr))
            {
                _enabledNotorieties.Clear();
                foreach (string s in notorietiesStr.Split(','))
                    if (int.TryParse(s, out int value))
                        _enabledNotorieties.Add((NotorietyFlag)value);
            }

            // Restore border hue
            if (ushort.TryParse(xml.GetAttribute("borderHue"), out ushort hue))
            {
                _borderHue = hue;
                if (_borderColorBox != null) _borderColorBox.Hue = hue;
            }

            // Restore sort state
            if (bool.TryParse(xml.GetAttribute("sortByDistance"), out bool sortByDistance))
            {
                _sortByDistance = sortByDistance;
                if (_sortButton != null) _sortButton.IsSelected = sortByDistance;
            }

            // Rebuild healthbar list based on restored notorieties
            RebuildHealthbarList();
        }

        public override void Dispose()
        {
            // Dispose all compact healthbars
            foreach (CompactHealthBar bar in _healthbars.Values) bar.Dispose();
            _healthbars.Clear();
            _trackedMobiles.Clear();

            base.Dispose();
        }

        private class ResizeHandle : Control
        {
            private bool _isDragging;
            private int _dragStartY;
            private int _startY;

            public ResizeHandle(int x, int y)
            {
                X = x;
                Y = y;
                Width = 16;
                Height = 16;
                AcceptMouseInput = true;
                CanMove = false;
            }

            public bool IsDragging => _isDragging;

            protected override void OnMouseDown(int x, int y, Input.MouseButtonType button)
            {
                if (button == Input.MouseButtonType.Left)
                {
                    _isDragging = true;
                    _dragStartY = Input.Mouse.Position.Y;
                    _startY = Y;
                }
            }

            protected override void OnMouseUp(int x, int y, Input.MouseButtonType button)
            {
                if (button == Input.MouseButtonType.Left) _isDragging = false;
            }

            public override void Update()
            {
                base.Update();

                if (_isDragging)
                {
                    int deltaY = Input.Mouse.Position.Y - _dragStartY;
                    int newY = _startY + deltaY;

                    // Calculate the minimum Y position based on MIN_HEIGHT
                    int minY = MIN_HEIGHT - 18;

                    // Clamp Y to valid range
                    if (newY < minY) newY = minY;

                    Y = newY;
                }
            }

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                // Draw resize handle (horizontal lines)
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, 0.5f);

                for (int i = 0; i < 3; i++)
                    batcher.Draw(
                        SolidColorTextureCache.GetTexture(Color.White),
                        new Rectangle(x, y + i * 4, Width, 2),
                        hueVector
                    );

                return true;
            }
        }

        private class CompactHealthBar : Control
        {
            private const int BAR_WIDTH = 100;
            private const int BAR_HEIGHT = 8;
            private readonly World _world;
            private readonly Label _nameLabel;
            private readonly HealthBarLine _hpBar;
            private readonly HealthBarLine _hpBackground;
            private Mobile _mobile;
            public int Distance = 0;

            public uint Serial { get; }

            public CompactHealthBar(World world, uint serial)
            {
                _world = world;
                Serial = serial;

                Width = 100;
                Height = 30;
                CanMove = true;
                AcceptMouseInput = true;

                Entity entity = world.Get(serial);
                if (entity == null || entity is not Mobile mob)
                {
                    Dispose();
                    return;
                }

                _mobile =  mob;
                Distance = entity.Distance;

                // Name label (centered)
                _nameLabel = new Label(string.Empty, true, Notoriety.GetHue((entity as Mobile)?.NotorietyFlag ?? NotorietyFlag.Gray), font: 1, style: FontStyle.BlackBorder)
                {
                    X = 0,
                    Y = 2,
                    Width = BAR_WIDTH
                };
                SetName();
                Add(_nameLabel);

                // HP background (red/gray bar)
                _hpBackground = new HealthBarLine(0, 16, BAR_WIDTH, BAR_HEIGHT, Color.DarkRed);
                Add(_hpBackground);

                // HP foreground (blue bar)
                _hpBar = new HealthBarLine(0, 16, BAR_WIDTH, BAR_HEIGHT, Color.DodgerBlue);
                Add(_hpBar);

                WantUpdateSize = false;
            }

            private void SetName()
            {
                Distance = _mobile.Distance;
                _nameLabel.Text = $"{_mobile.Name} ({Distance})";
            }

            public override void PreDraw()
            {
                base.PreDraw();

                if (_mobile.IsDestroyed)
                {
                    Dispose();
                    return;
                }

                // Update name if changed
                if ((!string.IsNullOrEmpty(_mobile.Name) && _nameLabel.Text != _mobile.Name) || Distance != _mobile.Distance) SetName();

                // Update HP bar width
                if (_mobile.HitsMax > 0)
                {
                    int hpWidth = CalculatePercents(_mobile.HitsMax, _mobile.Hits, BAR_WIDTH);
                    _hpBar.BarWidth = hpWidth;

                    // Change color based on status
                    if (_mobile.IsPoisoned)
                        _hpBar.BarColor = SolidColorTextureCache.GetTexture(Color.LimeGreen);
                    else if (_mobile.IsYellowHits)
                        _hpBar.BarColor = SolidColorTextureCache.GetTexture(Color.Orange);
                    else
                        _hpBar.BarColor = SolidColorTextureCache.GetTexture(Color.DodgerBlue);

                    // Update notoriety color
                    ushort hue = Notoriety.GetHue(_mobile.NotorietyFlag);
                    if (_nameLabel.Hue != hue) _nameLabel.Hue = hue;
                }
            }

            protected override void OnMouseDown(int x, int y, Input.MouseButtonType button)
            {
                if (button == Input.MouseButtonType.Left)
                    // Target on left click
                    _world.TargetManager.Target(Serial);
                base.OnMouseDown(x, y, button);
            }

            protected override void OnMouseOver(int x, int y)
            {
                Entity entity = _world.Get(Serial);
                if (entity != null)
                {
                    SelectedObject.HealthbarObject = entity;
                    SelectedObject.Object = entity;
                }
                base.OnMouseOver(x, y);
            }

            protected override bool OnMouseDoubleClick(int x, int y, Input.MouseButtonType button)
            {
                if (button == Input.MouseButtonType.Left)
                {
                    Entity entity = _world.Get(Serial);
                    if (entity != null)
                    {
                        if (entity != _world.Player)
                        {
                            if (_world.Player.InWarMode)
                                GameActions.Attack(_world, entity);
                            else if (!GameActions.OpenCorpse(_world, entity)) GameActions.DoubleClick(_world, entity);
                        }
                        else
                            GameActions.DoubleClick(_world, entity);
                    }
                    return true;
                }
                return false;
            }

            private static int CalculatePercents(int max, int current, int maxValue)
            {
                if (max > 0)
                {
                    max = current * 100 / max;

                    if (max > 100) max = 100;

                    if (max > 1) max = maxValue * max / 100;
                }

                return max;
            }

            private class HealthBarLine : Control
            {
                private Texture2D _texture;
                public int BarWidth { get; set; }

                public Texture2D BarColor
                {
                    get => _texture;
                    set => _texture = value;
                }

                public HealthBarLine(int x, int y, int maxWidth, int height, Color color)
                {
                    X = x;
                    Y = y;
                    Width = maxWidth;
                    Height = height;
                    BarWidth = maxWidth;
                    _texture = SolidColorTextureCache.GetTexture(color);
                    CanMove = true;
                }

                public override bool Draw(UltimaBatcher2D batcher, int x, int y)
                {
                    Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, 1.0f);

                    batcher.Draw(
                        _texture,
                        new Rectangle(x, y, BarWidth, Height),
                        hueVector
                    );

                    return true;
                }
            }
        }
    }
}
