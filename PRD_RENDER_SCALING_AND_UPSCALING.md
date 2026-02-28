# PRD: Layered UI Scaling & Graphics Upscaling

## Problem Statement

TazUO's UI was designed for 800x600 era resolutions. On modern displays (1440p, 4K), legacy UO gumps are tiny and hard to read. The current "Global Scale" feature (`RenderScale`) addresses this by scaling the entire frame uniformly, but it has significant issues:

1. **Login screen gets scaled** and clips off-screen
2. **ImGui windows get double-scaled** (ImGui has its own DPI scaling)
3. **All text gets blurry** when scaled up
4. **Extra full-screen render target blit per frame** even at 1.0x scale
5. **Mouse coordinates need manual `/RenderScale` adjustment** everywhere
6. The game world already has its own Camera zoom system, making `RenderScale` redundant for world scaling

Additionally, the existing XBR pixel-art upscaling shader is underutilized and only applies to the game world viewport, not UI elements that need it most.

## Goals

1. **Readable UI at any resolution** without blurring text or double-scaling ImGui
2. **Better-looking game world** with upscaling filters (XBR, bilinear, or modern alternatives)
3. **No performance regression** — equal or better FPS than current implementation
4. **Clean separation** of scaling concerns: world, legacy gumps, ImGui, and text

## Non-Goals

- Complete UI redesign / vector-based gumps (too large in scope)
- Real-time AI super-resolution (too expensive for this engine)
- Shader model 4+ features (FNA targets SM3.0 for compatibility)

---

## Current Architecture

### Four Independent Scale Systems (Conflicting)

| System | Scope | Where Applied | Filter? |
|--------|-------|---------------|---------|
| Camera.Zoom | World viewport | GameScene RT sizing | YES (XBR/Linear/Aniso) |
| RenderScale | Everything | GameController final composite | Anisotropic only |
| ContainersScale | Container items | Per-item draw | NO (point sampled) |
| GridContainersScale | Grid layout | Layout math | NO (point sampled) |

### Render Pipeline (Current)

```
GameController.Draw()
├── [if _useScreenRenderTarget] SetRenderTarget(_screenRenderTarget)
├── Draw background
├── GameScene.Draw()
│   ├── Render world → _worldRenderTarget (with Camera.Zoom sizing)
│   ├── Render lights → _lightRenderTarget
│   ├── Composite world RT → backbuffer (WITH post-process filter)
│   ├── Composite lights (blend)
│   └── Draw overheads/selection (direct, no filter)
├── UIManager.Draw() ← ALL gumps, NO filter, NO scale transform
├── GameCursor.Draw()
├── ImGui.Render()
├── Plugin.Draw()
├── [if RT] SetRenderTarget(null)
└── [if RT] Composite _screenRenderTarget → backbuffer (with RenderScale)
```

**Key problem:** The outer `_screenRenderTarget` does a second full-screen blit that scales EVERYTHING uniformly. There's no way to filter world+gumps differently from text+ImGui.

### Key Files

| File | Lines | Purpose |
|------|-------|---------|
| `GameController.cs` | 535-641 | Outer draw loop, _screenRenderTarget |
| `GameScene.cs` | 1132-1230 | World draw, post-process application |
| `GameScene.cs` | 1550-1614 | Filter selection (Point/Linear/Aniso/XBR) |
| `UIManager.cs` | 465-477 | Gump rendering (no filter) |
| `Camera.cs` | 10-196 | Viewport zoom system |
| `xBR.fx` | 1-162 | XBR pixel-art upscaling shader |
| `IsometricWorld.fx` | 1-206 | Main world shader (14 hue modes) |
| `Batcher2D.cs` | 1423-1462 | ApplyStates (samplers, shaders) |
| `Profile.cs` | 673-674 | EnablePostProcessingEffects, PostProcessingType |
| `ModernOptionsGump.cs` | 1078-1098 | Post-process settings UI |
| `ModernOptionsGump.cs` | 4045-4059 | Global Scale slider |
| `GeneralTabContent.cs` | 221-229 | ImGui scale slider |
| `LoginScene.cs` | 50, 79, 150 | Scale event handler, window resize |
| `Mouse.cs` | 150-152 | RenderScale mouse adjustment |
| `ImGuiRenderer.cs` | 223 | RenderScale mouse adjustment for ImGui |

---

## Proposed Architecture

### Phase 1: Remove Outer Render Target, Add UI Scale (LOW RISK)

**Goal:** Eliminate the redundant `_screenRenderTarget` and replace `RenderScale` with a proper UI scale that only affects legacy gumps.

#### Render Pipeline (New)

