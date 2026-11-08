using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace DoomProjection
{
    public static class SchoolHallwaySetup
    {
        private const string AssetFolder = "Assets/DoomSpriteProjection/SchoolAssets";
        private const string SourceScene = "Assets/Scenes/SampleScene.unity";
        private const string OutputScene = "Assets/Scenes/SchoolHallway.unity";

        public static void Run()
        {
            var report = new StringBuilder("School hallway setup\n");
            bool success = false;
            try
            {
                Directory.CreateDirectory(AssetFolder);
                Texture2D door = ImportImage("레트로 픽셀 한국 교실 미닫이문-3.png", "ClassroomDoor", false, report);
                Texture2D ceiling = ImportImage("따뜻한 형광등의 학교 천장-2.png", "Ceiling", false, report);
                Texture2D wall = ImportImage("아이보리와 민트빛 학교 복도 벽-1.png", "Wall", false, report);
                Texture2D floor = ImportImage("밝은 학교 복도 테라조 타일-4.png", "Floor", false, report);
                Texture2D moodangTexture = ImportImage("Gachon_Moodang_Front.png", "Moodang", true, report);
                Texture2D moohanTexture = ImportImage("Gachon_Moohan_Front.png", "Moohan", true, report);
                Sprite moodang = SliceCharacter(moodangTexture, "Moodang", new Rect(589, 161, 220, 316), report);
                Sprite moohan = SliceCharacter(moohanTexture, "Moohan", new Rect(348, 70, 689, 1256), report);

                BuildScene(moodang, moohan, wall, door, floor, ceiling, report);
                AssetDatabase.SaveAssets();
                report.AppendLine("PASS: school hallway scene saved; source PNG pixels and SampleScene preserved.");
                success = true;
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                string logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(Path.Combine(logDirectory, "SchoolHallwaySetup.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log(report.ToString());
                EditorApplication.Exit(success ? 0 : 1);
            }
        }

        private static Texture2D ImportImage(string sourceName, string assetName, bool character, StringBuilder report)
        {
            string sourcePath = Path.Combine("D:/Downloads", sourceName);
            string assetPath = $"{AssetFolder}/{assetName}.png";
            File.Copy(sourcePath, assetPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = character ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = character ? SpriteImportMode.Multiple : SpriteImportMode.None;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = character || assetName == "ClassroomDoor" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = character;
            if (character)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                throw new InvalidOperationException("Could not import " + assetPath);
            if (!File.ReadAllBytes(sourcePath).SequenceEqual(File.ReadAllBytes(assetPath)))
                throw new InvalidOperationException("Imported PNG does not match its source: " + assetPath);
            report.AppendLine($"Imported {assetPath}: {texture.width} x {texture.height}; PNG copied without pixel changes.");
            return texture;
        }

        private static Sprite SliceCharacter(Texture2D texture, string name, Rect expectedRect, StringBuilder report)
        {
            Color32[] pixels = texture.GetPixels32();
            int left = texture.width, bottom = texture.height, right = -1, top = -1;
            int visiblePixels = 0;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a == 0)
                        continue;
                    left = Mathf.Min(left, x);
                    right = Mathf.Max(right, x);
                    bottom = Mathf.Min(bottom, y);
                    top = Mathf.Max(top, y);
                    visiblePixels++;
                }
            }
            if (visiblePixels == 0)
                throw new InvalidOperationException(name + " has no visible pixels.");
            var crop = new Rect(left, bottom, right - left + 1, top - bottom + 1);
            if (crop != expectedRect)
                throw new InvalidOperationException($"Unexpected alpha bounds for {name}: {crop}; expected {expectedRect}.");

            string assetPath = AssetDatabase.GetAssetPath(texture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            SpriteRect previous = provider.GetSpriteRects().FirstOrDefault(rect => rect.name == name);
            GUID id = previous != null ? previous.spriteID : GUID.Generate();
            var spriteRect = new SpriteRect
            {
                name = name,
                spriteID = id,
                rect = crop,
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f)
            };
            provider.SetSpriteRects(new[] { spriteRect });
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
                .SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(name, id) });
            provider.Apply();
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Single();
            if (sprite.rect != crop || Mathf.Abs(sprite.pivot.y) > 0.001f)
                throw new InvalidOperationException("Sprite crop or bottom pivot was not applied: " + name);
            report.AppendLine($"{name}: alpha > 0 pixels={visiblePixels}; bottom-left crop={crop}; pivot={sprite.pivot}; original texture retained.");
            return sprite;
        }

        private static void BuildScene(Sprite moodang, Sprite moohan, Texture2D wall, Texture2D door,
            Texture2D floor, Texture2D ceiling, StringBuilder report)
        {
            byte[] originalScene = File.ReadAllBytes(SourceScene);
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, OutputScene))
                throw new InvalidOperationException("Could not create " + OutputScene);

            SpriteProjectionDemo viewer = UnityEngine.Object.FindFirstObjectByType<SpriteProjectionDemo>();
            SpriteMap map = viewer != null ? viewer.map : null;
            if (map == null)
                throw new InvalidOperationException("SampleScene is missing its projection viewer or map.");
            int removedCount = map.transform.childCount;
            for (int i = map.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(map.transform.GetChild(i).gameObject);
            map.name = "School Sprite Map";
            map.snapToGrid = false;
            map.placementBaseHeight = 0f;
            Place(map, moodang, "Moodang Welcome", new Vector2(-1.6f, 6f), 1.25f);
            Place(map, moohan, "Moohan Welcome", new Vector2(1.5f, 8f), 1.8f);
            map.placementSprite = moodang;
            map.placementHeight = 1.3f;

            viewer.playerPosition = new Vector2(0f, 2f);
            viewer.yawDegrees = 0f;
            viewer.eyeHeight = 1.4f;
            viewer.horizontalFov = 85f;
            viewer.moveSpeed = 5.6f;
            viewer.pitchDegrees = 0f;
            viewer.mouseLookEnabled = true;
            var background = viewer.gameObject.AddComponent<SchoolCorridorBackground>();
            background.viewer = viewer;
            background.wallTexture = wall;
            background.doorTexture = door;
            background.floorTexture = floor;
            background.ceilingTexture = ceiling;
            background.floorBounds = new Rect(-4f, 0f, 8f, 24f);
            background.roomHeight = 3.4f;
            background.tileSize = 3f;
            background.doorWidth = 2.6f;
            background.doorHeight = 2.8f;
            background.doorCenters = new[] { 8f, 16f };
            background.renderWidth = 480;
            ProjectionLevelSetup.AttachCorridor(viewer, background);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, OutputScene))
                throw new InvalidOperationException("Could not save school hallway scene.");
            if (!originalScene.SequenceEqual(File.ReadAllBytes(SourceScene)))
                throw new InvalidOperationException("SampleScene unexpectedly changed.");

            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (buildScenes.All(entry => entry.path != OutputScene))
            {
                buildScenes.Add(new EditorBuildSettingsScene(OutputScene, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }
            report.AppendLine($"Saved {OutputScene}; replaced {removedCount} demo children with {map.GetObjects().Length} mascots.");
            foreach (SpriteMapObject item in map.GetObjects())
                report.AppendLine($"  {item.name}: map={item.MapPosition}, base={item.baseHeight}, height={item.objectHeight}, sprite={item.sourceSprite.name}");
        }

        private static void Place(SpriteMap map, Sprite sprite, string name, Vector2 position, float height)
        {
            map.placementSprite = sprite;
            map.placementHeight = height;
            SpriteMapObject item = map.AddObject(position);
            item.name = name;
        }
    }
}
