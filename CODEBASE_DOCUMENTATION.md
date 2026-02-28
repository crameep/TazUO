# TazUO Codebase - Comprehensive Documentation for Agents

## Table of Contents
1. [Project Overview](#1-project-overview)
2. [Repository Structure](#2-repository-structure)
3. [Build System](#3-build-system)
4. [Architecture Overview](#4-architecture-overview)
5. [Project Dependency Graph](#5-project-dependency-graph)
6. [Entry Point & Startup Flow](#6-entry-point--startup-flow)
7. [Core Game Systems](#7-core-game-systems)
8. [Networking](#8-networking)
9. [Rendering & Graphics](#9-rendering--graphics)
10. [UI System (Gumps & Controls)](#10-ui-system-gumps--controls)
11. [Game World & Entities](#11-game-world--entities)
12. [Asset System](#12-asset-system)
13. [Configuration & Settings](#13-configuration--settings)
14. [Scripting System (LegionScripting)](#14-scripting-system-legionscripting)
15. [Plugin System](#15-plugin-system)
16. [Manager Classes Reference](#16-manager-classes-reference)
17. [Game Data & Constants](#17-game-data--constants)
18. [ImGui Integration](#18-imgui-integration)
19. [Input System](#19-input-system)
20. [Audio System](#20-audio-system)
21. [Map System](#21-map-system)
22. [Data Storage & Persistence](#22-data-storage--persistence)
23. [External Dependencies](#23-external-dependencies)
24. [Command-Line Arguments](#24-command-line-arguments)
25. [Key Namespaces](#25-key-namespaces)
26. [File Index by Category](#26-file-index-by-category)
27. [Conventions & Guidelines](#27-conventions--guidelines)

---

## 1. Project Overview

**TazUO** is a feature-rich open-source Ultima Online game client, originally forked from ClassicUO. It's a full game client implementation that reads original UO data files and connects to UO servers.

- **Repository**: https://github.com/PlayTazUO/TazUO
- **Language**: C# (.NET 10.0, C# Preview)
- **Graphics**: FNA (XNA reimplementation) with OpenGL/Vulkan support
- **License**: BSD-2-Clause
- **Codebase Size**: ~640 C# files, ~222,000 lines of code
- **Platforms**: Windows (primary), Linux, macOS (Intel + ARM64)

### Key Features
- Grid Containers (visual inventory)
- Built-in Python scripting (IronPython 3.4.2) + LegionScript
- Auto-loot, auto-buy/sell agents
- Controller support (gamepad)
- Improved journal, buff bars, cooldown bars
- Custom TTF fonts
- Tooltip overrides
- Grid item highlighting by properties
- ImGui-based advanced UI panels
- Discord rich presence integration
- Alternative paperdoll

---

## 2. Repository Structure

```
TazUo/
├── ClassicUO.sln                    # Main Visual Studio solution
├── ClassicUO.sln.DotSettings        # ReSharper/Rider code style
├── ClassicUO.licenseheader           # License header template (BSD-2-Clause)
├── .editorconfig                     # EditorConfig formatting rules
├── README.md                         # Project overview
├── LICENSE.md                        # BSD-2-Clause license
├── CLAUDE.md                         # Claude agent guide
├── CODEBASE_DOCUMENTATION.md         # This file
├── FeaturesBot.py                    # Features bot script
├── format.sh                         # Code formatting script
├── .gitmodules                       # Git submodule definitions
├── .github/                          # GitHub workflows & templates
├── .claude/                          # Claude agent configuration
│
├── src/                              # Source code (6 projects)
│   ├── Directory.Build.props         # Shared MSBuild properties
│   ├── ClassicUO.Client/            # Main executable - game client
│   ├── ClassicUO.Assets/            # Asset loading (UO files, fonts, gumps)
│   ├── ClassicUO.Renderer/          # Rendering engine (FNA-based)
│   ├── ClassicUO.IO/               # I/O for UO file formats
│   ├── ClassicUO.Utility/          # Utilities, collections, logging
│   ├── ClassicUO.CUOAPI/           # CUO API wrapper
│   └── APIToMarkdown/              # Scripting API doc generator
│
├── external/                         # External dependencies
│   ├── FNA/                         # Git submodule - XNA reimplementation
│   ├── MP3Sharp/                    # Git submodule - MP3 decoder
│   ├── FileEmbed/                   # Git submodule - File embedding source gen
│   ├── FontStashSharp/              # Font rendering library
│   ├── DiscordSocialSDK.Wrapper/    # Discord integration
│   ├── cuoapi/                      # ClassicUO API binary (cuoapi.dll)
│   ├── iplib/                       # Python standard library (348+ modules)
│   ├── x64/                         # Windows x64 native binaries
│   ├── lib64/                       # Linux x64 native binaries
│   ├── osx/                         # macOS Intel native binaries
│   ├── osx-arm/                     # macOS ARM64 native binaries
│   ├── win-arm/                     # Windows ARM native binaries
│   └── vulkan/icd.d/               # Vulkan ICD definitions
│
├── tests/                            # Unit tests
│   └── ClassicUO.UnitTests/         # xUnit test project
│
└── tools/                            # Build tools
    ├── ManifestCreator/              # Deployment manifest generator (net8.0)
    ├── EventGenerator/               # Roslyn source generator for events
    └── ws/                           # Miscellaneous tools
```

### ClassicUO.Client Internal Structure
```
src/ClassicUO.Client/
├── Main.cs                           # Entry point (Bootstrap class)
├── Client.cs                         # GameController, UltimaOnline class
├── PluginHost.cs                     # Plugin host interface
├── CUOEnviroment.cs                  # Environment/version info
├── Configuration/                    # Settings & profiles
│   ├── Settings.cs                  # Global settings (settings.json)
│   ├── Profile.cs                   # Character profile (~750+ properties)
│   ├── ProfileManager.cs            # Profile management
│   ├── ConfigurationResolver.cs     # JSON load/save with safety
│   └── Json/                        # JSON serialization context
├── Game/                             # Core game logic
│   ├── World.cs                     # Central game state singleton
│   ├── Constants.cs                 # Game constants
│   ├── GameActions.cs               # Player actions
│   ├── Time.cs                      # Time management
│   ├── UoAssist.cs                  # UO Assist integration
│   ├── Data/                        # Game data structures & enums
│   ├── GameObjects/                 # Entities (Item, Mobile, etc.)
│   ├── Managers/                    # 70+ manager subsystems
│   ├── Map/                         # Map & terrain system
│   ├── Scenes/                      # Game scenes (Login, Game, Main)
│   └── UI/                          # User interface
│       ├── Controls/                # 60+ UI control classes
│       ├── Gumps/                   # 95+ game windows ("gumps")
│       │   ├── CharCreation/        # Character creation screens
│       │   ├── Login/               # Login UI screens
│       │   ├── SpellBar/            # Spell bar UI
│       │   ├── GridHighLight/       # Grid highlighting config
│       │   └── DiscordGump/         # Discord integration UI
│       └── ImGuiControls/           # 30+ ImGui panels
│           ├── Legion/              # Scripting ImGui controls
│           └── Agents/              # Agent config ImGui tabs
├── Input/                            # Keyboard/mouse/controller input
├── Network/                          # Server communication
│   ├── PacketHandlers.cs            # Incoming packet handlers
│   ├── OutgoingPackets.cs           # Outgoing packet builders
│   ├── PacketsTable.cs             # Packet size definitions
│   ├── Plugin.cs                    # Plugin integration layer
│   ├── Encryption/                  # Packet encryption
│   ├── EnhancedPackets/            # Enhanced protocol extensions
│   └── Socket/                      # Low-level socket handling
├── LegionScripting/                  # Python/LegionScript system
│   ├── API.cs                       # Main scripting API
│   ├── LegionScripting.cs          # Script runtime manager
│   ├── LScriptSettings.cs          # Script settings
│   ├── PyClasses/                   # 35+ Python binding classes
│   └── docs/                        # Auto-generated API docs
├── Resources/                        # Localization strings
│   ├── ResGeneral.resx
│   ├── ResErrorMessages.resx
│   └── ResGumps.resx
└── Properties/
    └── launchSettings.json           # Launch profiles
```

---

## 3. Build System

### Shared Build Properties (`src/Directory.Build.props`)
- **Target Framework**: `net10.0`
- **Language Version**: Preview (C# Preview features)
- **Unsafe Blocks**: Enabled
- **Self-Contained**: true
- **Platform**: x64 (primary), AnyCPU
- **Dev Build**: Set `IS_DEV_BUILD=true` for DEV_BUILD constant

### Main Executable (`ClassicUO.Client.csproj`)
- **Output Type**: WinExe
- **Assembly Name**: TazUO
- **Root Namespace**: ClassicUO
- **Assembly Version**: 4.16.0
- **NuGet Packages**: IronPython 3.4.2, ImGui.NET 1.91.6.1, Microsoft.Data.Sqlite 8.0.0

### Build Commands
```bash
# Debug build
dotnet build ClassicUO.sln

# Release build
dotnet build ClassicUO.sln -c Release

# Dev build (adds DEV_BUILD constant)
dotnet build ClassicUO.sln /p:IS_DEV_BUILD=true

# Run tests
dotnet test tests/ClassicUO.UnitTests/ClassicUO.UnitTests.csproj

# Publish distributable
dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release

# Run with skip login
dotnet run --project src/ClassicUO.Client -- -skiploginscreen

# Run with profiler
dotnet run --project src/ClassicUO.Client -- -profiler
```

### Build Output
- Debug: `bin/Debug/`
- Release: `bin/Release/`
- Publish: `bin/dist/`

### Post-Build Steps
1. **GenerateDocs** - Runs APIToMarkdown to generate scripting API docs
2. **CreateVersionFile** - Writes version to `v.txt`
3. **CopyExternalDeps** - Copies platform-specific native libraries

### Test Configuration
- **Framework**: xUnit 2.9.0
- **Assertions**: FluentAssertions 6.12.0
- **SDK**: Microsoft.NET.Test.Sdk 17.10.0
- **Coverage**: coverlet.collector 6.0.2

---

## 4. Architecture Overview

TazUO follows a layered architecture with clear separation of concerns:

```
┌─────────────────────────────────────────────────────┐
│                 ClassicUO.Client                     │
│  ┌───────────┐ ┌──────────┐ ┌────────────────────┐  │
│  │  Scenes   │ │   UI     │ │  LegionScripting   │  │
│  │  (Login,  │ │ (Gumps,  │ │  (Python/Legion    │  │
│  │  Game,    │ │ Controls,│ │   scripting API)    │  │
│  │  Main)    │ │ ImGui)   │ │                     │  │
│  └─────┬─────┘ └────┬─────┘ └────────┬───────────┘  │
│        │             │                │               │
│  ┌─────┴─────────────┴────────────────┴───────────┐  │
│  │              Game Systems                       │  │
│  │  World, Managers, GameObjects, Map, Network     │  │
│  └─────────────────────┬───────────────────────────┘  │
│                        │                              │
│  ┌─────────────────────┴───────────────────────────┐  │
│  │           Configuration / Input                  │  │
│  └─────────────────────────────────────────────────┘  │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────┴──────────────────────────────┐
│              ClassicUO.Renderer                       │
│  FNA-based rendering: Batcher2D, Camera, Effects,    │
│  Texture Atlas, Shaders, Animation/Art/Gump sprites  │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────┴──────────────────────────────┐
│              ClassicUO.Assets                         │
│  UO file loaders: Art, Animations, Gumps, Hues,     │
│  Maps, Sounds, Fonts, TileData, Cliloc              │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────┴──────────────────────────────┐
│              ClassicUO.IO                             │
│  Low-level I/O: UOFile, UOFileMul, UOFileUop,       │
│  StackDataReader/Writer, Audio (Sound/Music)         │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────┴──────────────────────────────┐
│              ClassicUO.Utility                        │
│  Helpers, Collections, Logging, Platforms, ZLib,     │
│  StbRectPack, StbTextedit, Crypter, MathHelper      │
└─────────────────────────────────────────────────────┘
```

---

## 5. Project Dependency Graph

```
ClassicUO.Client (Main Exe)
├── ClassicUO.Assets
│   ├── ClassicUO.IO
│   │   ├── ClassicUO.Utility
│   │   ├── FNA.Core
│   │   └── MP3Sharp
│   ├── ClassicUO.Utility
│   └── FontStashSharp.FNA.Core
├── ClassicUO.Renderer
│   ├── ClassicUO.Assets
│   ├── ClassicUO.Utility
│   └── FontStashSharp.FNA.Core
├── ClassicUO.Utility
│   ├── FNA.Core
│   └── SixLabors.ImageSharp 3.1.11
├── ClassicUO.CUOAPI
│   ├── ClassicUO.Utility
│   ├── FNA.Core
│   └── MP3Sharp
├── DiscordSocialSDK.Wrapper
├── FNA.Core (submodule)
├── IronPython 3.4.2 (NuGet)
├── ImGui.NET 1.91.6.1 (NuGet)
├── Microsoft.Data.Sqlite 8.0.0 (NuGet)
└── EventGenerator (Roslyn Analyzer)
```

---

## 6. Entry Point & Startup Flow

**File**: `src/ClassicUO.Client/Main.cs`

### Entry Points
```csharp
public static class Bootstrap
{
    [STAThread]
    public static void Main(string[] args) => Boot(null, args);  // Standard entry

    [UnmanagedCallersOnly(EntryPoint = "Initialize")]
    static unsafe void Initialize(...)  // Plugin host entry

    public static void Boot(UnmanagedAssistantHost pluginHost, string[] args)  // Core boot
}
```

### Startup Sequence
1. **CopyRequiredLibs()** - Copy platform-specific native libraries (x64/lib64/osx/osx-arm)
2. **Set InvariantCulture** - Normalize number/date formatting
3. **Language.Load()** - Load localization strings
4. **Log.Start(LogTypes.All)** - Initialize logging
5. **Set GameThread** - Store main thread reference in `CUOEnviroment.GameThread`
6. **Configure Exception Handler** - Crash log generation (HTML + crash.txt)
7. **ReadSettingsFromArgs(args)** - Parse command-line arguments
8. **Set Environment Variables** - FNA graphics config, SDL3 settings, DPI awareness
9. **Load Global Settings** - From `settings.json`
10. **Validate Language** - Detect from OS or use "ENU"
11. **Validate UO Directory** - Check for `tiledata.mul`
12. **Validate Client Version** - From settings or parse from `client.exe`
13. **Set Graphics Driver** - OpenGL (default) or Vulkan
14. **Client.Run(pluginHost)** - Start game loop

### Game Initialization (`Client.cs` / `UltimaOnline` class)
```
UltimaOnline.Load(game):
  1. LoadUOFiles() - Load all UO data files
  2. Create GPU texture samplers
  3. Initialize renderers: Animations, Arts, Gumps, Texmaps, Lights, MultiMaps, Sounds
  4. Create World instance
  5. Create GameCursor
```

### Scene System
| Scene | Class | File | Purpose |
|-------|-------|------|---------|
| Login | `LoginScene` | `Game/Scenes/LoginScene.cs` | Server connection, character selection |
| Game | `GameScene` | `Game/Scenes/GameScene.cs` | Main gameplay (partial class, split across 3 files) |
| Main | `MainScene` | `Game/Scenes/MainScene.cs` | Initial/transition scene |

**Scene Base Class** (`Scene.cs`): Abstract class with `Load()`, `Unload()`, `Update()`, `Draw()`, and input event methods.

---

## 7. Core Game Systems

### World (`Game/World.cs`)
The `World` class is the central game state singleton. It owns all manager subsystems:

```csharp
public sealed class World
{
    public static World Instance { get; private set; }

    // Entity collections
    public Dictionary<uint, Item> Items { get; }
    public Dictionary<uint, Mobile> Mobiles { get; }
    public PlayerMobile Player { get; }
    public Maps.Map Map { get; }

    // Core managers (all created in constructor)
    public WorldMapEntityManager WMapManager { get; }
    public CorpseManager CorpseManager { get; }
    public PartyManager Party { get; }
    public HouseManager HouseManager { get; }
    public WorldTextManager WorldTextManager { get; }
    public MessageManager MessageManager { get; }
    public ContainerManager ContainerManager { get; }
    public IgnoreManager IgnoreManager { get; }
    public SkillsGroupManager SkillsGroupManager { get; }
    public ChatManager ChatManager { get; }
    public AuraManager AuraManager { get; }
    public TargetManager TargetManager { get; }
    public DelayedObjectClickManager DelayedObjectClickManager { get; }
    public BoatMovingManager BoatMovingManager { get; }
    public NameOverHeadManager NameOverHeadManager { get; }
    public MacroManager Macros { get; }
    public CommandManager CommandManager { get; }
    public Weather Weather { get; }
    public InfoBarManager InfoBars { get; }
    public DurabilityManager DurabilityManager { get; }
    public ObjectPropertiesListManager OPL { get; }
    public CoolDownBarManager CoolDownBarManager { get; }
    public ActiveSpellIconsManager ActiveSpellIcons { get; }
}
```

---

## 8. Networking

### File Organization
```
src/ClassicUO.Client/Network/
├── PacketHandlers.cs                 # Incoming packet processing (server -> client)
├── OutgoingPackets.cs                # Outgoing packet builders (client -> server)
├── PacketsTable.cs                   # Packet ID -> size mapping (0x00-0xFF)
├── PacketLogger.cs                   # Packet logging for debugging
├── Plugin.cs                         # Plugin integration for packet interception
├── Encryption/                       # Encryption implementations
│   ├── Encryption.cs
│   ├── BlowfishBehaviour.cs
│   ├── TwofishBehaviour.cs
│   ├── MD5Behaviour.cs
│   ├── LoginCryptBehaviour.cs
│   └── Huffman.cs                   # Huffman compression
├── Socket/                           # Low-level networking
│   ├── AsyncNetClient.cs            # Async TCP client
│   ├── CircularBuffer.cs            # Circular buffer for data
│   ├── NetStatistics.cs             # Network statistics
│   ├── SocketWrapper.cs             # Socket abstraction
│   ├── TcpSocketWrapper.cs          # TCP socket implementation
│   └── WebSocketWrapper.cs          # WebSocket implementation
└── EnhancedPackets/                  # TazUO enhanced protocol
    ├── EnhancedPacketHandler.cs      # Enhanced packet processing
    ├── EnhancedOutgoingPackets.cs    # Enhanced outgoing packets
    ├── EnhancedPacketTypeEnum.cs     # Enhanced packet types
    ├── Extensions.cs                 # Helper extensions
    └── EnhancedPackets.md            # Protocol documentation
```

### Packet System
- **PacketsTable**: Defines fixed sizes for packets 0x00-0xFF (-1 = variable length)
- **PacketHandlers**: Registered handlers for each incoming packet ID
- **OutgoingPackets**: Static methods to build and send packets to server
- **Encryption**: Supports Blowfish, Twofish, MD5, Login crypt, Huffman compression
- **Plugin Interception**: Plugins can intercept both incoming and outgoing packets

---

## 9. Rendering & Graphics

### Renderer Project (`ClassicUO.Renderer`)
```
src/ClassicUO.Renderer/
├── Batching/
│   ├── Batcher2D.cs                 # Main 2D sprite batcher (UltimaBatcher2D)
│   ├── BatchCommand.cs              # Draw command abstraction
│   └── (various GPU state commands)
├── Camera.cs                         # Viewport camera (zoom 0.3x-3.0x)
├── TextureAtlas.cs                   # Texture atlas for sprite batching
├── PixelPicker.cs                    # Pixel-level picking for clicks
├── SolidColorTextureCache.cs         # Solid color texture cache
├── Primitives2D.cs                   # Basic 2D shape rendering
├── SpriteInfo.cs                     # Sprite information
├── ShaderHueTranslator.cs            # Hue/color translation shader
├── Animations/
│   ├── Animation.cs                 # Animation sprite data
│   ├── AnimationDirection.cs        # Direction-based animation
│   └── AnimationGroup.cs            # Animation group management
├── Arts/
│   └── Art.cs                       # Static/item art rendering
├── Gumps/
│   └── Gump.cs                      # Gump texture rendering
├── Lights/
│   └── Light.cs                     # Light texture rendering
├── Texmaps/
│   └── Texmap.cs                    # Terrain texture rendering
├── MultiMaps/
│   └── MultiMap.cs                  # Multi-object map rendering
├── Sounds/
│   └── Sound.cs                     # Sound playback wrapper
├── Effects/
│   ├── BasicUOEffect.cs             # Basic shader effect
│   └── XBREffect.cs                 # XBR scaling effect
├── fonts/                            # Embedded TrueType fonts
│   └── Roboto-Regular.ttf
├── Fonts.cs                          # Font management
├── SpriteFont.cs                     # Sprite font rendering
└── shaders/                          # HLSL/GLSL shader files
```

### Key Rendering Classes
- **UltimaBatcher2D** (via `Batcher2D.cs`): Main sprite batcher for all 2D rendering
- **Camera**: Handles viewport transforms, zoom (0.3x - 3.0x), and scrolling
- **TextureAtlas**: Packs sprites into atlas textures for efficient GPU batching
- **ShaderHueTranslator**: Translates UO hue values to shader parameters

### Graphics Pipeline
1. FNA (XNA reimplementation) provides the graphics framework
2. Supports OpenGL (default) and Vulkan backends
3. Sprites batched via TextureAtlas for efficiency
4. Custom shader effects (XBR scaling, hue translation, lighting)

---

## 10. UI System (Gumps & Controls)

### Terminology
- **Gump**: A game window/dialog (from "Graphical User Menu Pop-up")
- **Control**: A reusable UI widget (button, label, scrollbar, etc.)

### Base Classes
- **Control** (`Game/UI/Controls/Control.cs`): Base class for all UI elements
- **Gump** (`Game/UI/Gumps/Gump.cs`): Base class for all game windows, extends Control
- **AnchorableGump**: Gumps that can be anchored/docked together
- **ResizableGump**: Gumps that can be resized
- **NineSliceGump**: Gumps with nine-slice rendering
- **UIManager** (`Game/Managers/UIManager.cs`): Central UI controller

### Gumps (95+ Window Types)
**Core Gameplay:**
| Gump | File | Purpose |
|------|------|---------|
| `WorldViewportGump` | `WorldViewportGump.cs` | Main game viewport |
| `PaperdollGump` | `PaperdollGump.cs` | Character equipment/paperdoll |
| `ModernPaperdoll` | `ModernPaperdoll.cs` | TazUO alternative paperdoll |
| `StatusGump` | `StatusGump.cs` | Character stats display |
| `ContainerGump` | `ContainerGump.cs` | Standard container view |
| `GridContainer` | `GridContainer.cs` | TazUO grid inventory view |
| `SpellbookGump` | `SpellbookGump.cs` | Spellbook display |
| `SkillGumpAdvanced` | `SkillGumpAdvanced.cs` | Skills window |
| `MapGump` | `MapGump.cs` | In-game map |
| `WorldMapGump` | `WorldMapGump.cs` | World map overview |
| `MiniMapGump` | `MiniMapGump.cs` | Mini-map |

**Chat & Communication:**
| Gump | Purpose |
|------|---------|
| `JournalGump` | Standard journal |
| `ResizableJournal` | Improved resizable journal |
| `ChatGump` | Chat window |
| `PartyGump` | Party management |

**Combat & Spells:**
| Gump | Purpose |
|------|---------|
| `BuffGump` | Standard buff display |
| `ImprovedBuffGump` | TazUO improved buff bar |
| `CoolDownBar` | Cooldown timer display |
| `SpellBar` | Spell quick-access bar |
| `CombatBookGump` | Combat abilities |
| `HealthBarGump` | Health bars |
| `HealthbarCollectorGump` | Health bar group |

**Configuration:**
| Gump | Purpose |
|------|---------|
| `ModernOptionsGump` | Main options window |
| `MacroGump` | Macro editor |
| `IgnoreManagerGump` | Ignore list management |
| `DressAgentConfigGump` | Dress agent configuration |
| `TooltipConfigGump` | Tooltip customization |
| `MarkersManagerGump` | Map markers |

**Trading & Commerce:**
| Gump | Purpose |
|------|---------|
| `ShopGump` | Standard shop UI |
| `ModernShopGump` | Modern shop interface |
| `TradingGump` | Player-to-player trading |

**Login Flow:**
- `LoginGump` - Login form
- `ServerSelectionGump` - Server selection
- `CharacterSelectionGump` - Character selection
- `LoadingGump` - Loading screen
- Character creation: `CharCreationGump`, `CreateCharAppearanceGump`, `CreateCharCityGump`, `CreateCharProfessionGump`, `CreateCharTradeGump`

### Controls (60+ Widget Types)
| Control | Purpose |
|---------|---------|
| `Button` | Clickable button |
| `NiceButton` | Styled button |
| `NineSliceButton` | Nine-slice styled button |
| `Label` | Text label |
| `FadingLabel` | Label with fade effect |
| `HoveredLabel` | Label with hover state |
| `StbTextBox` | Text input (STB-based) |
| `TTFTextInputField` | TTF font text input |
| `Checkbox` | Toggle checkbox |
| `RadioButton` | Radio button group |
| `Combobox` | Dropdown selector |
| `HSliderBar` | Horizontal slider |
| `ScrollArea` | Scrollable container |
| `ModernScrollArea` | Modern scrollable container |
| `ScrollBar` / `ScrollBarBase` | Scroll bars |
| `HtmlControl` | HTML-rendered content |
| `GumpPic` | Gump image display |
| `StaticPic` | Static art image |
| `ItemGump` | Item display widget |
| `ColorPickerBox` | Color picker |
| `HotkeyBox` | Hotkey binding input |
| `MacroControl` | Macro configuration widget |
| `ExpandableScroll` | Expandable scroll container |
| `HBoxContainer` / `VBoxContainer` | Layout containers |
| `TableContainer` | Table layout |
| `SettingsSection` | Options section container |
| `ProgressBar` | Progress indicator |
| `ContextMenuControl` | Right-click context menu |

---

## 11. Game World & Entities

### Entity Hierarchy
```
GameObject (base)
├── Entity (has serial, name, properties)
│   ├── Item
│   │   └── (containers, equipment, etc.)
│   └── Mobile
│       └── PlayerMobile (the local player)
├── Land (terrain tiles)
├── Static (static world objects)
├── Multi (multi-tile structures like houses)
├── GameEffect (base for effects)
│   ├── FixedEffect
│   ├── MovingEffect
│   ├── DragEffect
│   └── LightningEffect
└── TextObject (floating text)
```

### Key Entity Files
```
src/ClassicUO.Client/Game/GameObjects/
├── GameObject.cs              # Base game object
├── Entity.cs                  # Entity with serial/name/properties
├── EntityCollection.cs        # Entity collection management
├── EntityTextContainer.cs     # Text associated with entities
├── Item.cs                    # Items (equipment, containers, etc.)
├── Mobile.cs                  # Characters, NPCs, creatures
├── MobileAnimation.cs         # Mobile animation state
├── PlayerMobile.cs            # Local player character
├── Land.cs                    # Terrain tiles
├── Static.cs                  # Static world objects
├── Multi.cs                   # Multi-tile structures
├── House.cs                   # House structures
├── GameEffect.cs              # Base effect class
├── FixedEffect.cs             # Fixed position effects
├── MovingEffect.cs            # Projectile effects
├── DragEffect.cs              # Drag effects
├── LightningEffect.cs         # Lightning effects
├── IsometricLight.cs          # Light data for isometric rendering
├── LineOfSightHelper.cs       # Line of sight calculations
├── RenderedText.cs            # Rendered text objects
└── TextObject.cs              # Floating text
```

### Entity Properties
- **Serial** (`uint`): Unique identifier for all game entities
- **Graphic** (`ushort`): Visual appearance ID
- **Hue** (`ushort`): Color/tint value
- **Position**: X, Y, Z coordinates in game world
- **Direction**: Facing direction
- **Flags**: Entity state flags (see `EntityFlags.cs`)
- **Notoriety**: PvP status flags (see `NotorietyFlag.cs`)

---

## 12. Asset System

### UO File Loaders (`ClassicUO.Assets`)
These classes load original Ultima Online data files:

| Loader | File | UO Files | Purpose |
|--------|------|----------|---------|
| `ArtLoader` | `ArtLoader.cs` | art.mul/idx | Item & static art |
| `AnimationsLoader` | `AnimationsLoader.cs` | anim*.mul/idx | Character/creature animations |
| `AnimDataLoader` | `AnimDataLoader.cs` | animdata.mul | Animation frame data |
| `GumpsLoader` | `GumpsLoader.cs` | gumpart*.mul | UI graphics |
| `HuesLoader` | `HuesLoader.cs` | hues.mul | Color hue tables |
| `MapLoader` | `MapLoader.cs` | map*.mul, statics*.mul | World maps |
| `SoundsLoader` | `SoundsLoader.cs` | sound*.mul | Sound effects |
| `SoundOverrideLoader` | `SoundOverrideLoader.cs` | - | Sound overrides |
| `TexmapsLoader` | `TexmapsLoader.cs` | texmaps*.mul | Terrain textures |
| `TileDataLoader` | `TileDataLoader.cs` | tiledata.mul | Tile properties |
| `ClilocLoader` | `ClilocLoader.cs` | cliloc.* | Localized text strings |
| `FontsLoader` | `FontsLoader.cs` | fonts.mul | Game fonts |
| `LightsLoader` | `LightsLoader.cs` | light*.mul | Light textures |
| `MultiLoader` | `MultiLoader.cs` | multi.* | Multi-tile objects |
| `MultiMapLoader` | `MultiMapLoader.cs` | multimap.rle | Multi-map data |
| `ProfessionLoader` | `ProfessionLoader.cs` | prof.txt | Character professions |
| `SkillsLoader` | `SkillsLoader.cs` | skills.* | Skill definitions |
| `SpeechesLoader` | `SpeechesLoader.cs` | speech.mul | Speech keywords |
| `VerdataLoader` | `VerdataLoader.cs` | verdata.mul | Version patches |
| `TrueTypeLoader` | `TrueTypeLoader.cs` | - | TTF font loading |
| `PNGLoader` | `PNGLoader.cs` | - | PNG image loading |

### Custom Assets
- `src/ClassicUO.Assets/gumpartassets/` - Custom UI graphics (PNG replacements)
- `src/ClassicUO.Assets/fonts/` - Additional TrueType fonts
- `TileArt.cs` - Custom tile art definitions

### File Manager
- `UOFileManager.cs` - Central manager for all UO file loaders
- `UOFileLoader.cs` - Base class for all file loaders

### Low-Level I/O (`ClassicUO.IO`)
```
src/ClassicUO.IO/
├── UOFile.cs                  # Base UO file handler
├── UOFileMul.cs               # .MUL file format handler
├── UOFileUop.cs               # .UOP file format handler
├── UOFileIndex.cs             # Index file handler
├── UOFilesOverrideMap.cs      # File override system
├── FileReader.cs              # File reading utilities
├── MMFileReader.cs            # Memory-mapped file reader
├── StackDataReader.cs         # Stack-based data reader (efficient, no alloc)
├── StackDataWriter.cs         # Stack-based data writer
├── DefReader.cs               # Definition file reader
└── Audio/
    ├── Sound.cs               # Sound data handling
    ├── UOSound.cs             # UO sound file handling
    └── UOMusic.cs             # UO music file handling
```

---

## 13. Configuration & Settings

### Global Settings (`Configuration/Settings.cs`)
- **Storage**: `settings.json` (in executable directory)
- **Key Properties**:
  - Username, Password, IP, Port
  - UltimaOnlineDirectory, ProfilesPath
  - ClientVersion, Language
  - FPS, WindowPosition, WindowSize
  - SaveAccount, AutoLogin, Reconnect
  - LoginMusic, LoginMusicVolume
  - ForceDriver, Encryption, Plugins

### Character Profiles (`Configuration/Profile.cs`)
- **Storage**: `{ProfilesPath}/{Username}/{ServerName}/{CharacterName}/profile.json`
- **~750+ configurable properties** organized by category:

**Sound**: EnableSound, SoundVolume, EnableMusic, MusicVolume, EnableFootstepsSound, EnableCombatMusic, ReproduceSoundsInBackground

**Visual**: BackpackStyle, HighlightGameObjects, ShowMobilesHP, DrawRoofs, TreeToStumps, HideVegetation, UseCircleOfTransparency, CircleOfTransparencyRadius, DefaultScale

**Speech/Journal**: ChatFont, SpeechDelay, SaveJournalToFile, ForceUnicodeJournal, 20+ hue color settings

**Movement**: EnablePathfind, AlwaysRun, AlwaysRunUnlessHidden

**Experimental**: AutoOpenDoors, AutoOpenCorpses, EnableDragSelect, CastSpellsByOneClick

**Bandage Agent**: EnableBandageAgent, BandageAgentDelay, BandageAgentHPPercentage

**Auto Agents**: SellAgentEnabled, BuyAgentEnabled, EnableScavenger

### SQL Settings (`Game/Managers/SQLSettingsManager.cs`)
- **Storage**: `Data/settings.db` (SQLite)
- **Schema**: `settings(scope TEXT, name TEXT, value TEXT)`
- **Scopes**: Global, Character
- **Backup System**: 3 rotating backups
- **Key SQL Settings** (from `Constants.SqlSettings`):
  - DISABLE_WEATHER, SCALE_PETS_ENABLED
  - AUTO_UNEQUIP_FOR_ACTIONS
  - QUICK_HEAL_SPELL, QUICK_CURE_SPELL
  - ENHANCED_PACKETS_ENABLED
  - MIN_GUMP_MOVE_DIST

### Configuration Flow
```
Settings.json (global)
  └── Profile.json (per character)
      └── settings.db (SQL, per character scope)
          └── gumps.xml (UI layout)
              └── grid_containers.json (grid container state)
```

---

## 14. Scripting System (LegionScripting)

### Architecture
```
src/ClassicUO.Client/LegionScripting/
├── API.cs                     # Main scripting API (exposed to Python)
├── LegionScripting.cs         # Script runtime/execution manager
├── LScriptSettings.cs         # Script settings (auto-start, cache)
├── PersistentVars.cs          # Persistent variable storage
├── IPyGump.cs                 # Gump interface for scripting
├── Utility.cs                 # Scripting utility helpers
├── ScriptFile.cs              # Script file model
├── ScriptBrowser.cs           # Script file browser
├── ScriptRecorder.cs          # Script action recorder
├── ScriptRecordingGump.cs     # Recording UI
├── ScriptingInfoGump.cs       # Script info display
├── PyClasses/                 # 35+ Python binding classes
│   ├── PyPlayer.cs            # Player access
│   ├── PyMobile.cs            # Mobile/NPC access
│   ├── PyItem.cs              # Item access
│   ├── PyGameObject.cs        # Game object base
│   ├── PyEntity.cs            # Entity base
│   ├── PyGumps.cs             # Gump creation/manipulation
│   ├── PyBaseGump.cs          # Base gump class for scripts
│   ├── PyBaseControl.cs       # Base control class
│   ├── PyEvents.cs            # Event system
│   ├── PyProfile.cs           # Profile/settings access
│   ├── PyLand.cs              # Land tile access
│   ├── PyStatic.cs            # Static object access
│   ├── PyMulti.cs             # Multi-tile object access
│   ├── PyButton.cs            # Button control
│   ├── PyCheckbox.cs          # Checkbox control
│   ├── PyLabel.cs             # Label control
│   ├── PyTextBox.cs           # Text box control
│   ├── PyScrollArea.cs        # Scroll area control
│   ├── PyMenuItem.cs          # Menu item
│   ├── PyGumpPic.cs           # Gump image
│   ├── PyAlphaBlendControl.cs # Alpha blend control
│   ├── PyNiceButton.cs        # Nice button
│   ├── PyNineSliceGump.cs     # Nine-slice gump
│   ├── PyRadioButton.cs       # Radio button
│   ├── PySimpleProgressBar.cs # Progress bar
│   ├── PyControlComboBox.cs   # Combo box
│   ├── PyResizableStaticPic.cs # Resizable static pic
│   ├── PyTiledGumpPic.cs      # Tiled gump pic
│   ├── PyTTFTextInputField.cs # TTF text input
│   ├── PyJournalEntry.cs      # Journal entry access
│   └── Buff.cs                # Buff data for scripting
└── docs/                      # Auto-generated API documentation
    ├── API.md
    ├── PyPlayer.md
    ├── PyMobile.md
    ├── PyItem.md
    ├── PyGameObject.md
    ├── PyBaseGump.md
    ├── Gumps.md
    ├── Events.md
    └── ...
```

### Runtime
- **Engine**: IronPython 3.4.2
- **Python Library**: `external/iplib/` (348+ standard library modules)
- **Auto-start**: Scripts can be configured to auto-run globally or per character
- **Module Caching**: Optional, controlled by `LScriptSettings.DisableModuleCache`

### ImGui Script Tools
```
Game/UI/ImGuiControls/Legion/
├── ScriptEditorWindow.cs          # Built-in script editor
├── ScriptManagerWindow.cs         # Script execution manager
├── RunningScriptsWindow.cs        # Running scripts monitor
├── ScriptConstantsEditorWindow.cs # Script constants editor
├── PersistentVarsWindow.cs        # Persistent variables viewer
```

---

## 15. Plugin System

### Plugin Architecture
- **File**: `src/ClassicUO.Client/Network/Plugin.cs` (~1,436 lines)
- **Host**: `src/ClassicUO.Client/PluginHost.cs`
- **Storage**: `{ExecutablePath}/Data/Plugins/`
- **Loading**: Via `Settings.GlobalSettings.Plugins` array

### Plugin Types
1. **Native DLL plugins** - C/C++ DLLs with unmanaged entry points
2. **Managed .NET assemblies** - .NET DLLs with `Assistant.Engine.Install()` entry point

### Plugin Callbacks (Host -> Plugin)
- `InitializeFn`, `LoadPluginFn`, `TickFn`, `ClosingFn`
- `FocusGainedFn`, `FocusLostFn`
- `ConnectedFn`, `DisconnectedFn`
- `HotkeyFn`, `MouseFn`, `CmdListFn`, `SdlEventFn`
- `UpdatePlayerPosFn`, `PacketInFn`, `PacketOutFn`

### Plugin API (Plugin -> Client)
- `PluginRecvFn`, `PluginSendFn` (packet filtering)
- `PacketLengthFn`, `CastSpellFn`
- `SetWindowTitleFn`, `GetClilocFn`
- `RequestMoveFn`, `GetPlayerPositionFn`

---

## 16. Manager Classes Reference

All managers are in `src/ClassicUO.Client/Game/Managers/`. Here are the 70+ manager classes:

### World Managers (owned by World instance)
| Manager | Purpose |
|---------|---------|
| `WorldMapEntityManager` | World map entity tracking |
| `CorpseManager` | Corpse management |
| `PartyManager` | Party/group system |
| `HouseManager` | House data management |
| `WorldTextManager` | Floating world text |
| `EffectManager` | Visual effects |
| `MessageManager` | Message/chat handling |
| `ContainerManager` | Container state |
| `IgnoreManager` | Player ignore list |
| `SkillsGroupManager` | Skill grouping |
| `ChatManager` | Chat channels |
| `AuraManager` | Aura effects |
| `TargetManager` | Target cursor system |
| `DelayedObjectClickManager` | Delayed click handling |
| `BoatMovingManager` | Boat movement |
| `NameOverHeadManager` | Name overhead display |
| `MacroManager` | Macro system (112K bytes) |
| `CommandManager` | Client commands |
| `Weather` | Weather effects |
| `InfoBarManager` | Info bar display |
| `DurabilityManager` | Equipment durability |
| `ObjectPropertiesListManager` | Item property tooltips |
| `CoolDownBarManager` | Cooldown tracking |
| `ActiveSpellIconsManager` | Active spell display |

### Standalone Managers
| Manager | Purpose |
|---------|---------|
| `UIManager` | Central UI controller |
| `AudioManager` | Sound/music playback |
| `HotkeysManager` | Hotkey binding (24K bytes) |
| `AutoLootManager` | Automatic item looting |
| `BuySellAgent` | Buy/sell automation |
| `BandageManager` | Bandage healing agent |
| `DressAgentManager` | Equipment sets |
| `OrganizerAgent` | Item organization |
| `DiscordManager` | Discord rich presence |
| `FriendliesSQLManager` | Friends database |
| `FriendsListManager` | Friends list |
| `HealthLinesManager` | Health bar rendering |
| `HideHudManager` | HUD visibility toggle |
| `ItemDatabaseManager` | Item property database |
| `JournalManager` | Journal entries |
| `JournalFilterManager` | Journal filtering |
| `LastCharacterManager` | Last used character |
| `LastEquipmentManager` | Last equipment state |
| `MapWebServer` | Map web server |
| `MapWebServerManager` | Map server management |
| `AnchorManager` | Gump anchoring |
| `AnimatedStaticsManager` | Animated static objects |
| `EventSink` | Central event system |
| `GridContainerSaveData` | Grid container persistence |
| `GraphicsReplacement` | Graphics override system |
| `HouseCustomizationManager` | House decoration |
| `ProfileManager` | Profile management |
| `SeasonManager` | Seasonal effects |
| `SoundFilterManager` | Sound filtering |
| `SpellBarManager` | Spell bar configuration |
| `SQLSettingsManager` | SQL settings persistence |
| `TextHistoryManager` | Text input history |
| `TextRenderer` | Text rendering utilities |
| `TileMarkerManager` | Map tile markers |
| `TitleBarStatsManager` | Title bar information |
| `ToolTipOverrideManager` | Custom tooltip text |
| `WalkableManager` | Walkable tile calculation |
| `WalkerManager` | Movement/walking system |
| `ForcedTooltipManager` | Forced tooltip display |
| `GlobalActionCooldown` | Action cooldown tracking |
| `GlobalPriorityQueue` | Priority queue for actions |
| `MainThreadQueue` | Main thread dispatch |
| `MoveItemQueue` | Item movement queue |
| `UseItemQueue` | Item use queue |
| `SimpleAccountManager` | Account management |

---

## 17. Game Data & Constants

### Constants (`Game/Constants.cs`)
Key values:
- FPS: MIN_FPS = 12, MAX_FPS = 1000
- Circle of Transparency: 50-1000 radius
- Hue constants: OUT_RANGE_COLOR = 0x038B, DEAD_RANGE_COLOR = 0x038E
- SQL setting key strings

### Data Enums & Structures (`Game/Data/`)
| File | Purpose |
|------|---------|
| `Ability.cs` | Combat ability definitions |
| `BuffIcon.cs` | Buff icon IDs |
| `BuffTable.cs` | Buff/debuff effect table |
| `ChairTable.cs` | Sittable furniture data |
| `CharacterCreationValues.cs` | Character creation defaults |
| `CharacterSpeedType.cs` | Movement speed types |
| `ClientFeatures.cs` | Client capability flags |
| `ClientProtocol.cs` | Protocol version info |
| `ContainerData.cs` | Container type data |
| `CustomHouse.cs` | House customization data |
| `Direction.cs` | 8-direction enum |
| `EntityFlags.cs` | Entity state flags |
| `GraphicEffectBlendMode.cs` | Effect blend modes |
| `GraphicEffectType.cs` | Effect types |
| `HideHudFlags.cs` | HUD element visibility |
| `ItemInfo.cs` | Item information |
| `Layers.cs` | Equipment layer definitions |
| `LayerOrder.cs` | Layer rendering order |
| `LightColors.cs` | Light color definitions |
| `LightShaderData.cs` | Light shader parameters |
| `LockedFeatures.cs` | Feature lock flags |
| `MapMessageType.cs` | Map message types |
| `MessageType.cs` | Chat message types (Regular, System, Emote, Whisper, Yell, Spell, Guild, Alliance, Party, etc.) |
| `ModernUIConstants.cs` | Modern UI size/color constants |
| `Mounts.cs` | Mountable creature data |
| `MovementSpeed.cs` | Movement speed calculations |
| `NotorietyFlag.cs` | PvP notoriety (Innocent, Criminal, Enemy, etc.) |
| `PopupMenuData.cs` | Context menu data |
| `PromptData.cs` | Text prompt data |
| `RaceType.cs` | Race enum (HUMAN=1, ELF=2, GARGOYLE=3) |
| `Reagents.cs` | Spell reagent types |
| `ServerErrorMessages.cs` | Server error codes |
| `Sextant.cs` | Navigation sextant data |
| `Skill.cs` | Skill definition |
| `SpellbookTypes.cs` | Spellbook type enum |
| `StaticFilters.cs` | Static object filters |
| `TextType.cs` | Text rendering types |
| `Waypoints.cs` | Waypoint data |

### Spell Definitions
| File | School | Spell Count |
|------|--------|-------------|
| `SpellsMagery.cs` | Magery | 64 |
| `SpellsNecromancy.cs` | Necromancy | 17 |
| `SpellsChivalry.cs` | Chivalry | 10 |
| `SpellsBushido.cs` | Bushido | 6 |
| `SpellsNinjitsu.cs` | Ninjitsu | 30 |
| `SpellsMysticism.cs` | Mysticism | 8 |
| `SpellsSpellweaving.cs` | Spellweaving | 16 |
| `SpellsMastery.cs` | Mastery | varies |

Each spell has: Name, ID, GumpIconID, PowerWords, TargetType, ManaCost, MinSkill, Reagents.

---

## 18. ImGui Integration

### ImGui Window System
```
src/ClassicUO.Client/Game/UI/ImGuiControls/
├── ImGuiWindow.cs                     # Base ImGui window class
├── ImGUIComponents.cs                 # Shared ImGui components
├── ImGuiThemeEditorWindow.cs          # Theme editor
├── AssistantWindow.cs                 # Main assistant panel
├── TestWindow.cs                      # Debug test window
├── Legion/                            # Script-related windows
│   ├── ScriptEditorWindow.cs          # Built-in script editor
│   ├── ScriptManagerWindow.cs         # Script execution manager
│   ├── RunningScriptsWindow.cs        # Running scripts monitor
│   ├── ScriptConstantsEditorWindow.cs # Constants editor
│   └── PersistentVarsWindow.cs        # Persistent variables viewer
└── Agents/                            # Agent configuration tabs
    ├── TabContent.cs                  # Base tab content class
    ├── GeneralTabContent.cs           # General settings
    ├── AutoLootTabContent.cs          # Auto-loot configuration
    ├── AutoBuyTabContent.cs           # Auto-buy configuration
    ├── AutoSellTabContent.cs          # Auto-sell configuration
    ├── BandageAgentTabContent.cs      # Bandage agent config
    ├── DressAgentTabContent.cs        # Dress agent config
    ├── FriendsListTabContent.cs       # Friends list
    ├── FiltersTabContent.cs           # Filter settings
    ├── GraphicReplacementTabContent.cs # Graphic overrides
    ├── HudTabContent.cs               # HUD configuration
    ├── ItemDatabaseTabContent.cs      # Item database browser
    ├── ItemDetailWindow.cs            # Item detail popup
    ├── JournalFilterTabContent.cs     # Journal filter config
    ├── MacrosTabContent.cs            # Macros configuration
    ├── OrganizerTabContent.cs         # Organizer agent
    ├── SeasonFilterTabContent.cs      # Season filter settings
    ├── SoundFilterTabContent.cs       # Sound filter settings
    ├── SpellBarTabContent.cs          # Spell bar config
    ├── SpellIndicatorTabContent.cs    # Spell indicator config
    └── TitleBarTabContent.cs          # Title bar config
```

Uses **ImGui.NET 1.91.6.1** for immediate-mode GUI panels.

---

## 19. Input System

### Files
```
src/ClassicUO.Client/Input/
├── (Mouse, Keyboard, Controller input handling)
```

### Input Flow
1. **SDL3 Events** - Raw input from SDL3 library
2. **Scene Input Handlers** - Scene-specific input processing
3. **UI Input** - UIManager routes input to controls
4. **Hotkey System** - HotkeysManager processes keybindings
5. **Macro System** - MacroManager executes macro sequences
6. **Controller** - Gamepad button mapping via SDL

### Scene Input Methods (from `Scene.cs`)
```csharp
OnMouseUp(MouseButtonType), OnMouseDown(MouseButtonType)
OnMouseDoubleClick(MouseButtonType), OnMouseWheel(bool up)
OnMouseDragging()
OnControllerButtonDown(SDL_GamepadButtonEvent)
OnControllerButtonUp(SDL_GamepadButtonEvent)
OnTextInput(string), OnKeyDown(SDL_KeyboardEvent), OnKeyUp(SDL_KeyboardEvent)
```

---

## 20. Audio System

### AudioManager (`Game/Managers/AudioManager.cs`)
- FNA SoundEffect API for sound playback
- Background music via UOMusic
- Volume control per category (sound, music)
- Sound filtering via SoundFilterManager
- Window focus handling (mute when unfocused, configurable)

### Audio I/O (`ClassicUO.IO/Audio/`)
| File | Purpose |
|------|---------|
| `Sound.cs` | Sound data handling |
| `UOSound.cs` | UO sound file format |
| `UOMusic.cs` | UO music file format |

### MP3 Support
- **MP3Sharp** submodule for MP3 decoding
- Used for custom music playback

---

## 21. Map System

### Map Classes
```
src/ClassicUO.Client/Game/Map/
├── Map.cs        # Map data structure and management
└── Chunk.cs      # Map chunk (8x8 tile block)
```

### Map Loading (`ClassicUO.Assets/MapLoader.cs`)
- Loads `map*.mul` and `statics*.mul` files
- Supports multiple map facets (Felucca, Trammel, Ilshenar, Malas, Tokuno, TerMur)
- Lazy loading by chunk

### World Map Features
- `WorldMapGump.cs` - Full world map display
- `MiniMapGump.cs` - Mini-map overlay
- `MapWebServer.cs` / `MapWebServerManager.cs` - Web-based map server
- `TileMarkerManager.cs` - Custom tile markers
- `UserMarkerGump.cs` - User-placed map markers

---

## 22. Data Storage & Persistence

### Storage Formats Summary
| Data | Format | File | Location |
|------|--------|------|----------|
| Global Settings | JSON | `settings.json` | Executable directory |
| Character Profile | JSON | `profile.json` | `Profiles/{User}/{Server}/{Char}/` |
| Default Profile | JSON | `default.json` | `Profiles/` |
| Gump Layouts | XML | `gumps.xml` | Profile directory |
| Grid Containers | JSON | `grid_containers.json` | Profile directory |
| SQL Settings | SQLite | `settings.db` | `Data/` |
| Spell Indicators | JSON | `DefaultSpellIndicatorConfig.json` | Embedded/Managers/ |
| Scripting API Docs | Markdown | `*.md` | `LegionScripting/docs/` |

### Grid Container Data (`Game/Managers/GridContainerSaveData.cs`)
- `GridContainerEntry` - Individual container entry
- 3 rotating backup files
- 120-day inactive entry cleanup

### Grid Highlight (`Game/UI/Gumps/GridHighLight/`)
- `GridHighLightProfile.cs` - Highlight profile structure
- `GridHighLightData.cs` - Highlight configuration data
- `GridHighLightProperties.cs` - Property matching rules
- `GridHighLightRules.cs` - Matching rule engine
- `GridHighLightConfig.cs` - Configuration management
- `GridHightlightMenu.cs` - Configuration UI

### Tooltip Overrides (`Game/Managers/ToolTipOverrideManager.cs`)
- Customizable tooltip text replacement
- Per-profile configuration
- UI: `TooltipConfigGump.cs`

---

## 23. External Dependencies

### Git Submodules
| Name | URL | Path | Purpose |
|------|-----|------|---------|
| FNA | https://github.com/FNA-XNA/FNA.git | `external/FNA` | XNA framework reimplementation |
| MP3Sharp | https://github.com/andreakarasho/MP3Sharp.git | `external/MP3Sharp` | MP3 audio decoder |
| FileEmbed | https://github.com/SirCxyrtyx/FileEmbed.git | `external/FileEmbed` | Compile-time file embedding |

### NuGet Packages
| Package | Version | Project | Purpose |
|---------|---------|---------|---------|
| IronPython | 3.4.2 | Client | Python scripting runtime |
| ImGui.NET | 1.91.6.1 | Client | Immediate-mode GUI |
| Microsoft.Data.Sqlite | 8.0.0 | Client | SQLite database |
| System.Text.Json | 8.0.5 | All | JSON serialization |
| SixLabors.ImageSharp | 3.1.11 | Utility | Image processing |
| xunit | 2.9.0 | Tests | Test framework |
| FluentAssertions | 6.12.0 | Tests | Test assertions |

### Local External Libraries
| Name | Path | Purpose |
|------|------|---------|
| FontStashSharp | `external/FontStashSharp` | Font rendering |
| DiscordSocialSDK.Wrapper | `external/DiscordSocialSDK.Wrapper` | Discord integration |
| cuoapi | `external/cuoapi` | ClassicUO API binary |
| iplib | `external/iplib` | Python standard library |

### Native Platform Libraries
| Platform | Path | Contents |
|----------|------|----------|
| Windows x64 | `external/x64/` | SDL, FNA3D, FAudio DLLs |
| Windows ARM | `external/win-arm/` | ARM native binaries |
| Linux x64 | `external/lib64/` | .so shared libraries |
| macOS Intel | `external/osx/` | .dylib libraries |
| macOS ARM64 | `external/osx-arm/` | ARM64 .dylib libraries |
| Vulkan | `external/vulkan/icd.d/` | Vulkan ICD definitions |

---

## 24. Command-Line Arguments

**File**: `src/ClassicUO.Client/Main.cs` (`ReadSettingsFromArgs()`)

### Account
| Argument | Type | Description |
|----------|------|-------------|
| `-username <value>` | string | Account username |
| `-password <value>` | string | Password (encrypted) |
| `-password_enc <value>` | string | Pre-encrypted password |
| `-saveaccount <bool>` | bool | Save account info |
| `-autologin <bool>` | bool | Auto-login |
| `-lastcharname <name>` | string | Last character name |
| `-lastservernum <num>` | int | Last server number |
| `-last_server_name <name>` | string | Last server name |

### Server
| Argument | Type | Description |
|----------|------|-------------|
| `-ip <address>` | string | Server IP |
| `-port <number>` | int | Server port |
| `-clientversion <version>` | string | UO client version (e.g., "7.0.59.1") |
| `-encryption <byte>` | byte | Encryption type |

### Paths
| Argument | Type | Description |
|----------|------|-------------|
| `-settings <filepath>` | string | Custom settings.json path |
| `-ultimaonlinedirectory <path>` | string | UO files directory |
| `-uopath <path>` | string | Alias for above |
| `-profilespath <path>` | string | Profiles directory |
| `-filesoverride <path>` | string | File override directory |
| `-maps_layouts <path>` | string | Custom map layouts |

### Graphics
| Argument | Type | Description |
|----------|------|-------------|
| `-highdpi` | flag | Enable high DPI |
| `-fps <number>` | int | FPS cap (10-244) |
| `-force_driver <1\|2>` | int | 1=OpenGL, 2=Vulkan |
| `-fixed_time_step <bool>` | bool | Fixed timestep |

### Scripting/Plugins
| Argument | Type | Description |
|----------|------|-------------|
| `-plugins <csv>` | string | Comma-separated plugin names |
| `-language <code>` | string | Language code (ENU, RUS, FRA, etc.) |

### Network
| Argument | Type | Description |
|----------|------|-------------|
| `-reconnect <bool>` | bool | Auto-reconnect |
| `-reconnect_time <ms>` | int | Reconnect timeout (min 1000ms) |
| `-no_server_ping` | flag | Disable server ping |
| `-zlib` | flag | Force managed zlib |
| `-packetlog [csv]` | string | Log packet IDs |

### Debug
| Argument | Type | Description |
|----------|------|-------------|
| `-skiploginscreen` | flag | Skip login screen |
| `-debug` | flag | Enable debug mode |
| `-profiler` | flag | Enable profiler |

---

## 25. Key Namespaces

### Core
| Namespace | Location | Purpose |
|-----------|----------|---------|
| `ClassicUO` | Client root | Root namespace, Bootstrap, Client |
| `ClassicUO.Configuration` | Client/Configuration | Settings, Profile, ProfileManager |
| `ClassicUO.Configuration.Json` | Client/Configuration/Json | JSON serialization contexts |

### Game Logic
| Namespace | Location | Purpose |
|-----------|----------|---------|
| `ClassicUO.Game` | Client/Game | World, Constants, GameActions |
| `ClassicUO.Game.Data` | Client/Game/Data | Enums, data structures, spell definitions |
| `ClassicUO.Game.GameObjects` | Client/Game/GameObjects | Item, Mobile, PlayerMobile, etc. |
| `ClassicUO.Game.Managers` | Client/Game/Managers | All manager classes |
| `ClassicUO.Game.Map` | Client/Game/Map | Map, Chunk |
| `ClassicUO.Game.Scenes` | Client/Game/Scenes | Scene, LoginScene, GameScene, MainScene |

### UI
| Namespace | Location | Purpose |
|-----------|----------|---------|
| `ClassicUO.Game.UI` | Client/Game/UI | UIManager |
| `ClassicUO.Game.UI.Controls` | Client/Game/UI/Controls | All UI controls |
| `ClassicUO.Game.UI.Gumps` | Client/Game/UI/Gumps | All gump windows |
| `ClassicUO.Game.UI.ImGuiControls` | Client/Game/UI/ImGuiControls | ImGui panels |

### Infrastructure
| Namespace | Location | Purpose |
|-----------|----------|---------|
| `ClassicUO.Input` | Client/Input | Input handling |
| `ClassicUO.Network` | Client/Network | Networking, packets |
| `ClassicUO.Network.Encryption` | Client/Network/Encryption | Crypto implementations |
| `ClassicUO.Network.EnhancedPackets` | Client/Network/EnhancedPackets | Enhanced protocol |
| `ClassicUO.LegionScripting` | Client/LegionScripting | Scripting system |
| `ClassicUO.LegionScripting.PyClasses` | Client/LegionScripting/PyClasses | Python bindings |
| `ClassicUO.Resources` | Client/Resources | Localization strings |

### Libraries
| Namespace | Location | Purpose |
|-----------|----------|---------|
| `ClassicUO.Assets` | Assets project | UO file loaders |
| `ClassicUO.Renderer` | Renderer project | Graphics rendering |
| `ClassicUO.Renderer.Animations` | Renderer/Animations | Animation sprites |
| `ClassicUO.Renderer.Arts` | Renderer/Arts | Art textures |
| `ClassicUO.Renderer.Batching` | Renderer/Batching | Draw batching |
| `ClassicUO.IO` | IO project | File I/O |
| `ClassicUO.IO.Audio` | IO/Audio | Audio file I/O |
| `ClassicUO.Utility` | Utility project | Helpers |
| `ClassicUO.Utility.Collections` | Utility/Collections | Deque, FastList, etc. |
| `ClassicUO.Utility.Logging` | Utility/Logging | Log, Logger, LogFile |
| `ClassicUO.Utility.Platforms` | Utility/Platforms | Platform-specific code |

---

## 26. File Index by Category

### Game Objects (21 files)
`src/ClassicUO.Client/Game/GameObjects/`: DragEffect, Entity, EntityCollection, EntityTextContainer, FixedEffect, GameEffect, GameObject, House, IsometricLight, Item, Land, LightningEffect, LineOfSightHelper, Mobile, MobileAnimation, MovingEffect, Multi, PlayerMobile, RenderedText, Static, TextObject

### Gumps (95+ files)
`src/ClassicUO.Client/Game/UI/Gumps/`: AnchorableGump, AnimBrowser, ArtBrowserGump, BaseOptionsGump, BoatControl, BuffGump, BulletinBoardGump, ChatGump, ChatGumpChooseName, ColorPickerGump, CombatBookGump, CommandsGump, ContainerGump, CoolDownBar, CounterBarGump, CreditsGump, CustomToolTip, DebugGump, DressAgentConfigGump, DurabilityGump, FileSelector, GridContainer, GridLootGump, Gump, GumpType, HealthbarCollectorGump, HealthBarGump, HouseCustomizationGump, IgnoreManagerGump, ImprovedBuffGump, InfoBarGump, InputRequest, InspectorGump, JournalGump, LocationGoGump, MacroButtonEditorGump, MacroButtonGump, MacroGump, MapGump, MarkersManagerGump, MenuGump, MenuGumpItemViewMetadata, MessageBoxGump, MiniMapGump, ModernBookGump, ModernColorPicker, ModernOptionsGump, ModernPaperdoll, ModernShopGump, MultiItemMoveGump, MultipleToolTipGump, NameOverheadGump, NameOverHeadHandlerGump, NearbyItems, NetworkStatsGump, NineSliceGump, PaperdollGump, PartyGump, PartyInviteGump, PopupMenuGump, ProfileGump, ProgressBarGump, QuestArrowGump, QuestionGump, RaceChangeGump, RacialAbilitiesBookGump, RacialAbilityButton, ResizableGump, ResizableJournal, RGBColorPickerGump, ScalableGump, ScalableTextContainerGump, SelectableItemListGump, ShopGump, SimpleTimedTextGump, SkillButtonGump, SkillGumpAdvanced, SkillProgressBar, SpellbookGump, SplitMenuGump, StandardSkillsGump, StatusGump, Supporters, SystemChatControl, TextContainerGump, TextEntryDialogGump, TipNoticeGump, TooltipConfigGump, TopBarGump, TradingGump, UpdateTimerViewer, UseAbilityButtonGump, UserMarkerGump, UseSpellButtonGump, VersionHistory, WorldMapGump, WorldViewportGump

### UI Controls (60+ files)
`src/ClassicUO.Client/Game/UI/Controls/`: AlphaBlendControl, AnimationDisplay, Area, ArrowNumbersTextBox, Button, ButtonTileArt, Checkbox, CheckerTrans, ClickableColorBox, ClickPriority, ColorBox, ColorPickerBox, ColorSelectorControl, Combobox, ContextMenuControl, Control, CroppedText, DataBox, ExpandableScroll, ExternalUrlImage, FadingLabel, GumpControlInfo, GumpPic, GumpPicTiled, GumpPicWithWidth, HBoxContainer, HitBox, HotkeyBox, HoveredLabel, HSliderBar, HtmlControl, HttpClickableLink, InfoBarBuilderControl, InputField, ItemGump, Label, Line, MacroControl, MenuButton, ModernScrollArea, ModernScrollBar, NameOverheadAssignControl, NiceButton, NineSliceButton, NineSliceControl, PaperDollInteractable, RadioButton, RenderedMapArea, ResizableStaticPic, ResizePic, ScissorControl, ScrollArea, ScrollBar, ScrollBarBase, ScrollFlag, SettingsSection, SimpleBorder, SimpleProgressBar, StaticPaperDollView, StaticPic, StbTextBox, TableContainer, TextBox, TTFTextInputField, VBoxContainer

### Managers (70+ files)
`src/ClassicUO.Client/Game/Managers/`: ActiveIconsManager, AnchorManager, AnimatedStaticsManager, AudioManager, AuraManager, AutoLootManager, AutoUnequipActionManager, BandageManager, BoatMovingManager, BuySellAgent, ChatChannel, ChatManager, ChatStatus, CommandManager, ContainerManager, CoolDownBarManager, CorpseManager, DelayedObjectClickManager, DiscordManager, DressAgentManager, DurabilityManager, EffectManager, EventSink, ForcedTooltipManager, FriendliesSQLManager, FriendsListManager, GlobalActionCooldown, GlobalPriorityQueue, GraphicsReplacement, GridContainerSaveData, HealthLinesManager, HideHudManager, HotkeysManager, HouseCustomizationManager, HouseManager, IgnoreManager, InfoBarManager, ItemDatabaseManager, JournalFilterManager, JournalManager, LastCharacterManager, LastEquipmentManager, MacroManager, MainThreadQueue, MapWebServer, MapWebServerManager, MessageEventArgs, MessageManager, MoveItemQueue, NameOverHeadManager, NextGumpConfig, ObjectPropertiesListManager, OrganizerAgent, PartyManager, Season, SeasonFilter, SeasonManager, SimpleAccountManager, SkillsGroupManager, SoundFilterManager, SpellBarManager, SQLSettingsManager, Stitchin, TargetManager, TextHistoryManager, TextRenderer, TileMarkerManager, TitleBarStatsManager, ToolTipOverrideManager, UIManager, UseItemQueue, WalkableManager, WalkerManager, WorldMapEntityManager, WorldTextManager

### Game Data (47 files)
`src/ClassicUO.Client/Game/Data/`: Ability, BuffIcon, BuffTable, ChairTable, CharacterCreationValues, CharacterSpeedType, ClientFeatures, ClientProtocol, ContainerData, CustomHouse, Direction, EntityFlags, GraphicEffectBlendMode, GraphicEffectType, HideHudFlags, ItemInfo, LayerOrder, Layers, LightColors, LightShaderData, LockedFeatures, MapMessageType, MessageType, ModernUIConstants, Mounts, MovementSpeed, NotorietyFlag, PopupMenuData, PopupMenuItem, PromptData, RaceType, Reagents, ServerErrorMessages, Sextant, Skill, SpellbookTypes, SpellDefinition, SpellsBushido, SpellsChivalry, SpellsMagery, SpellsMastery, SpellsMysticism, SpellsNecromancy, SpellsNinjitsu, SpellsSpellweaving, StaticFilters, TextType, Waypoints

### Network (23 files)
`src/ClassicUO.Client/Network/`: AsyncNetClient, BlowfishBehaviour, CircularBuffer, Encryption, EnhancedOutgoingPackets, EnhancedPacketHandler, EnhancedPacketTypeEnum, Extensions, Huffman, LoginCryptBehaviour, LoginHandshake, MD5Behaviour, NetStatistics, OutgoingPackets, PacketHandlers, PacketLogger, PacketsTable, Plugin, ServerListEntry, SocketWrapper, TcpSocketWrapper, TwofishBehaviour, WebSocketWrapper

### LegionScripting (41 files)
`src/ClassicUO.Client/LegionScripting/`: API, Buff, IPyGump, LegionScripting, LScriptSettings, PersistentVars, PyAlphaBlendControl, PyBaseControl, PyBaseGump, PyButton, PyCheckbox, PyControlComboBox, PyEntity, PyEvents, PyGameObject, PyGumpPic, PyGumps, PyItem, PyJournalEntry, PyLabel, PyLand, PyMenuItem, PyMobile, PyMulti, PyNiceButton, PyNineSliceGump, PyPlayer, PyProfile, PyRadioButton, PyResizableStaticPic, PyScrollArea, PySimpleProgressBar, PyStatic, PyTextBox, PyTiledGumpPic, PyTTFTextInputField, ScriptBrowser, ScriptFile, ScriptingInfoGump, ScriptRecorder, ScriptRecordingGump, Utility

### Asset Loaders (24 files)
`src/ClassicUO.Assets/`: AnimationsLoader, AnimDataLoader, ArtLoader, ClilocLoader, FontsLoader, GumpsLoader, HuesLoader, LightsLoader, MapLoader, MultiLoader, MultiMapLoader, PNGLoader, ProfessionLoader, SkillsLoader, SoundOverrideLoader, SoundsLoader, SpeechesLoader, StringDictionary, TexmapsLoader, TileArt, TileDataLoader, TrueTypeLoader, UOFileLoader, UOFileManager, VerdataLoader

### Renderer (40+ files)
`src/ClassicUO.Renderer/`: Animation, AnimationDirection, AnimationGroup, Art, BasicUOEffect, BatchCommand, Batcher2D, BlendFactorCommand, Camera, various Create*Command, DestroyResourceCommand, Fonts, Gump, Light, MultiMap, PixelPicker, Primitives2D, ScissorCommand, ScissorStack, Set*Command, ShaderHueTranslator, SolidColorTextureCache, Sound, SpriteFont, SpriteInfo, Texmap, TextureAtlas, ViewportCommand, XBREffect

### I/O (12 files)
`src/ClassicUO.IO/`: DefReader, FileReader, MMFileReader, Sound, StackDataReader, StackDataWriter, UOFile, UOFileIndex, UOFileMul, UOFilesOverrideMap, UOFileUop, UOMusic, UOSound

### Utility (50+ files)
`src/ClassicUO.Utility/`: Adler32, AverageOverTime, Bag, BwtDecompress, ByteFlagHelper, ClientVersion, Clipboard, CollectionHelper, ColorQuantizer, CRuntime, Crypter, Deque, Easings, Enums, Extensions, FastList, FileSystemHelper, FindState, HuesHelper, IEnumeratorExtensions, ITextEditHandler, JsonHelper, Log, LogFile, Logger, LogTypes, MapChecksumCalculator, MathHelper, Native, ObjectPool, OrderedDictionary, Packer, PlatformHelper, Profiler, QueuedPool, RandomHelper, ReadOnlyArrayView, RegexHelper, StbRectPack, StringHelper, TextEdit, TextEditRow, TextFileParser, UInt16Converter, UndoRecord, UndoState, UnsafeMemoryManager, ValueStringBuilder, ZLib, ZLibHeader, ZLibManaged, ZLIBStream

---

## 27. Conventions & Guidelines

### Code Style
- Follow existing C# conventions
- Root namespace: `ClassicUO`
- JSON serialization: Must use source-generated `JsonSerializerContext`
- License: Do NOT put license headers on new files
- Unsafe code: Allowed (enabled in build props)
- C# Preview features: Available

### Architecture Patterns
- **Singleton**: World.Instance, ProfileManager (static)
- **Manager Pattern**: Subsystems managed via dedicated manager classes
- **Scene Pattern**: Login -> Game scene lifecycle
- **Observer/Event**: EventSink for centralized events
- **Command Pattern**: Batch rendering commands
- **Partial Classes**: GameScene split across 3 files (main, drawing, input)

### Key Design Decisions
- All entities identified by uint serial numbers
- Packet-based networking with UO protocol
- FNA for cross-platform graphics
- IronPython for scripting
- ImGui for advanced dev/config panels
- SQLite for persistent settings with backup rotation
- Stack-based data readers/writers for efficient I/O (no allocation)
