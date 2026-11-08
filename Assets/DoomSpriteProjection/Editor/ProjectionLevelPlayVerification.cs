using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace DoomProjection
{
    [InitializeOnLoad]
    public static class ProjectionLevelPlayVerification
    {
        private const string ActiveKey = "DoomProjection.LevelPlayVerification.Active";
        private const string StartKey = "DoomProjection.LevelPlayVerification.Start";
        private static readonly StringBuilder Report = new StringBuilder("Editable map Play Mode and GPU checks\n");
        private static readonly string[] Captures = {
            "MapEditorDemo-preview", "LevelMap-open", "LevelMap-blocked", "LevelMap-partial", "LevelMap-pitched-open"
        };
        private static SpriteProjectionDemo viewer;
        private static SchoolCorridorBackground background;
        private static ProjectionLevel level;
        private static SpriteMapObject sample;
        private static Texture2D sampleTexture;
        private static Sprite sampleSprite;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode oldInputBehavior;
        private static bool changedInputBehavior;
        private static int stage, nextFrame, passed, openCount, previousRenderCount;
        private static double nextTime;
        private static string LogDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));

        static ProjectionLevelPlayVerification()
        {
            if (SessionState.GetBool(ActiveKey, false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                foreach (string name in Captures)
                    if (File.Exists(CapturePath(name))) File.Delete(CapturePath(name));
                EditorSceneManager.OpenScene("Assets/Scenes/MapEditorDemo.unity");
                Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
                    gameView.position = new Rect(0, 0, 960, 580);
                    gameView.Show();
                    gameView.Focus();
                }
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                SessionState.SetBool(ActiveKey, true);
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            {
                Report.AppendLine("FAIL while opening the scene: " + error);
                Finish(false);
            }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > 120)
                    throw new Exception("120 second Play Mode timeout.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling ||
                    Time.frameCount < nextFrame || EditorApplication.timeSinceStartup < nextTime)
                    return;

                switch (stage)
                {
                    case 0:
                        if (Time.frameCount < 8) return;
                        viewer = UnityEngine.Object.FindFirstObjectByType<SpriteProjectionDemo>();
                        background = UnityEngine.Object.FindFirstObjectByType<SchoolCorridorBackground>();
                        Require(viewer != null && background != null && viewer.level != null,
                            "Editable map scene connects a viewer, renderer and level");
                        level = viewer.level;
                        Require(background.spriteDepthShader != null && background.spriteDepthShader.isSupported,
                            "The real UI depth shader is assigned and supported");
                        Require(viewer.map != null && viewer.map.GetObjects().Length == 2,
                            "Demo keeps one Moodang and one Moohan");
                        Require(level.CanOccupy(viewer.playerPosition), "Demo starts on a walkable floor tile");
                        Capture("MapEditorDemo-preview");
                        Advance(8, 0.3);
                        break;
                    case 1:
                        Require(File.Exists(CapturePath("MapEditorDemo-preview")),
                            "The edited room and corridor example produces a real Game screenshot");
                        PrepareFixture();
                        Advance(5);
                        break;
                    case 2:
                        Require(background.DepthTexture != null &&
                            background.DepthTexture.width == background.RenderedTexture.width &&
                            background.DepthTexture.height == background.RenderedTexture.height,
                            "Rendered colors and camera depths have matching dimensions");
                        Image sampleImage = viewer.DisplayCanvas.transform.Find("Sprites/Projected Sprite: " + sample.name)
                            .GetComponent<Image>();
                        Require(sampleImage.enabled && sampleImage.material != null &&
                            sampleImage.material.shader == background.spriteDepthShader &&
                            sampleImage.material.GetTexture("_WorldDepthTex") == background.DepthTexture,
                            "The projected sprite is actually drawn with the world depth texture");
                        Capture("LevelMap-open");
                        Advance(8, 0.3);
                        break;
                    case 3:
                        PixelCounts open = ReadMagenta("LevelMap-open");
                        openCount = open.total;
                        Require(openCount > 1000, "Unobstructed magenta sprite is visible in GPU output: " + openCount);
                        previousRenderCount = background.RenderCount;
                        PaintBarrier(false);
                        Advance(5);
                        break;
                    case 4:
                        Require(background.RenderCount > previousRenderCount && viewer.playerPosition == new Vector2(0, 2),
                            "Painting a wall updates the renderer while the player stays still");
                        float centerDepth = background.DepthTexture.GetPixel(background.DepthTexture.width / 2,
                            background.DepthTexture.height / 2).r;
                        Require(Mathf.Abs(centerDepth - 3f) < 0.05f,
                            "The front wall writes camera depth three, in front of the depth-six sprite");
                        Capture("LevelMap-blocked");
                        Advance(8, 0.3);
                        break;
                    case 5:
                        PixelCounts blocked = ReadMagenta("LevelMap-blocked");
                        Require(blocked.total == 0,
                            "A complete wall hides every sprite pixel in actual GPU output");
                        previousRenderCount = background.RenderCount;
                        PaintBarrier(true);
                        Advance(5);
                        break;
                    case 6:
                        Require(background.RenderCount > previousRenderCount,
                            "Removing half of the wall redraws the current view");
                        Capture("LevelMap-partial");
                        Advance(8, 0.3);
                        break;
                    case 7:
                        PixelCounts partial = ReadMagenta("LevelMap-partial");
                        Require(partial.total > openCount * 0.3f && partial.total < openCount * 0.7f,
                            "Half a wall leaves about half the sprite visible: " + partial.total + " / " + openCount);
                        Require(partial.left == 0 && partial.right > 100,
                            "GPU output hides the left sprite half while keeping the right half");
                        PaintBarrier(false);
                        viewer.playerPosition = new Vector2(0, 4.4f);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Advance(10, 0.35);
                        break;
                    case 8:
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        Require(viewer.playerPosition.y >= 4.5f && viewer.playerPosition.y <= 4.801f &&
                            level.CanOccupy(viewer.playerPosition),
                            "Actual forward input stops before the painted wall, including player radius");
                        viewer.yawDegrees = 37;
                        viewer.pitchDegrees = 89;
                        previousRenderCount = background.RenderCount;
                        Advance(5);
                        break;
                    case 9:
                        Require(background.RenderCount > previousRenderCount,
                            "Yaw and pitch changes redraw the edited map");
                        ValidateDepths("upward +89-degree view");
                        viewer.pitchDegrees = -89;
                        Advance(5);
                        break;
                    case 10:
                        ValidateDepths("downward -89-degree view");
                        for (int x = 0; x < level.width; x++) level.SetCell(x, 5, LevelCell.Floor);
                        viewer.playerPosition = new Vector2(0, 2);
                        viewer.yawDegrees = 0;
                        viewer.pitchDegrees = -20;
                        Advance(5);
                        break;
                    case 11:
                        Capture("LevelMap-pitched-open");
                        Advance(8, 0.3);
                        break;
                    case 12:
                        PixelCounts pitched = ReadMagenta("LevelMap-pitched-open");
                        Require(pitched.total > openCount * 0.8f && pitched.total < openCount * 1.2f,
                            "A downward view keeps the unobstructed sprite visible, checking depth texture vertical UV orientation");
                        Finish(true);
                        break;
                }
            }
            catch (Exception error)
            {
                Report.AppendLine("FAIL at stage " + stage + ": " + error);
                Finish(false);
            }
        }

        private static void PrepareFixture()
        {
            level.Initialize(12, 12, 1f, new Vector2(-6, 0), LevelCell.Floor);
            level.roomHeight = 3.4f;
            viewer.playerPosition = new Vector2(0, 2);
            viewer.yawDegrees = 0;
            viewer.pitchDegrees = 0;
            viewer.eyeHeight = 1.4f;
            viewer.mouseLookEnabled = false;
            viewer.SetMouseLookActive(false);
            SpriteMapObject[] items = viewer.map.GetObjects();
            sample = items[0];
            for (int i = 1; i < items.Length; i++) items[i].gameObject.SetActive(false);
            sampleTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            sampleTexture.filterMode = FilterMode.Point;
            Color32[] pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255);
            sampleTexture.SetPixels32(pixels);
            sampleTexture.Apply();
            sampleSprite = Sprite.Create(sampleTexture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0));
            sample.SetSprite(sampleSprite);
            sample.SetMapPosition(new Vector2(0, 8), 0);
            sample.objectHeight = 2;
            oldInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            changedInputBehavior = true;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("Editable Map Verification Keyboard");
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        private static void PaintBarrier(bool leftHalfOnly)
        {
            for (int x = 0; x < level.width; x++)
                level.SetCell(x, 5, leftHalfOnly && x >= 6 ? LevelCell.Floor : LevelCell.Wall);
        }

        private struct PixelCounts { public int total, left, right; }

        private static PixelCounts ReadMagenta(string name)
        {
            string path = CapturePath(name);
            if (!File.Exists(path)) throw new Exception("Missing actual screenshot: " + path);
            Texture2D capture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!capture.LoadImage(File.ReadAllBytes(path))) throw new Exception("Cannot decode " + path);
                Color32[] pixels = capture.GetPixels32();
                PixelCounts counts = default;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 color = pixels[i];
                    if (color.r < 220 || color.b < 220 || color.g > 40) continue;
                    counts.total++;
                    if (i % capture.width < capture.width / 2) counts.left++;
                    else counts.right++;
                }
                Report.AppendLine($"PIXELS {name}: {capture.width}x{capture.height}; magenta={counts.total}, left={counts.left}, right={counts.right}");
                return counts;
            }
            finally { UnityEngine.Object.Destroy(capture); }
        }

        private static void ValidateDepths(string description)
        {
            var depths = background.DepthTexture.GetPixelData<float>(0);
            for (int i = 0; i < depths.Length; i++)
                if (float.IsNaN(depths[i]) || float.IsInfinity(depths[i]) || depths[i] < 0 || depths[i] >= 1e19f)
                    throw new Exception("Invalid world depth at pixel " + i + " for " + description);
            Pass("All map pixels have finite surface depths in the " + description);
        }

        private static string CapturePath(string name) => Path.Combine(LogDirectory, name + ".png");
        private static void Capture(string name) => ScreenCapture.CaptureScreenshot(CapturePath(name));

        private static void Advance(int frames = 3, double seconds = 0)
        {
            stage++;
            nextFrame = Time.frameCount + frames;
            nextTime = EditorApplication.timeSinceStartup + seconds;
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            Pass(description);
        }

        private static void Pass(string description)
        {
            passed++;
            Report.AppendLine("PASS: " + description);
        }

        private static void Finish(bool success)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            if (viewer != null) viewer.SetMouseLookActive(false);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (changedInputBehavior) InputSystem.settings.editorInputBehaviorInPlayMode = oldInputBehavior;
            if (sampleSprite != null) UnityEngine.Object.Destroy(sampleSprite);
            if (sampleTexture != null) UnityEngine.Object.Destroy(sampleTexture);
            Report.AppendLine($"Runtime result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(Path.Combine(LogDirectory, "ProjectionLevelPlayChecks.txt"), Report.ToString());
            Debug.Log(Report.ToString());
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