```
GameController.Draw()
├── Draw background (direct to backbuffer)
├── GameScene.Draw()
│   ├── World → _worldRenderTarget (Camera.Zoom)
│   ├── Composite with post-process filter (XBR/Linear/Aniso/Point)
│   ├── Lights composite
│   └── Overheads/selection
├── UIManager.Draw() ← with UIScale transform matrix
│   ├── batcher.Begin(null, uiScaleMatrix)
│   ├── Draw all legacy gumps (scaled)
│   └── batcher.End()
├── GameCursor.Draw()
├── ImGui.Render() ← NO extra scale (has own DPI scaling)
└── Plugin.Draw() ← NO extra scale
```

#### Changes

**GameController.cs:**
- Remove `_screenRenderTarget` field and `EnsureScreenRenderTarget()`
- Remove the `_useScreenRenderTarget` flag
- Remove the `if (useRenderTarget)` / `else` branching in `Draw()`
- Rename `RenderScale` → `UIScale` (clarity)
- Remove RenderScale from screenshot methods (use backbuffer directly)

**UIManager.cs:**
- Accept a scale matrix in `Draw()`: `Draw(batcher, Matrix uiScaleMatrix)`
- Apply matrix: `batcher.Begin(null, uiScaleMatrix)` instead of bare `batcher.Begin()`
- Scale matrix: `Matrix.CreateScale(UIScale, UIScale, 1f)`

**Mouse.cs:**
- Remove `/ Client.Game.RenderScale` from position calculation (lines 150-152)
- Add inverse scale only for gump hit-testing: `UIManager.ScreenToUI(position)` helper

**ImGuiRenderer.cs:**
- Remove `/ Client.Game.RenderScale` from mouse position (line 223)

**LoginScene.cs:**
- Remove `ScaleChanged` event subscription (lines 50, 79, 552)
- Remove `* Client.Game.RenderScale` from `UpdateWindowSize()` (line 150)
- Login screen always renders at its native 640x480 in the window

**GameCursor.cs:**
- Remove `Client.Game.RenderScale` references (lines 387, 429)

**Settings Migration:**
- Read old `GAME_SCALE` setting, migrate to new `UI_SCALE`
- Default: 1.0 (100%)
- Range: 1.0 - 2.5 (100% - 250%) — only upscaling, no downscaling

**Settings UI (GeneralTabContent.cs / ModernOptionsGump.cs):**
- Rename "Global Scale" → "UI Scale"
- Update tooltip: "Scales legacy UO interface elements. Does not affect the game world (use camera zoom) or modern UI windows."

#### Hit Testing

When `UIScale != 1.0`, mouse coordinates for gump interaction need inverse-scaling:

```csharp
// UIManager helper
public static Point ScreenToUI(Point screenPos)
{
    float inv = 1f / Client.Game.UIScale;
    return new Point((int)(screenPos.X * inv), (int)(screenPos.Y * inv));
}
```

This replaces the current scattered `/ RenderScale` adjustments with a single centralized conversion.

---

### Phase 2: Filtered UI Upscaling (MEDIUM RISK)

**Goal:** When UIScale > 1.0, apply smooth filtering to gump graphics while keeping text crisp.

#### Approach: UI Render Target with Selective Filtering

```
UIManager.Draw()
├── SetRenderTarget(_uiRenderTarget)  // native resolution
├── batcher.Begin()
├── Draw all gumps at 1:1
├── batcher.End()
├── SetRenderTarget(null)             // back to backbuffer
├── batcher.Begin(null, sampler: LinearClamp)  // smooth upscale
└── Draw _uiRenderTarget scaled to UIScale
```

