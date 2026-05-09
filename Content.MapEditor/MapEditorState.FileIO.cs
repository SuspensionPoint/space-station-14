using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.MapEditor;

// Map file open/save using IFileDialogManager and MapLoaderSystem.
public sealed partial class MapEditorState
{
    private void OnFileNewPressed()
    {
        CreateNewMap();
    }

    private void OnFileOpenPressed()
    {
        OpenMapAsync();
    }

    private void OnFileSavePressed()
    {
        SaveMapAsync();
    }

    private void OnFileExitPressed()
    {
        IoCManager.Resolve<Robust.Client.IGameController>().Shutdown("Editor closed");
    }

    private async void OpenMapAsync()
    {
        try
        {
            var filters = new FileDialogFilters(new FileDialogFilters.Group("yml", "yaml"));
            var stream = await _fileDialog.OpenFile(filters, FileAccess.Read, FileShare.Read);
            if (stream == null)
                return;

            using var reader = new StreamReader(stream);
            var source = "opened map";

            var mapLoader = _entityManager.System<MapLoaderSystem>();

            var options = new DeserializationOptions
            {
                InitializeMaps = true,
                PauseMaps = false, // Load unpaused so entity systems (node groups, etc.) run
            };

            if (!mapLoader.TryLoadMap(reader, source, out var map, out var grids, options))
            {
                _sawmill.Error("Failed to load map file.");
                _screen.SetStatusInfo("Failed to load map");
                return;
            }

            _loadedMapId = map.Value.Comp.MapId;
            _screen.SetStatusInfo($"Loaded map ({grids!.Count} grid(s))");

            // Initialize the map to run entity startup events (node groups, icon smoothing, etc.).
            // Without this, cables/pipes render as dots because NodeGroupSystem never builds
            // connection data on paused entities. After init, re-pause to stop game logic.
            try
            {
                _mapManager.DoMapInitialize(_loadedMapId);
                _mapManager.SetMapPaused(_loadedMapId, true);
            }
            catch (Exception initEx)
            {
                _sawmill.Warning($"Map init/pause error (non-fatal): {initEx.Message}");
            }

            CenterOnMap(map.Value, grids);
            PopulateGridTabs(_loadedMapId);
            ComputeCableConnections();

            _sawmill.Info($"Map loaded: {grids.Count} grids on map {_loadedMapId}");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error opening map: {ex}");
            _screen.SetStatusInfo("Error loading map");
        }
    }

    private void CenterOnMap(Entity<MapComponent> map, System.Collections.Generic.HashSet<Entity<MapGridComponent>> grids)
    {
        if (grids.Count == 0)
        {
            _eye.Position = new MapCoordinates(Vector2.Zero, map.Comp.MapId);
            return;
        }

        var firstGrid = grids.First();
        var aabb = firstGrid.Comp.LocalAABB;
        var center = aabb.Center;

        var xform = _entityManager.GetComponent<TransformComponent>(firstGrid.Owner);
        var worldPos = xform.LocalPosition + center;

        _eye.Position = new MapCoordinates(worldPos, map.Comp.MapId);

        // Set a reasonable zoom level based on grid size.
        var maxDim = Math.Max(aabb.Width, aabb.Height);
        if (maxDim > 0)
        {
            var fitZoom = 21f / maxDim * 0.8f;
            fitZoom = Math.Clamp(fitZoom, MinZoom, 2f);
            _eye.Zoom = new Vector2(fitZoom, fitZoom);
        }
    }

    private async void SaveMapAsync()
    {
        try
        {
            var mapId = _eye.Position.MapId;
            var mapUid = _mapManager.GetMapEntityId(mapId);

            if (mapUid == EntityUid.Invalid)
            {
                _sawmill.Warning("No map to save (eye is in null space).");
                _screen.SetStatusInfo("No map to save");
                return;
            }

            var filters = new FileDialogFilters(new FileDialogFilters.Group("yml", "yaml"));
            var result = await _fileDialog.SaveFile(filters);
            if (result == null)
                return;

            var (stream, _) = result.Value;

            // Ensure the file has a .yml extension.
            if (stream is FileStream fs && !fs.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                                        && !fs.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                var newPath = fs.Name + ".yml";
                await stream.DisposeAsync();
                stream = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None);
            }

            await using (stream)
            {
                using var writer = new StreamWriter(stream);

                var mapLoader = _entityManager.System<MapLoaderSystem>();
                if (!mapLoader.TrySaveMap(mapUid, writer))
                {
                    _sawmill.Error("Failed to save map.");
                    _screen.SetStatusInfo("Failed to save map");
                    return;
                }
            }

            _screen.SetStatusInfo("Map saved");
            _sawmill.Info($"Map {mapId} saved.");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error saving map: {ex}");
            _screen.SetStatusInfo("Error saving map");
        }
    }
}
