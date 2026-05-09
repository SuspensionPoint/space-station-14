using System;
using Content.MapEditor.Tools;
using Robust.Client.Input;
using Robust.Shared.Maths;

namespace Content.MapEditor;

// Keyboard shortcut polling: tool hotkeys, undo/redo, rotate, select tool operations.
public sealed partial class MapEditorState
{
    /// <summary>
    ///     Polls key states each frame and fires actions on press edges.
    ///     B = Paint, E = Erase, I = Eyedropper, Ctrl+Z = Undo, Ctrl+Y / Ctrl+Shift+Z = Redo.
    /// </summary>
    private void UpdateKeyboardShortcuts()
    {
        // Don't process shortcuts while a tool stroke is in progress.
        if (_isToolActive)
        {
            UpdatePreviousKeyState();
            return;
        }

        var ctrl = _input.IsKeyDown(Keyboard.Key.Control);

        // Undo / Redo
        var zDown = _input.IsKeyDown(Keyboard.Key.Z);
        if (zDown && !_wasZDown && ctrl)
        {
            if (_input.IsKeyDown(Keyboard.Key.Shift))
                _commandStack.Redo();
            else
                _commandStack.Undo();
            _cablesDirty = true;
        }

        var yDown = _input.IsKeyDown(Keyboard.Key.Y);
        if (yDown && !_wasYDown && ctrl)
        {
            _commandStack.Redo();
            _cablesDirty = true;
        }

        // EntitySelectTool operations (Delete, R/Shift+R for rotate)
        if (_activeTool is EntitySelectTool entitySelectTool)
        {
            var deleteDown = _input.IsKeyDown(Keyboard.Key.Delete);
            if (deleteDown && !_wasDeleteDown)
                entitySelectTool.DeleteSelected(_toolContext);

            if (!ctrl)
            {
                var rDown = _input.IsKeyDown(Keyboard.Key.R);
                if (rDown && !_wasRDown)
                {
                    if (_input.IsKeyDown(Keyboard.Key.Shift))
                        entitySelectTool.RotateSelectedCCW(_toolContext);
                    else
                        entitySelectTool.RotateSelectedCW(_toolContext);
                }
            }
        }

        // SelectTool operations (Ctrl+C, Ctrl+X, Ctrl+V, Delete)
        if (_activeTool is SelectTool selectTool)
        {
            var deleteDown = _input.IsKeyDown(Keyboard.Key.Delete);
            if (deleteDown && !_wasDeleteDown)
                selectTool.DeleteSelection(_toolContext);

            if (ctrl)
            {
                var cDown = _input.IsKeyDown(Keyboard.Key.C);
                if (cDown && !_wasCDown)
                    selectTool.CopySelection(_toolContext);

                var xDown = _input.IsKeyDown(Keyboard.Key.X);
                if (xDown && !_wasXDown)
                    selectTool.CutSelection(_toolContext);

                var vDown = _input.IsKeyDown(Keyboard.Key.V);
                if (vDown && !_wasVDown)
                {
                    var screenPos = _input.MouseScreenPosition;
                    if (TryResolveGridTile(screenPos, out var pastePos))
                        selectTool.PasteClipboard(_toolContext, pastePos);
                }
            }
        }

        // Placement rotation (R/Shift+R cycles angle for pipe draw and entity place)
        if (!ctrl && _activeTool is PipeDrawTool or EntityPlaceTool)
        {
            var rDown = _input.IsKeyDown(Keyboard.Key.R);
            if (rDown && !_wasRDown)
            {
                var delta = _input.IsKeyDown(Keyboard.Key.Shift) ? -Math.PI / 2 : Math.PI / 2;
                _toolContext.PlacementRotation += new Angle(delta);
                var deg = (int) Math.Round(_toolContext.PlacementRotation.Degrees) % 360;
                if (deg < 0) deg += 360;
                _screen.SetStatusInfo($"Rotation: {deg}°");
            }
        }

        // Tool shortcuts (only without modifiers, and not when entity select, pipe draw,
        // or entity place is active since R is used for rotation there)
        if (!ctrl && _activeTool is not EntitySelectTool && _activeTool is not PipeDrawTool && _activeTool is not EntityPlaceTool)
        {
            var bDown = _input.IsKeyDown(Keyboard.Key.B);
            if (bDown && !_wasBDown)
                OnToolSelected("paint");

            var eDown = _input.IsKeyDown(Keyboard.Key.E);
            if (eDown && !_wasEDown)
                OnToolSelected("erase");

            var iDown = _input.IsKeyDown(Keyboard.Key.I);
            if (iDown && !_wasIDown)
                OnToolSelected("eyedropper");

            var fDown = _input.IsKeyDown(Keyboard.Key.F);
            if (fDown && !_wasFDown)
                OnToolSelected("fill");

            var rDown = _input.IsKeyDown(Keyboard.Key.R);
            if (rDown && !_wasRDown)
                OnToolSelected("rectangle");

            var lDown = _input.IsKeyDown(Keyboard.Key.L);
            if (lDown && !_wasLDown)
                OnToolSelected("line");

            var cDown = _input.IsKeyDown(Keyboard.Key.C);
            if (cDown && !_wasCDown)
                OnToolSelected("circle");

            var sDown = _input.IsKeyDown(Keyboard.Key.S);
            if (sDown && !_wasSDown)
                OnToolSelected("select");

            var gDown = _input.IsKeyDown(Keyboard.Key.G);
            if (gDown && !_wasGDown)
                OnToolSelected("entityplace");

            var qDown = _input.IsKeyDown(Keyboard.Key.Q);
            if (qDown && !_wasQDown)
                OnToolSelected("entityselect");

            var jDown = _input.IsKeyDown(Keyboard.Key.J);
            if (jDown && !_wasJDown)
                OnToolSelected("pipedraw");

            var kDown = _input.IsKeyDown(Keyboard.Key.K);
            if (kDown && !_wasKDown)
                OnToolSelected("cabledraw");
        }

        UpdatePreviousKeyState();
    }

    private void UpdatePreviousKeyState()
    {
        _wasBDown = _input.IsKeyDown(Keyboard.Key.B);
        _wasEDown = _input.IsKeyDown(Keyboard.Key.E);
        _wasIDown = _input.IsKeyDown(Keyboard.Key.I);
        _wasFDown = _input.IsKeyDown(Keyboard.Key.F);
        _wasRDown = _input.IsKeyDown(Keyboard.Key.R);
        _wasLDown = _input.IsKeyDown(Keyboard.Key.L);
        _wasCDown = _input.IsKeyDown(Keyboard.Key.C);
        _wasSDown = _input.IsKeyDown(Keyboard.Key.S);
        _wasXDown = _input.IsKeyDown(Keyboard.Key.X);
        _wasVDown = _input.IsKeyDown(Keyboard.Key.V);
        _wasZDown = _input.IsKeyDown(Keyboard.Key.Z);
        _wasYDown = _input.IsKeyDown(Keyboard.Key.Y);
        _wasDeleteDown = _input.IsKeyDown(Keyboard.Key.Delete);
        _wasGDown = _input.IsKeyDown(Keyboard.Key.G);
        _wasQDown = _input.IsKeyDown(Keyboard.Key.Q);
        _wasJDown = _input.IsKeyDown(Keyboard.Key.J);
        _wasKDown = _input.IsKeyDown(Keyboard.Key.K);
    }
}
