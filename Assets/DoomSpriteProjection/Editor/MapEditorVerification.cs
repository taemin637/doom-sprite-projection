using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    public static class MapEditorVerification
    {
        public static void Run()
        {
            var report = new StringBuilder("Sprite map editor checks\n");
            int passed = 0;
            bool success = false;
            string logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(logDirectory);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/DoomSpriteProjection/Editor/SpriteMapVerification.unity");

            void Check(bool condition, string description)
            {
                if (!condition) throw new Exception(description);
                report.AppendLine("PASS: " + description);
                passed++;
            }

            void Near(float actual, float expected)
            {
                if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > 0.001f)
                    throw new Exception($"Expected {expected}, got {actual}.");
            }

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var map = new GameObject("Verification Map").AddComponent<SpriteMap>();
                Sprite brush = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                Check(brush != null, "Persistent built-in sprite is available as a test brush");
                map.placementSprite = brush;
                map.placementHeight = 3f;
                map.placementBaseHeight = 0.5f;
                map.snapToGrid = true;
                map.gridSize = 1f;

                Undo.IncrementCurrentGroup();
                int createGroup = Undo.GetCurrentGroup();
                SpriteMapObject placed = SpriteMapEditor.PlaceObject(map, new Vector2(0.6f, 4.6f));
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(createGroup);
                Check(placed != null && placed.transform.parent == map.transform, "Placement creates a map child");
                Check(placed.MapPosition == new Vector2(1, 5), "Placement snaps to the nearest grid point");
                Check(placed.sourceSprite == brush, "Placement copies the selected sprite");
                Near(placed.objectHeight, 3f);
                Near(placed.baseHeight, 0.5f);
                Check(map.gameObject.scene.isDirty, "Placement marks the scene dirty");
                Check(map.GetObjects().Length == 1, "Map collection contains the placed object");

                Undo.PerformUndo();
                Check(map.GetObjects().Length == 0, "Undo removes the placed object");
                Undo.PerformRedo();
                Check(map.GetObjects().Length == 1, "Redo restores the placed object");
                placed = map.GetObjects()[0];

                map.snapToGrid = false;
                Vector2 unsnapped = new Vector2(-0.3f, 2.7f);
                Check(map.SnapPosition(unsnapped) == unsnapped, "Disabling snap preserves arbitrary coordinates");
                map.snapToGrid = true;
                map.gridSize = 0.5f;
                Check(map.SnapPosition(new Vector2(-0.6f, 2.8f)) == new Vector2(-0.5f, 3f),
                    "Fractional grid size also snaps negative coordinates");

                Undo.IncrementCurrentGroup();
                int moveGroup = Undo.GetCurrentGroup();
                Undo.RecordObjects(new UnityEngine.Object[] { placed, placed.transform }, "Move map object");
                placed.SetMapPosition(new Vector2(-2, 6), 1.25f);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(moveGroup);
                Check(placed.MapPosition == new Vector2(-2, 6), "Moving a transform changes its map coordinates");
                Near(placed.baseHeight, 1.25f);
                Undo.PerformUndo();
                Check(placed.MapPosition == new Vector2(1, 5), "Undo restores position data");
                Near(placed.baseHeight, 0.5f);
                Undo.PerformRedo();
                Check(placed.MapPosition == new Vector2(-2, 6), "Redo restores the moved position");

                Undo.IncrementCurrentGroup();
                var duplicate = UnityEngine.Object.Instantiate(placed, map.transform);
                duplicate.name = "Verification Duplicate";
                Undo.RegisterCreatedObjectUndo(duplicate.gameObject, "Duplicate map object");
                duplicate.SetMapPosition(new Vector2(3, 9), 2f);
                duplicate.objectHeight = 1.5f;
                Check(map.GetObjects().Length == 2 && duplicate.sourceSprite == placed.sourceSprite,
                    "Duplicating preserves sprite data and adds another map object");

                Undo.IncrementCurrentGroup();
                Undo.DestroyObjectImmediate(duplicate.gameObject);
                Check(map.GetObjects().Length == 1, "Deleting removes the map object");
                Undo.PerformUndo();
                Check(map.GetObjects().Length == 2, "Undo restores a deleted map object");

                Check(EditorSceneManager.SaveScene(map.gameObject.scene, scenePath), "Test scene saves outside SampleScene");
                AssetDatabase.ImportAsset(scenePath);
                File.Copy(scenePath, Path.Combine(logDirectory, "SpriteMapVerification.unity"), true);
                Undo.ClearAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.OpenScene(scenePath);
                map = UnityEngine.Object.FindFirstObjectByType<SpriteMap>();
                Check(map != null && map.GetObjects().Length == 2, "Saved map objects survive reopening the scene");
                SpriteMapObject[] restored = map.GetObjects();
                SpriteMapObject original = Array.Find(restored, obj => obj.name != "Verification Duplicate");
                duplicate = Array.Find(restored, obj => obj.name == "Verification Duplicate");
                Check(original != null && duplicate != null, "Object names and separate instances survive serialization");
                Check(original.MapPosition == new Vector2(-2, 6) && duplicate.MapPosition == new Vector2(3, 9),
                    "Reopened map preserves both positions");
                Near(original.baseHeight, 1.25f);
                Near(original.objectHeight, 3f);
                Near(duplicate.baseHeight, 2f);
                Near(duplicate.objectHeight, 1.5f);
                Check(original.sourceSprite == brush && duplicate.sourceSprite == brush,
                    "Reopened objects preserve their sprite references");
                Check(map.placementSprite == brush && map.snapToGrid, "Brush and snap settings are saved with the map");
                Near(map.gridSize, 0.5f);
                success = true;
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL: " + error);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }

            report.AppendLine($"Result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            File.WriteAllText(Path.Combine(logDirectory, "SpriteMapEditorChecks.txt"), report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
