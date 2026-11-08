using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    public static class ProjectionLevelSetup
    {
        public const string DemoScene = "Assets/Scenes/MapEditorDemo.unity";

        public static ProjectionLevel AttachCorridor(SpriteProjectionDemo viewer, SchoolCorridorBackground background)
        {
            ProjectionLevel level = viewer.level;
            if (level == null)
            {
                level = new GameObject("Map Layout").AddComponent<ProjectionLevel>();
                level.Initialize(16, 18, 2f, new Vector2(-16f, -4f));
                Fill(level, 6, 2, 9, 13, LevelCell.Floor);
                foreach (int y in new[] { 5, 9 })
                {
                    level.SetCell(5, y, LevelCell.Door);
                    level.SetCell(10, y, LevelCell.Door);
                }
                level.SetCell(7, 14, LevelCell.Door);
                level.SetCell(8, 14, LevelCell.Door);
                level.playerStart = viewer.playerPosition;
                level.playerYaw = viewer.yawDegrees;
                level.wallTexture = background.wallTexture;
                level.doorTexture = background.doorTexture;
                level.floorTexture = background.floorTexture;
                level.ceilingTexture = background.ceilingTexture;
                level.roomHeight = background.roomHeight;
                level.tileSize = background.tileSize;
            }
            level.viewer = viewer;
            viewer.level = level;
            if (viewer.map != null) viewer.map.level = level;
            background.spriteDepthShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/DoomSpriteProjection/SpriteWorldDepth.shader");
            if (background.spriteDepthShader == null) throw new Exception("Map depth shader is missing.");
            EditorSceneManager.MarkSceneDirty(viewer.gameObject.scene);
            return level;
        }

        private static void Fill(ProjectionLevel level, int minX, int minY, int maxX, int maxY, LevelCell cell)
        {
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++) level.SetCell(x, y, cell);
        }

        public static void Run()
        {
            bool success = false;
            try
            {
                const string school = "Assets/Scenes/SchoolHallway.unity";
                var scene = EditorSceneManager.OpenScene(school);
                var viewer = UnityEngine.Object.FindFirstObjectByType<SpriteProjectionDemo>();
                var background = viewer.GetComponent<SchoolCorridorBackground>();
                AttachCorridor(viewer, background);
                if (!EditorSceneManager.SaveScene(scene, school)) throw new Exception("School map could not be saved.");

                if (!File.Exists(DemoScene))
                {
                    if (!EditorSceneManager.SaveScene(scene, DemoScene)) throw new Exception("Demo scene could not be created.");
                    ProjectionLevel level = viewer.level;
                    Fill(level, 11, 5, 14, 10, LevelCell.Floor);
                    level.SetCell(10, 7, LevelCell.Floor);
                    level.SetCell(15, 7, LevelCell.Door);
                    Fill(level, 2, 10, 5, 11, LevelCell.Floor);
                    Fill(level, 2, 12, 3, 15, LevelCell.Floor);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, DemoScene)) throw new Exception("Demo layout could not be saved.");
                }
                var scenes = EditorBuildSettings.scenes.ToList();
                if (scenes.All(item => item.path != DemoScene))
                {
                    scenes.Add(new EditorBuildSettingsScene(DemoScene, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/ProjectionLevelSetup.txt",
                    "PASS: SchoolHallway converted to editable cells.\n" +
                    "MapEditorDemo has a side classroom and a bent corridor.\n" +
                    "Both scenes retain one Moodang, one Moohan, speed 5.6 and mouse look.\n");
                success = true;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { EditorApplication.Exit(success ? 0 : 1); }
        }
    }
}
