---
title: "Render Scaling Phase 1: Remove Outer RT, Add UI Scale"
created: 2026-02-13
poured:
  - TazUo-mol-49jk
  - TazUo-mol-h8nr
  - TazUo-mol-lo7e
  - TazUo-mol-31e0
  - TazUo-mol-f9i7
  - TazUo-mol-qrz2
  - TazUo-mol-4p4t
  - TazUo-mol-joe9
iteration: 2
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Render Scaling Phase 1: Remove Outer RT, Add UI Scale</project_name>

  <overview>
    Remove the redundant _screenRenderTarget from GameController and replace the global
    RenderScale (which scales everything uniformly, causing double-scaled ImGui, blurred text,
    and login-screen clipping) with a targeted UIScale that only affects legacy UO gumps via
    a scale matrix in UIManager.Draw(). This is Phase 1 of the layered UI scaling overhaul
    described in PRD_RENDER_SCALING_AND_UPSCALING.md.

    The net result: legacy gumps scale up for readability on modern displays, ImGui windows
    are unaffected (they have their own DPI scaling), the login screen bypasses UIScale,
    and we remove one full-screen render target blit per frame (performance improvement).

    IMPORTANT ARCHITECTURAL NOTE: The batcher transform matrix ONLY affects rendering (it
    feeds into the shader's MatrixTransform). Hit-testing in Control.HitTest() operates on
    raw pixel coordinates independently. This means every place that does mouse-to-gump
    coordinate mapping needs explicit inverse-scaling — the matrix does NOT do this for us.
  </overview>

  <context>
    <existing_patterns>
      - GameController.Draw() (line 535) is the outer draw loop: background, Scene.Draw, UIManager.Draw, GameCursor.Draw, ImGui, Plugins
      - _screenRenderTarget (line 51) wraps everything; composited at line 611-626 with RenderScale
      - _useScreenRenderTarget (line 52) is initialized true and NEVER changed — the "else" path is dead code
      - RenderScale property (line 84-88) is a simple float with 0.1 floor; SetScale() fires ScaleChanged event
      - ScaleChanged event has exactly ONE subscriber: LoginScene (verified across entire codebase)
      - UIManager.Draw(batcher) at Game/Managers/UIManager.cs:465 does batcher.Begin()/End() with no transform matrix
      - UltimaBatcher2D.Begin() has 3 overloads: Begin(), Begin(Effect), Begin(Effect, Matrix) — Matrix stored as _transformMatrix, multiplied into projection in ApplyStates() (Batcher2D.cs:1452)
      - No gumps call batcher.Begin()/End() internally (verified across 100+ gump subclasses) — the outer Begin/End in UIManager.Draw() is the only batch scope
      - Scissor clipping already supports transform matrices via ScissorStack.CalculateScissors() (ScissorStack.cs:65-82)
      - Mouse.cs lines 150-152 normalize SDL coords to backbuffer coords AND divide by RenderScale
      - ImGuiRenderer.cs line 223 divides by RenderScale for ImGui mouse position
      - Hit-testing pipeline: HandleMouseInput() (UIManager.cs:628) → GetMouseOverControl(Mouse.Position) (line 674) → Control.HitTest(position) (Control.cs:662) — all use raw pixel coords, NO matrix involvement
      - Gump drag: DoDragControl() (UIManager.cs:832) computes delta = Mouse.Position - _dragOrigin, adds delta to DraggingControl.X/Y (lines 898-899)
      - ContextMenuControl positions itself at Mouse.Position (ContextMenuControl.cs:132-133)
      - LoginScene gumps render via UIManager.Draw() — they would be affected by any matrix applied there
      - LoginScene.Load() sets AllowUserResizing=false and calls UpdateWindowSize() which sets window to 640*RenderScale x 480*RenderScale
      - LoginScene gumps use hardcoded 640x480 coordinates (e.g. GumpPicTiled(0, 0, 640, 480, 0x0150))
      - GameCursor.cs uses RenderScale at lines 387, 429 for cursor aura and item drag sizing
      - Screenshot methods (lines 1142-1197) read from _screenRenderTarget when available, fall back to backbuffer
      - Settings stored via Constants.SqlSettings.GAME_SCALE ("game_scale") loaded in Client.cs:221 via Settings.GetAsyncOnMainThread()
      - Constants.MAX_GAME_SCALE=1.5f, MIN_GAME_SCALE=-0.6f (Constants.cs:91-92) — negative MIN is a quirk: GeneralTabContent.cs:55 uses Math.Abs() to get 60 (i.e. 60%-150% range)
      - ContainersScale is applied per-draw-call in ItemGump rendering and per-hit-test in ItemGump.Contains() — NOT via a matrix
    </existing_patterns>
    <integration_points>
      - GameController.cs: Remove RT infrastructure, replace RenderScale with UIScale, gate UIScale matrix on active scene
      - UIManager.cs: Accept scale matrix in Draw(), add ScreenToUI() helper, apply inverse-scale in HandleMouseInput() and DoDragControl()
      - Mouse.cs: Remove /RenderScale division (Mouse.Position becomes raw backbuffer coords)
      - ImGuiRenderer.cs: Remove /RenderScale from mouse position
      - LoginScene.cs: Remove ScaleChanged subscription, use fixed 640x480 window size
      - GameCursor.cs: Remove RenderScale references from aura and item drag
      - ContextMenuControl.cs: Use ScreenToUI() for positioning when UIScale != 1.0
      - GeneralTabContent.cs: Update ImGui settings slider label/binding, fix Math.Abs() quirk for new MIN
      - ModernOptionsGump.cs: Update legacy settings slider label/binding
      - Client.cs: Update settings key load (GAME_SCALE -> UI_SCALE with migration)
      - Constants.cs: Rename scale constants and settings key
    </integration_points>
    <new_technologies>
      - None — uses existing Matrix.CreateScale() from XNA/FNA framework
    </new_technologies>
    <conventions>
      - Settings use SqlSettings constants with SettingsScope.Global
      - Settings loaded via Client.Settings.GetAsyncOnMainThread() in Client.cs
      - Profile changes saved with Client.Settings.SetAsync()
      - Batcher uses Begin()/End() pairs with optional transform matrix
      - Settings migration precedent: try new key, fall back to old key, clamp, save as new key (see AutoLootManager, GraphicsReplacement for examples)
      - Build command: dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj
    </conventions>
  </context>

  <tasks>
    <task id="remove-screen-rt" priority="0" category="functional">
      <title>Remove _screenRenderTarget and outer RT composite from GameController</title>
      <description>
        Remove the entire _screenRenderTarget infrastructure from GameController.cs. This
        includes the field (line 51), the _useScreenRenderTarget flag (line 52), the
        EnsureScreenRenderTarget() method (lines 493-533), and the RT branching in Draw()
        (lines 545-637). After removal, Draw() should render directly to the backbuffer
        always: background, Scene.Draw, UIManager.Draw, GameCursor.Draw, ImGui, Plugins
        in sequence with no RT indirection.

        Note: _useScreenRenderTarget is initialized to true and NEVER changed anywhere in
        the codebase. The "else" fallback path in Draw() is dead code. Removal simplifies
        Draw() to just the fallback path (which is the correct direct-to-backbuffer behavior).

        Simplify the screenshot methods (TakeScreenshot lines 1142+, ClipboardScreenshot
        lines 1191+) to always use GraphicsDevice.GetBackBufferData() since there is no
        longer a render target to read from. After this change, screenshots capture the
        final composited backbuffer including any UIScale effects — this is correct behavior.

        Remove the ScaleChanged event (line 96) and SetScale() method (lines 310-314).
        ScaleChanged has exactly one subscriber (LoginScene) which will be updated in a
        later task. The Dispose() cleanup of _screenRenderTarget (lines 228-229) should
        also be removed.
      </description>
      <steps>
        - Remove _screenRenderTarget field (line 51) and _useScreenRenderTarget flag (line 52)
        - Remove EnsureScreenRenderTarget() method entirely (lines 493-533)
        - Simplify Draw() to always render directly to backbuffer — keep only the "else" path logic: Clear, background, Scene, UIManager, GameCursor, ImGui, Plugins (remove all RT branching)
        - Remove ScaleChanged event declaration (line 96)
        - Remove SetScale() method (lines 310-314)
        - Simplify TakeScreenshot() to always use GraphicsDevice.GetBackBufferData (remove RT conditional at lines 1142-1147)
        - Simplify ClipboardScreenshot() to always use graphicDevice.GetBackBufferData (remove RT conditional at lines 1191-1193)
        - Remove _screenRenderTarget?.Dispose() from UnloadContent (lines 228-229)
      </steps>
      <test_steps>
        1. Build succeeds with no errors (dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj)
        2. Game launches and renders correctly at the login screen
        3. Game world renders normally after logging in
        4. ImGui windows render on top of the game world
        5. Screenshots still work
        6. No visual regressions compared to before (at RenderScale 1.0 the RT was a 1:1 passthrough)
      </test_steps>
      <review></review>
    </task>

    <task id="add-uiscale-property" priority="0" category="functional">
      <title>Add UIScale property to GameController, update settings and sliders</title>
      <description>
        Replace the old RenderScale property (lines 83-88) with a new UIScale property.
        The property should be a float clamped between 1.0 (no downscaling) and 2.5.

        Update Constants.cs (lines 91-92, 122):
        - Replace MAX_GAME_SCALE=1.5f with MAX_UI_SCALE=2.5f
        - Replace MIN_GAME_SCALE=-0.6f with MIN_UI_SCALE=1.0f (eliminates the negative quirk)
        - Replace SqlSettings.GAME_SCALE="game_scale" with SqlSettings.UI_SCALE="ui_scale"

        Update Client.cs (line 221) settings load:
        - Try loading UI_SCALE first with default 1.0f
        - If not found (returns default), try loading old GAME_SCALE key
        - Clamp old value to [1.0, 2.5] range
        - Save migrated value under UI_SCALE key for future loads
        - Call the new UIScale setter (no more SetScale() method)

        Update GeneralTabContent.cs (ImGui slider, lines 54-56, 221-230):
        - Reference Client.Game.UIScale instead of Client.Game.RenderScale
        - Set UIScale directly instead of calling Client.Game.SetScale()
        - Save with Constants.SqlSettings.UI_SCALE
        - FIX: Remove Math.Abs() on _minScale (line 55) — no longer needed since MIN_UI_SCALE is positive
        - Change label text from "Game Scale" to "UI Scale"
        - Add tooltip: "Scales legacy UO interface elements. Does not affect the game world or modern UI windows."

        Update ModernOptionsGump.cs (legacy slider, line 4045-4059):
        - Same changes as GeneralTabContent: reference UIScale, save as UI_SCALE
        - Change label text to "UI Scale"
      </description>
      <steps>
        - Replace RenderScale property with UIScale property (clamped 1.0 - 2.5)
        - Update Constants.cs: MAX_UI_SCALE=2.5f, MIN_UI_SCALE=1.0f, SqlSettings.UI_SCALE="ui_scale"
        - Update Client.cs line 221: load UI_SCALE with GAME_SCALE fallback and migration
        - Update GeneralTabContent.cs: reference UIScale, use new constants, remove Math.Abs() on _minScale, save as UI_SCALE, update label to "UI Scale"
        - Update ModernOptionsGump.cs: reference UIScale, save as UI_SCALE, update label to "UI Scale"
      </steps>
      <test_steps>
        1. Build succeeds
        2. Game loads without errors — settings migration works (no crash on first run with old settings)
        3. UI Scale slider appears in both ImGui and legacy settings
        4. Slider range is 100% to 250%
        5. Old GAME_SCALE setting value is respected on first load (e.g. old 1.2 becomes 1.2)
        6. Old values below 1.0 (e.g. 0.6) are clamped to 1.0
      </test_steps>
      <review></review>
    </task>

    <task id="ui-scale-matrix" priority="1" category="functional">
      <title>Apply UIScale transform matrix in UIManager.Draw() with login scene bypass</title>
      <description>
        Modify UIManager.Draw() (Game/Managers/UIManager.cs:465) to accept and apply a
        scale matrix. GameController.Draw() should compute the matrix and pass it.

        CRITICAL: The batcher transform matrix ONLY affects rendering (vertex shader). It
        does NOT affect hit-testing. Hit-testing changes are in the next task.

        In UIManager.Draw(), change the signature to accept a Matrix parameter:
          public static void Draw(UltimaBatcher2D batcher, Matrix uiTransform)
        Change batcher.Begin() (line 468) to batcher.Begin(null, uiTransform).

        In GameController.Draw() (line 584), compute and pass the matrix:
          - If active Scene is LoginScene: pass Matrix.Identity (login gumps are hardcoded
            for 640x480 and must NOT be scaled)
          - If UIScale == 1.0f: pass Matrix.Identity (no-op optimization)
          - Otherwise: pass Matrix.CreateScale(UIScale, UIScale, 1f)

        Add a static helper UIManager.ScreenToUI(Point screenPos) that converts screen
        coordinates to UI-space coordinates for hit-testing:
          public static Point ScreenToUI(Point screenPos)
          {
              float inv = 1f / Client.Game.UIScale;
              return new Point((int)(screenPos.X * inv), (int)(screenPos.Y * inv));
          }
        This will be used in the next task but should be defined here.

        Also add UIManager.ScreenToUIDelta(Point delta) for drag operations:
          public static Point ScreenToUIDelta(Point delta)
          {
              float inv = 1f / Client.Game.UIScale;
              return new Point((int)(delta.X * inv), (int)(delta.Y * inv));
          }
      </description>
      <steps>
        - Modify UIManager.Draw() signature to accept Matrix parameter
        - Pass matrix to batcher.Begin(null, uiTransform) inside UIManager.Draw()
        - In GameController.Draw(), compute the matrix gated on: not LoginScene AND UIScale != 1.0
        - Pass Matrix.Identity for LoginScene or UIScale 1.0
        - Add static UIManager.ScreenToUI(Point) helper for hit-test inverse-scaling
        - Add static UIManager.ScreenToUIDelta(Point) helper for drag inverse-scaling
      </steps>
      <test_steps>
        1. Build succeeds
        2. At UIScale 1.0, gumps render identically to before
        3. At UIScale 1.5, legacy gumps (paperdoll, containers, skills) are visibly larger
        4. ImGui windows are NOT affected by UIScale changes
        5. Game world viewport is NOT affected by UIScale changes
        6. Login screen gumps are NOT scaled (stay at 640x480 layout)
      </test_steps>
      <review></review>
    </task>

    <task id="fix-mouse-coordinates" priority="1" category="functional">
      <title>Fix mouse coordinates, hit-testing, dragging, and context menus for UIScale</title>
      <description>
        This task makes input handling work correctly with the UIScale transform matrix.
        The matrix only affects rendering — all hit-testing and positioning code must be
        updated to use inverse-scaling via UIManager.ScreenToUI().

        IMPORTANT CONTEXT: Currently Mouse.Position is normalized in Mouse.cs:150-152 as:
          Position.X = (int)((Position.X * PreferredWidth / WindowWidth) / RenderScale)
        After removing /RenderScale (done in this task), Mouse.Position will be in raw
        backbuffer coordinates. This is correct — world interactions, ImGui, and plugins
        all want raw coords. Only UIManager gump hit-testing needs inverse-scaling.

        == Mouse.cs (lines 150-152) ==
        Remove the / Client.Game.RenderScale division from both X and Y.
        Mouse.Position now reports raw backbuffer coordinates.

        == ImGuiRenderer.cs (line 223) ==
        Remove the / Client.Game.RenderScale division.
        ImGui receives raw backbuffer coords (it has its own DPI scaling).

        == UIManager.cs — HandleMouseInput() (line 628-672) ==
        At line 630, change:
          Control gump = GetMouseOverControl(Mouse.Position);
        To:
          Point uiMouse = (Client.Game.Scene is LoginScene) ? Mouse.Position : ScreenToUI(Mouse.Position);
          Control gump = GetMouseOverControl(uiMouse);
        Also update the InvokeMouseEnter/Exit/Over calls in this method to pass uiMouse
        instead of Mouse.Position (lines 634, 640, 649, 655, 660).

        == UIManager.cs — DoDragControl() (line 832-901) ==
        At line 839, the delta is computed as Mouse.Position - _dragOrigin. Both are in
        screen coords but DraggingControl.X/Y are in unscaled UI coords. At UIScale 1.5,
        a 150px mouse drag should move the gump 100 UI units. Fix:
          Point delta = Mouse.Position - _dragOrigin;
          // ... existing Ctrl axis-lock and speed reduction logic ...
          if (!(Client.Game.Scene is LoginScene))
              delta = ScreenToUIDelta(delta);
          DraggingControl.X += delta.X;
          DraggingControl.Y += delta.Y;
        Apply the inverse-scaling AFTER the Ctrl modifier logic (which operates on screen
        pixels for consistent feel) but BEFORE applying to gump X/Y.

        == ContextMenuControl.cs (lines 132-133) ==
        The context menu positions itself at Mouse.Position and compares against window
        bounds. When UIScale != 1.0, the position needs inverse-scaling since the menu
        is a gump drawn through UIManager (and thus scaled by the matrix):
          Point uiPos = UIManager.ScreenToUI(Mouse.Position);
          X = uiPos.X + 5;
          Y = uiPos.Y - 20;
        The bounds comparison (lines 135-140) should also use inverse-scaled window bounds
        or compare in screen space. Simplest approach: inverse-scale the bounds:
          int maxW = (int)(Client.Game.Window.ClientBounds.Width / Client.Game.UIScale);
          int maxH = (int)(Client.Game.Window.ClientBounds.Height / Client.Game.UIScale);

        == GameCursor.cs (lines 387, 429) ==
        Remove Client.Game.RenderScale usage from both locations.
        Line 387-389 (cursor aura): Use raw Mouse.Position — the aura is drawn outside
        UIManager's scaled batch, directly in GameController.Draw().
        Line 429 (item drag): Remove RenderScale as the initial scale value. If
        ScaleItemsInsideContainers is true, use UIManager.ContainerScale; otherwise
        use 1.0f (no scaling). The dragged item is drawn in the GameCursor batch which
        is NOT inside UIManager's scaled Begin/End.

        == LoginScene.cs (lines 50, 79, 150, 552) ==
        Remove ScaleChanged event subscription (line 50) and unsubscribe (line 552).
        Remove GameOnScaleChanged handler (line 79).
        Change UpdateWindowSize() (line 150) to use fixed 640x480:
          private void UpdateWindowSize() => Client.Game.SetWindowSize(640, 480);

        == UIManager.IsMouseOverWorld (UIManager.cs:66-80) ==
        This checks Camera.Bounds.Contains(mouse) — the camera bounds are in backbuffer
        coords and Mouse.Position is now in backbuffer coords, so this needs NO changes.
        World interactions are not affected by UIScale.
      </description>
      <steps>
        - Mouse.cs: Remove / Client.Game.RenderScale from Position.X (line 150) and Position.Y (line 152)
        - ImGuiRenderer.cs: Remove / Client.Game.RenderScale from mouse event (line 223)
        - UIManager.HandleMouseInput(): Use ScreenToUI(Mouse.Position) for GetMouseOverControl and InvokeMouse* calls (gated on not LoginScene)
        - UIManager.DoDragControl(): Apply ScreenToUIDelta() to delta before adding to gump X/Y (gated on not LoginScene)
        - ContextMenuControl.cs: Use ScreenToUI() for X/Y positioning, inverse-scale bounds comparison
        - GameCursor.cs line 387-389: Remove RenderScale, use raw Mouse.Position for aura
        - GameCursor.cs line 429: Remove RenderScale as initial scale, use 1.0f (or ContainerScale when applicable)
        - LoginScene.cs: Remove ScaleChanged subscription (line 50), handler (line 79), unsubscribe (line 552)
        - LoginScene.cs: Change UpdateWindowSize() to fixed 640x480
        - Verify UIManager.IsMouseOverWorld needs NO changes (already uses raw backbuffer coords)
      </steps>
      <test_steps>
        1. Build succeeds
        2. Mouse clicks land on correct gump targets at UIScale 1.0
        3. Mouse clicks land on correct gump targets at UIScale 1.5 and 2.0
        4. Gump dragging works correctly at UIScale 1.5 (gump follows mouse smoothly, no drift)
        5. Right-click context menus appear at correct positions at UIScale 1.5
        6. Context menus stay within window bounds at screen edges
        7. ImGui window clicks work correctly regardless of UIScale
        8. Login screen renders at fixed 640x480, not affected by UIScale
        9. Clicking on world objects (NPCs, items, ground) works correctly at UIScale 1.5
        10. Game cursor aura renders at correct position under cursor
        11. Item drag follows mouse correctly (both normal and ContainerScale modes)
        12. Ctrl+drag gump precision mode works correctly at UIScale > 1.0
      </test_steps>
      <review></review>
    </task>
  </tasks>

  <success_criteria>
    - _screenRenderTarget completely removed from GameController — no outer RT blit
    - Legacy gumps scale with UIScale slider (1.0x - 2.5x)
    - ImGui windows are NOT affected by UIScale
    - Login screen gumps bypass UIScale (rendered with Matrix.Identity)
    - Mouse hit-testing correct on scaled gumps via ScreenToUI() inverse-scaling
    - Gump dragging works at all UIScale values (delta inverse-scaled)
    - Context menus position correctly at all UIScale values
    - Game world uses Camera.Zoom only (unaffected by UIScale)
    - Performance equal or better than before (one fewer full-screen blit)
    - Old GAME_SCALE setting migrates cleanly to UI_SCALE
    - Screenshots work from backbuffer (captures final composited output)
    - No regressions in existing functionality
  </success_criteria>
</project_specification>