**Why this works:**
- Gumps render at native res (pixel-perfect internally)
- The upscale uses bilinear filtering → smooth edges on gump art
- Single extra RT blit (same cost as old `_screenRenderTarget`)
- Text rendered by gumps gets same filtering (acceptable since it's bitmap text)

**Optional enhancement:** TTF text (FontStashSharp) could render at scaled resolution directly, bypassing the RT. This would give crisp text + smooth gump art.

#### Changes

**UIManager.cs:**
- Add `_uiRenderTarget` (sized to backbuffer / UIScale)
- Render gumps to `_uiRenderTarget` at native resolution
- Composite to backbuffer with `SamplerState.LinearClamp` and scale matrix

**Profile.cs:**
- Add `UIFilteringMode`: Point (pixelated), Linear (smooth), Anisotropic (high quality)
- Default: Linear

---

### Phase 3: World Upscaling Improvements (LOW-MEDIUM RISK)

**Goal:** Better-looking game world with improved or additional upscaling options.

#### 3a: Expose XBR Quality Parameter

The existing XBR shader has a hardcoded `coef = 2.0` that controls edge detection sensitivity. Expose it as a user setting.

**xBR.fx:**
```hlsl
// Change from:
const static float coef = 2.0;
// To:
float coef;  // Set from C# code
```

**XBREffect.cs:**
- Add `Coef` parameter binding
- Set from profile setting

**Profile.cs:**
- Add `XbrSharpness` (float, 1.0 - 4.0, default 2.0)
- Lower = sharper/more pixelated, Higher = smoother

#### 3b: Add FSR 1.0 (FidelityFX Super Resolution) Shader

AMD's FSR 1.0 is a spatial upscaler that works as a single-pass shader. It's open source (MIT license), runs on any GPU, and is designed for exactly this use case.

**What it does:**
- EASU pass: Edge-Adaptive Spatial Upsampling (detects edges, applies directional sharpening)
- RCAS pass: Robust Contrast-Adaptive Sharpening (optional detail enhancement)

**Why FSR 1.0 specifically:**
- Single shader file, no temporal data needed (no motion vectors)
- Works with FNA's shader model 3.0 (with minor adaptations)
- ~0.5ms at 1080p on modern GPUs
- Significantly better quality than bilinear, comparable to XBR for pixel art
- Well-documented, MIT licensed

**Implementation:**
1. Port FSR 1.0 EASU+RCAS to HLSL SM3.0
2. Create `FSREffect.cs` wrapper (similar to `XBREffect.cs`)
3. Add as `PostProcessingType.FSR` option
4. Apply at same location as XBR (GameScene world composite)

**Files to create:**
- `src/ClassicUO.Renderer/shaders/fsr.fx` — HLSL shader
- `src/ClassicUO.Renderer/Effects/FSREffect.cs` — C# wrapper

**Files to modify:**
- `GameScene.cs` — Add FSR case to `UpdatePostProcessState()`
- `Profile.cs` — Add FSR to PostProcessingType enum
- Settings UI — Add FSR option to dropdown

#### 3c: Hybrid Filtering Mode

Combine approaches: XBR or FSR for the game world, Linear for UI, Point for text overlays.

**This is enabled by Phase 1's architecture** — once world and UI rendering are separated, each can use different filters independently. No additional render targets needed beyond Phase 2's `_uiRenderTarget`.

---

## Performance Analysis

### Current Cost (Phase 0 — today)

| Operation | Cost |
|-----------|------|
| World RT composite | 1 draw call, ~0.1ms |
| Screen RT composite (RenderScale) | 1 draw call, ~0.2ms (full-screen blit) |
| XBR shader (if enabled) | 1 draw call, ~1-2ms |
| **Total extra** | **~0.3-2.3ms/frame** |

### Phase 1 Cost (Remove outer RT)

| Operation | Cost |
|-----------|------|
| World RT composite | 1 draw call, ~0.1ms |
| Screen RT composite | **REMOVED** — saves ~0.2ms |
| UI scale matrix | Free (GPU transform) |
| **Total extra** | **~0.1ms/frame** (net savings) |

### Phase 2 Cost (UI render target)

| Operation | Cost |
|-----------|------|
| World RT composite | 1 draw call, ~0.1ms |
| UI RT composite | 1 draw call, ~0.1ms |
| **Total extra** | **~0.2ms/frame** (same as current) |

### Phase 3 Cost (FSR)

| Operation | Cost |
|-----------|------|
| FSR 1.0 EASU+RCAS | 1-2 draw calls, ~0.5-1ms |
| vs XBR (current) | 1 draw call, ~1-2ms |
| **Net change** | **~0ms or savings** (FSR is faster than XBR) |

**Bottom line:** Every phase is equal or better performance than today.

---

## Settings UI Design

### Video/Display Settings Section

```
┌─ Display ──────────────────────────────────────┐
│                                                  │
│  UI Scale          [====|======] 150%            │
│  (Scales legacy UO windows and menus)            │
│                                                  │
│  UI Filter         [Linear      ▼]              │
│  (Point / Linear / Anisotropic)                  │
│                                                  │
│  ☑ World Post-Processing                         │
│  World Filter      [XBR         ▼]              │
│  (Point / Linear / Anisotropic / XBR / FSR)      │
│                                                  │
│  XBR Sharpness     [====|===] 2.5                │
│  (Only visible when XBR selected)                │
│                                                  │
└──────────────────────────────────────────────────┘
```

---

## Implementation Order

### Phase 1 — Remove Outer RT, Add UI Scale Matrix
**Estimated complexity:** Medium
**Risk:** Low (mostly removing code)
**Files touched:** ~8 files
**Dependencies:** None

Tasks:
1. Remove `_screenRenderTarget` and related code from GameController.cs
2. Add `UIScale` property (replaces `RenderScale`)
3. Add `Matrix.CreateScale(UIScale)` to UIManager.Draw()
4. Add `UIManager.ScreenToUI()` for hit-testing
5. Update Mouse.cs — remove scattered `/RenderScale` adjustments
6. Update ImGuiRenderer.cs — remove `/RenderScale` from mouse position
7. Update LoginScene.cs — remove scale event, use fixed 640x480
8. Update GameCursor.cs — remove RenderScale references
9. Migrate settings: `GAME_SCALE` → `UI_SCALE`
10. Update settings UI labels and tooltips

### Phase 2 — Filtered UI Upscaling
**Estimated complexity:** Medium
**Risk:** Medium (new render target in UI path)
**Files touched:** ~4 files
**Dependencies:** Phase 1

Tasks:
1. Add `_uiRenderTarget` to UIManager
2. Render gumps to `_uiRenderTarget` at native resolution
3. Composite to backbuffer with configurable sampler
4. Add `UIFilteringMode` to Profile.cs
5. Add UI filter dropdown to settings

### Phase 3a — XBR Quality Parameter
**Estimated complexity:** Low
**Risk:** Low (shader parameter change)
**Files touched:** ~4 files
**Dependencies:** None (can be done independently)

Tasks:
1. Make `coef` a shader parameter in xBR.fx
2. Recompile xBR.fxc
3. Add parameter binding in XBREffect.cs
4. Add `XbrSharpness` to Profile.cs
5. Add slider to settings UI

### Phase 3b — FSR 1.0 Integration
**Estimated complexity:** High
**Risk:** Medium (new shader, SM3.0 port)
**Files touched:** ~6 files (2 new)
**Dependencies:** None (can be done independently)

Tasks:
1. Port FSR 1.0 EASU to HLSL SM3.0
2. Port FSR 1.0 RCAS to HLSL SM3.0
3. Create FSREffect.cs wrapper
4. Compile fsr.fxc
5. Add FSR case to GameScene.UpdatePostProcessState()
6. Add to PostProcessingType enum
7. Add to settings UI dropdown
8. Test on low-end hardware for performance validation

### Phase 3c — Hybrid Filtering
**Estimated complexity:** Low (if Phase 1+2 done)
**Risk:** Low
**Files touched:** ~2 files
**Dependencies:** Phase 1, Phase 2

Tasks:
1. Wire world filter and UI filter as independent settings
2. Ensure each render path uses its own filter setting
3. Update settings UI to show both independently

---

## Verification Plan

### Phase 1
- [ ] Login screen renders at native 640x480 regardless of UI scale
- [ ] ImGui windows are NOT affected by UI scale
- [ ] Legacy gumps (paperdoll, containers, skills) scale with UI scale slider
- [ ] Mouse click targets are correct on scaled gumps
- [ ] Game world is NOT affected by UI scale (Camera zoom only)
- [ ] FPS equal or better than before (no extra RT blit at 1.0x)
- [ ] Screenshots work without render target

### Phase 2
- [ ] Gump edges are smooth when UI scale > 1.0 (not pixelated)
- [ ] Text in gumps remains readable at 150%, 200%, 250%
- [ ] UI filter dropdown works (Point/Linear/Anisotropic)
- [ ] Performance within ~0.1ms of Phase 1

### Phase 3a
- [ ] XBR sharpness slider changes visual quality in real-time
- [ ] Low sharpness = pixel-y, high sharpness = smooth
- [ ] No artifacts at extreme values

### Phase 3b
- [ ] FSR option appears in world filter dropdown
- [ ] Visibly better quality than bilinear
- [ ] Performance within 1ms of XBR or better
- [ ] No visual artifacts on tile edges, character sprites, spell effects

### Phase 3c
- [ ] World filter and UI filter can be set independently
- [ ] Example: XBR world + Linear UI + Point text works correctly

---

## Open Questions

1. **Should UI scale affect the game viewport size?** Currently `RenderScale` doesn't change the viewport — world renders at the same tile count regardless. If UI elements are bigger, should the game viewport shrink to make room, or should gumps overlap more of the world view?

2. **TTF text rendering path:** FontStashSharp text could render at the scaled resolution directly (crisp), bypassing the UI render target. Worth the complexity?

3. **Container scale interaction:** `ContainersScale` and `GridContainersScale` already do per-gump scaling. Should `UIScale` multiply with these, or should they be relative to UIScale? (Probably multiply — UIScale is "make everything bigger", container scale is "customize this specific gump".)

4. **Backward compatibility:** Old `GAME_SCALE` setting maps to new `UIScale`. Users who had it at e.g. 1.5 should see the same gump sizes. World view will change though (no longer zoomed) — is this acceptable?

5. **FSR SM3.0 feasibility:** FSR 1.0's EASU pass uses some SM4.0 intrinsics (`gather4`). The SM3.0 fallback uses 4 individual texture fetches instead — slightly slower but functionally identical. Need to verify FNA's HLSL compiler handles the port correctly.
