using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    public static class ProjectionLevelVerification
    {
        public static void Run()
        {
            var report = new StringBuilder("Editable level data and editor checks\n");
            string logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/DoomSpriteProjection/Editor/ProjectionLevelVerificationScene.unity");
            Editor levelEditor = null;
            int passed = 0;
            bool success = false;
            Directory.CreateDirectory(logDirectory);

            void Check(bool condition, string description)
            {
                if (!condition) throw new Exception(description);
                passed++;
                report.AppendLine("PASS: " + description);
            }

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ProjectionLevel level = new GameObject("Verification Level").AddComponent<ProjectionLevel>();

                level.Initialize(2, 80, 0.1f, new Vector2(-2, -3), LevelCell.Floor);
                Check(level.width == 4 && level.height == 64 && level.cellSize == 0.5f,
                    "Initialization clamps dimensions and cell size to the supported range");
                Check(level.Bounds == new Rect(-2, -3, 2, 32), "Map bounds account for origin and cell size");
                Check(level.GetCell(-1, 0) == LevelCell.Wall && level.GetCell(4, 0) == LevelCell.Wall,
                    "Cells outside the map are solid walls");

                level.Initialize(4, 4, 2, new Vector2(-4, -2));
                int previousRevision = level.Revision;
                Check(level.SetCell(1, 2, LevelCell.Floor) && level.Revision != previousRevision,
                    "Painting a changed cell increments the render revision");
                previousRevision = level.Revision;
                Check(!level.SetCell(1, 2, LevelCell.Floor) && level.Revision == previousRevision &&
                      !level.SetCell(-1, 0, LevelCell.Floor), "Repeated or outside edits leave the cells unchanged");
                Check(level.WorldToCell(new Vector2(-4.1f, -2.1f)) == new Vector2Int(-1, -1),
                    "World-to-cell conversion floors negative coordinates");
                Check(level.CellCenter(1, 2) == new Vector2(-1, 3) &&
                      level.WorldToCell(level.CellCenter(1, 2)) == new Vector2Int(1, 2),
                    "Cell centers round-trip through the world coordinate mapping");

                level.SetCell(3, 2, LevelCell.Door);
                level.width = 6;
                level.height = 5;
                level.Touch();
                Check(level.GetCell(1, 2) == LevelCell.Floor && level.GetCell(3, 2) == LevelCell.Door &&
                      level.GetCell(5, 4) == LevelCell.Wall, "Growing a map preserves overlapping cells and fills new cells with walls");
                level.width = 4;
                level.height = 4;
                level.Touch();
                Check(level.GetCell(1, 2) == LevelCell.Floor && level.GetCell(3, 2) == LevelCell.Door,
                    "Shrinking a map preserves the cells within the new dimensions");

                level.Initialize(4, 4, 1, Vector2.zero);
                Check(!level.TryFindSpawn(out _), "A completely solid map has no valid spawn");
                level.SetCell(1, 1, LevelCell.Floor);
                level.SetCell(3, 3, LevelCell.Floor);
                level.playerStart = new Vector2(2.8f, 2.8f);
                Check(level.TryFindSpawn(out Vector2 spawn) && spawn == new Vector2(3.5f, 3.5f),
                    "An invalid spawn falls back to the nearest usable floor cell");
                level.playerStart = new Vector2(1.3f, 1.4f);
                Check(level.TryFindSpawn(out spawn) && spawn == level.playerStart,
                    "A valid arbitrary start position is preserved exactly");
                Check(!level.CanOccupy(new Vector2(1.05f, 1.5f)) && level.CanOccupy(new Vector2(1.5f, 1.5f)),
                    "Player radius blocks wall overlap while allowing the floor cell center");

                level.Initialize(8, 8, 1, Vector2.zero, LevelCell.Floor);
                for (int y = 0; y < 8; y++) level.SetCell(5, y, LevelCell.Wall);
                Check(level.Raycast(new Vector2(1.5f, 2.5f), new Vector2(2, 0), float.PositiveInfinity,
                    out LevelRayHit hit) && hit.x == 5 && hit.y == 2 && hit.sideX &&
                    Near(hit.depth, 1.75f) && Near(hit.u, 0.5f),
                    "An unnormalized ray returns its parameter depth and wall texture coordinate");
                Check(!level.Raycast(new Vector2(1.5f, 2.5f), new Vector2(2, 0), 1.7f, out hit) &&
                      float.IsPositiveInfinity(hit.depth), "A depth cap prevents hitting geometry beyond the requested range");
                Check(!level.Raycast(new Vector2(1.5f, 2.5f), Vector2.zero, float.PositiveInfinity, out hit) &&
                      float.IsPositiveInfinity(hit.depth), "A purely vertical 3D ray has no horizontal wall hit");
                Check(level.Raycast(new Vector2(1.5f, 2.5f), Vector2.left, float.PositiveInfinity, out hit) &&
                      hit.x == -1 && Near(hit.depth, 1.5f), "A negative-axis ray stops at the map boundary");
                Check(level.Raycast(new Vector2(1.5f, 2.5f), new Vector2(0, 3), float.PositiveInfinity, out hit) &&
                      hit.y == 8 && Near(hit.depth, 5.5f / 3f), "A zero-X ray preserves its scale and hits the upper map boundary");
                level.SetCell(5, 2, LevelCell.Door);
                Check(level.Raycast(new Vector2(1.5f, 2.5f), Vector2.right, float.PositiveInfinity, out hit) &&
                      hit.cell == LevelCell.Door && !level.CanOccupy(level.CellCenter(5, 2)),
                    "Closed door cells participate in both ray hits and collision");

                Vector2[] directions = { new Vector2(1.5f, 1), new Vector2(-1, 0.7f),
                    new Vector2(0.4f, -2), Vector2.up, Vector2.left, new Vector2(1, 1) };
                foreach (Vector2 direction in directions)
                {
                    Vector2 start = new Vector2(1.5f, 2.5f);
                    float expected = ClosestBoxHit(level, start, direction);
                    if (!level.Raycast(start, direction, float.PositiveInfinity, out hit) || !Near(hit.depth, expected))
                        throw new Exception($"DDA/AABB mismatch for {direction}: {hit.depth}, expected {expected}.");
                }
                Check(true, "Six cardinal and diagonal DDA rays match independent ray/AABB intersections");
                level.Initialize(4, 4, 1, Vector2.zero, LevelCell.Floor);
                level.SetCell(2, 1, LevelCell.Wall);
                Check(level.Raycast(new Vector2(1.5f, 1.5f), new Vector2(1, 1), float.PositiveInfinity, out hit) &&
                      Near(hit.depth, 0.5f), "A ray through a grid corner does not leak past an adjacent wall");

                level.Initialize(8, 8, 1, Vector2.zero, LevelCell.Floor);
                for (int y = 0; y < 8; y++) level.SetCell(4, y, LevelCell.Wall);
                Vector2 moved = level.Move(new Vector2(2, 2), new Vector2(100, 0));
                Check(moved.x <= 3.801f && moved.x > 3.5f && Near(moved.y, 2),
                    "A large movement is subdivided and cannot tunnel through a wall");
                moved = level.Move(new Vector2(3.5f, 1.5f), new Vector2(3, 3));
                Check(moved.x <= 3.801f && Near(moved.y, 4.5f) && level.CanOccupy(moved),
                    "Diagonal movement slides along a blocking wall");
                Vector2 blockedStart = new Vector2(4.5f, 4.5f);
                Check(level.Move(blockedStart, new Vector2(-3, 0)) == blockedStart,
                    "Movement does not silently teleport a player whose start is inside a wall");
                moved = level.Move(new Vector2(0.5f, 0.5f), new Vector2(-4, -4));
                Check(moved.x >= 0.199f && moved.y >= 0.199f && level.CanOccupy(moved),
                    "The player radius stays inside both outer map boundaries");
                level.Initialize(8, 8, 1, Vector2.zero, LevelCell.Floor);
                level.SetCell(4, 4, LevelCell.Wall);
                Check(!level.CanOccupy(new Vector2(3.9f, 3.9f)) && level.CanOccupy(new Vector2(3.84f, 3.84f)),
                    "Collision tests the circular radius at a corner rather than an oversized square");

                level.Initialize(4, 4, 1, Vector2.zero);
                Undo.ClearAll();
                levelEditor = Editor.CreateEditor(level, typeof(ProjectionLevelEditor));
                Undo.IncrementCurrentGroup();
                int paintGroup = Undo.GetCurrentGroup();
                Check(ProjectionLevelEditor.PaintCell(level, new Vector2Int(1, 1), LevelCell.Floor) &&
                      ProjectionLevelEditor.PaintCell(level, new Vector2Int(2, 1), LevelCell.Door),
                    "The editor paint API applies floor and door brushes");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(paintGroup);
                Check(level.gameObject.scene.isDirty &&
                      !ProjectionLevelEditor.PaintCell(level, new Vector2Int(-1, 1), LevelCell.Floor) &&
                      !ProjectionLevelEditor.PaintCell(level, new Vector2Int(1, 1), LevelCell.Floor),
                    "Painting marks the scene dirty and ignores outside or unchanged cells");
                Undo.PerformUndo();
                Check(level.GetCell(1, 1) == LevelCell.Wall && level.GetCell(2, 1) == LevelCell.Wall,
                    "Undo restores all cells from one painting operation");
                Undo.PerformRedo();
                Check(level.GetCell(1, 1) == LevelCell.Floor && level.GetCell(2, 1) == LevelCell.Door,
                    "Redo restores the painted floor and door");

                var viewerObject = new GameObject("Verification Viewer");
                viewerObject.SetActive(false);
                SpriteProjectionDemo viewer = viewerObject.AddComponent<SpriteProjectionDemo>();
                level.viewer = viewer;
                viewer.playerPosition = level.playerStart = new Vector2(1.5f, 1.5f);
                level.playerYaw = 45;
                Undo.IncrementCurrentGroup();
                int startGroup = Undo.GetCurrentGroup();
                Check(ProjectionLevelEditor.SetStart(level, new Vector2Int(3, 3)) &&
                      level.GetCell(3, 3) == LevelCell.Floor && level.playerStart == new Vector2(3.5f, 3.5f) &&
                      viewer.playerPosition == level.playerStart && viewer.yawDegrees == 45,
                    "The start brush makes a floor cell and synchronizes player position and direction");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(startGroup);
                Undo.PerformUndo();
                Check(level.GetCell(3, 3) == LevelCell.Wall && level.playerStart == new Vector2(1.5f, 1.5f) &&
                      viewer.playerPosition == level.playerStart && viewer.yawDegrees == 0,
                    "Undo restores both the start cell and the connected viewer state");
                Undo.PerformRedo();
                Check(level.GetCell(3, 3) == LevelCell.Floor && viewer.playerPosition == new Vector2(3.5f, 3.5f) &&
                      viewer.yawDegrees == 45, "Redo restores the start brush and connected viewer state");

                level.roomHeight = 4f;
                level.tileSize = 1.5f;
                level.wallTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/DoomSpriteProjection/SchoolAssets/Wall.png");
                Texture2D savedTexture = level.wallTexture;
                Check(savedTexture != null && EditorSceneManager.SaveScene(level.gameObject.scene, scenePath),
                    "The isolated test scene saves with a persistent texture reference");
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
                UnityEngine.Object.DestroyImmediate(levelEditor);
                levelEditor = null;
                Undo.ClearAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.OpenScene(scenePath);
                level = UnityEngine.Object.FindFirstObjectByType<ProjectionLevel>();
                Check(level != null && level.width == 4 && level.height == 4 &&
                      level.GetCell(1, 1) == LevelCell.Floor && level.GetCell(2, 1) == LevelCell.Door &&
                      level.GetCell(3, 3) == LevelCell.Floor && level.GetCell(0, 0) == LevelCell.Wall,
                    "Wall, floor and door data survive saving and reopening the scene");
                Check(level.playerStart == new Vector2(3.5f, 3.5f) && level.playerYaw == 45 &&
                      level.viewer != null && level.viewer.playerPosition == level.playerStart,
                    "The saved start pose and viewer reference survive scene serialization");
                Check(level.roomHeight == 4f && level.tileSize == 1.5f && level.wallTexture == savedTexture,
                    "Room settings and the selected wall image survive scene serialization");
                success = true;
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL: " + error);
            }
            finally
            {
                if (levelEditor != null) UnityEngine.Object.DestroyImmediate(levelEditor);
                Undo.ClearAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }

            report.AppendLine($"Result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            File.WriteAllText(Path.Combine(logDirectory, "ProjectionLevelChecks.txt"), report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool Near(float actual, float expected) =>
            !float.IsNaN(actual) && !float.IsInfinity(actual) && Mathf.Abs(actual - expected) <= 0.001f;

        private static float ClosestBoxHit(ProjectionLevel level, Vector2 start, Vector2 direction)
        {
            float closest = float.PositiveInfinity;
            for (int y = -1; y <= level.height; y++)
            for (int x = -1; x <= level.width; x++)
            {
                if (level.GetCell(x, y) == LevelCell.Floor) continue;
                Vector2 corner = level.origin + new Vector2(x * level.cellSize, y * level.cellSize);
                float entry = 0f, exit = float.PositiveInfinity;
                if (ClipAxis(start.x, direction.x, corner.x, corner.x + level.cellSize, ref entry, ref exit) &&
                    ClipAxis(start.y, direction.y, corner.y, corner.y + level.cellSize, ref entry, ref exit))
                    closest = Mathf.Min(closest, entry);
            }
            return closest;
        }

        private static bool ClipAxis(float start, float direction, float minimum, float maximum,
            ref float entry, ref float exit)
        {
            if (Mathf.Abs(direction) < 0.0000001f) return start >= minimum && start <= maximum;
            float first = (minimum - start) / direction;
            float second = (maximum - start) / direction;
            entry = Mathf.Max(entry, Mathf.Min(first, second));
            exit = Mathf.Min(exit, Mathf.Max(first, second));
            return entry <= exit;
        }
    }
}
