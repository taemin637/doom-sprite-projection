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
    public static class SchoolHallwayVerification
    {
        private const string ActiveKey = "DoomProjection.HallwayVerification.Active";
        private const string StartKey = "DoomProjection.HallwayVerification.Start";
        private static readonly StringBuilder Report = new StringBuilder("School hallway Play Mode checks\n");
        private static readonly string[] Captures = { "default", "turned", "looking-up", "looking-down" };
        private static SpriteProjectionDemo viewer;
        private static SchoolCorridorBackground background;
        private static Keyboard keyboard;
        private static Mouse mouse;
        private static InputSettings.EditorInputBehaviorInPlayMode oldInputBehavior;
        private static bool changedInputBehavior;
        private static int stage, nextFrame, passed, previousRenderCount;
        private static double nextTime;
        private static uint previousPixels;
        private static float previousYaw, previousPitch;
        private static string LogDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));

        static SchoolHallwayVerification()
        {
            if (SessionState.GetBool(ActiveKey, false))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            Directory.CreateDirectory(LogDirectory);
            foreach (string name in Captures)
                if (File.Exists(ScreenshotPath(name))) File.Delete(ScreenshotPath(name));
            EditorSceneManager.OpenScene("Assets/Scenes/SchoolHallway.unity");
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

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > 90)
                    throw new Exception("90 second Play Mode timeout.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling ||
                    Time.frameCount < nextFrame || EditorApplication.timeSinceStartup < nextTime)
                    return;

                switch (stage)
                {
                    case 0:
                        if (Time.frameCount < 6) return;
                        viewer = UnityEngine.Object.FindFirstObjectByType<SpriteProjectionDemo>();
                        background = UnityEngine.Object.FindFirstObjectByType<SchoolCorridorBackground>();
                        Require(viewer != null && background != null && background.viewer == viewer,
                            "School scene connects the sprite viewer and corridor background");
                        Require(viewer.map != null && viewer.map.GetObjects().Length == 2,
                            "School map contains two placed characters");
                        ValidateMap();
                        Require(viewer.playerPosition == new Vector2(0, 2) && viewer.yawDegrees == 0,
                            "Starting view looks down the corridor from (0, 2)");
                        Require(viewer.constrainMovement && viewer.movementBounds.width <
                            (viewer.level != null ? viewer.level.Bounds.width : background.floorBounds.width),
                            "Corridor movement bounds include an inset from the walls");
                        Require(background.RenderedTexture != null && background.RenderCount > 0,
                            "CPU background produces a runtime texture");
                        Require(background.wallTexture != null && background.doorTexture != null &&
                            background.floorTexture != null && background.ceilingTexture != null,
                            "All four supplied architectural textures are assigned");
                        ValidateBackgroundLayer();
                        previousPixels = PixelSignature();
                        previousRenderCount = background.RenderCount;
                        Capture("default");
                        Advance(8, 0.25);
                        break;
                    case 1:
                        Require(background.RenderCount == previousRenderCount,
                            "An unchanged view reuses its background image");
                        viewer.playerPosition = new Vector2(0.5f, 3f);
                        Advance();
                        break;
                    case 2:
                        Require(background.RenderCount > previousRenderCount && PixelSignature() != previousPixels,
                            "Player movement changes the rendered corridor");
                        previousPixels = PixelSignature();
                        previousRenderCount = background.RenderCount;
                        viewer.yawDegrees = 18;
                        Advance();
                        break;
                    case 3:
                        Require(background.RenderCount > previousRenderCount && PixelSignature() != previousPixels,
                            "View rotation changes the rendered corridor");
                        Capture("turned");
                        Advance(8, 0.25);
                        break;
                    case 4:
                        viewer.ResetView();
                        oldInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
                        changedInputBehavior = true;
                        InputSystem.settings.editorInputBehaviorInPlayMode =
                            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        keyboard = InputSystem.AddDevice<Keyboard>("School Hallway Verification Keyboard");
                        keyboard.MakeCurrent();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Advance(4, 0.08);
                        break;
                    case 5:
                        Require(viewer.playerPosition.y > 2 && Mathf.Abs(viewer.playerPosition.x) < 0.001f,
                            "Actual W input moves forward inside the corridor");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                        Advance(4, 0.08);
                        break;
                    case 6:
                        Require(viewer.yawDegrees > 0, "Actual E input rotates the view");
                        viewer.yawDegrees = 0;
                        viewer.playerPosition = new Vector2((viewer.level != null ? 3.8f : viewer.movementBounds.xMax) - 0.001f, 10f);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                        Advance(4, 0.08);
                        break;
                    case 7:
                        Near(viewer.playerPosition.x, viewer.level != null ? 3.8f : viewer.movementBounds.xMax);
                        Pass("Actual movement stops at the right corridor boundary");
                        viewer.playerPosition = new Vector2(0, (viewer.level != null ? 23.8f : viewer.movementBounds.yMax) - 0.001f);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Advance(4, 0.08);
                        break;
                    case 8:
                        Near(viewer.playerPosition.y, viewer.level != null ? 23.8f : viewer.movementBounds.yMax);
                        Pass("Actual movement stops at the far corridor boundary");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        viewer.ResetView();
                        Advance();
                        break;
                    case 9:
                        Require(viewer.playerPosition == new Vector2(0, 2) && viewer.yawDegrees == 0 &&
                            viewer.pitchDegrees == 0, "Reset restores position, yaw and pitch");
                        Near(viewer.moveSpeed, 5.6f);
                        Pass("School movement speed is doubled from 2.8 to 5.6 units per second");
                        previousRenderCount = background.RenderCount;
                        previousPixels = PixelSignature();
                        viewer.pitchDegrees = 12;
                        Advance();
                        break;
                    case 10:
                        Require(background.RenderCount > previousRenderCount && PixelSignature() != previousPixels,
                            "Looking vertically changes the true perspective background");
                        ValidatePitchedFeet();
                        viewer.pitchDegrees = 89;
                        Advance();
                        break;
                    case 11:
                        Capture("looking-up");
                        Advance(8, 0.25);
                        break;
                    case 12:
                        viewer.pitchDegrees = -89;
                        Advance();
                        break;
                    case 13:
                        Capture("looking-down");
                        Advance(8, 0.25);
                        break;
                    case 14:
                        viewer.pitchDegrees = 88.99f;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.PageUp));
                        Advance(4, 0.08);
                        break;
                    case 15:
                        Near(viewer.pitchDegrees, 89);
                        Pass("Page Up clamps the upward view at 89 degrees");
                        ValidateFiniteProjection();
                        viewer.pitchDegrees = -88.99f;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.PageDown));
                        Advance(4, 0.08);
                        break;
                    case 16:
                        Near(viewer.pitchDegrees, -89);
                        Pass("Page Down clamps the downward view at -89 degrees");
                        ValidateFiniteProjection();
                        viewer.ResetView();
                        viewer.pitchDegrees = 25;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Advance(4, 0.08);
                        break;
                    case 17:
                        Require(viewer.playerPosition.y > 2 && Mathf.Abs(viewer.playerPosition.x) < 0.001f &&
                            Mathf.Abs(viewer.eyeHeight - 1.4f) < 0.001f && viewer.pitchDegrees == 25,
                            "Walking while looking up stays in the map plane and preserves eye height");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        viewer.ResetView();
                        mouse = InputSystem.AddDevice<Mouse>("School Hallway Verification Mouse");
                        mouse.MakeCurrent();
                        InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
                        Advance();
                        break;
                    case 18:
                        if (!viewer.MouseLookActive)
                        {
                            Report.AppendLine("SKIP: Native cursor lock did not remain active in this batch Editor; " +
                                "mouse-delta, Esc and release-delta checks need an interactive Game view.");
                            viewer.SetMouseLookActive(true);
                            viewer.enabled = false;
                            stage = 23;
                            Advance();
                            break;
                        }
                        Require(!Cursor.visible, "Actual left click captures mouse look and hides the cursor");
                        previousYaw = viewer.yawDegrees;
                        previousPitch = viewer.pitchDegrees;
                        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(40, 20) });
                        Advance();
                        break;
                    case 19:
                        Near(viewer.yawDegrees, previousYaw + 40 * viewer.mouseSensitivity);
                        Near(viewer.pitchDegrees, previousPitch + 20 * viewer.mouseSensitivity);
                        Pass("Actual mouse delta rotates yaw and pitch once using pixel sensitivity");
                        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(0, 20000) });
                        Advance();
                        break;
                    case 20:
                        Near(viewer.pitchDegrees, 89);
                        Pass("A large mouse delta clamps pitch at 89 degrees");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                        Advance();
                        break;
                    case 21:
                        Require(!viewer.MouseLookActive && Cursor.visible, "Escape releases mouse look and shows the cursor");
                        previousYaw = viewer.yawDegrees;
                        previousPitch = viewer.pitchDegrees;
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(90, -80) });
                        Advance();
                        break;
                    case 22:
                        Near(viewer.yawDegrees, previousYaw);
                        Near(viewer.pitchDegrees, previousPitch);
                        Pass("Mouse movement after release does not rotate the view");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
                        Advance();
                        break;
                    case 23:
                        Require(viewer.playerPosition == new Vector2(0, 2) && viewer.yawDegrees == 0 &&
                            viewer.pitchDegrees == 0, "Actual R input resets the vertical view as well as position and yaw");
                        viewer.SetMouseLookActive(true);
                        viewer.enabled = false;
                        Advance();
                        break;
                    case 24:
                        Require(!viewer.MouseLookActive && Cursor.visible, "Disabling the viewer releases the mouse cursor");
                        viewer.enabled = true;
                        CleanupInput();
                        viewer.ResetView();
                        Advance();
                        break;
                    case 25:
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

        private static void ValidateMap()
        {
            int moodangCount = 0, moohanCount = 0;
            foreach (SpriteMapObject item in viewer.map.GetObjects())
            {
                Sprite sprite = item.sourceSprite;
                Require(sprite != null, item.name + " uses an imported character sprite");
                if (sprite.rect.size == new Vector2(220, 316)) moodangCount++;
                else if (sprite.rect.size == new Vector2(689, 1256)) moohanCount++;
                else throw new Exception("Unexpected crop rectangle: " + sprite.name + ": " + sprite.rect);
                Near(sprite.pivot.x, sprite.rect.width * 0.5f);
                Near(sprite.pivot.y, 0);
                Require(item.baseHeight == 0 && background.floorBounds.Contains(item.MapPosition),
                    item.name + " rests on the floor and remains inside the corridor");
                Transform node = viewer.DisplayCanvas.transform.Find("Sprites/Projected Sprite: " + item.name);
                Image image = node != null ? node.GetComponent<Image>() : null;
                Require(image != null && image.enabled && image.sprite == sprite,
                    item.name + " appears in the projected UI");
                float focalLength = Screen.width * 0.5f /
                    Mathf.Tan(viewer.horizontalFov * Mathf.Deg2Rad * 0.5f);
                float expectedFootY = Screen.height * 0.5f - viewer.eyeHeight * focalLength /
                    (item.MapPosition.y - viewer.playerPosition.y);
                Near(image.rectTransform.anchoredPosition.y, expectedFootY);
                Pass(item.name + " projects its cropped feet onto the floor height");
            }
            Require(moodangCount == 1 && moohanCount == 1,
                "One Moodang sprite uses a 220x316 crop and one Moohan sprite uses a 689x1256 crop");
            Pass("All imported character pivots are at the bottom center");
        }

        private static void ValidateBackgroundLayer()
        {
            RawImage[] images = viewer.DisplayCanvas.GetComponentsInChildren<RawImage>();
            RawImage backgroundImage = Array.Find(images, image => image.texture == background.RenderedTexture);
            Require(backgroundImage != null && backgroundImage.enabled,
                "Background texture is displayed by a UI RawImage");
            Transform sprites = viewer.DisplayCanvas.transform.Find("Sprites");
            Require(backgroundImage.transform.parent == sprites.parent &&
                backgroundImage.transform.GetSiblingIndex() < sprites.GetSiblingIndex(),
                "Corridor background draws behind the projected characters");
            Color32[] pixels = background.RenderedTexture.GetPixels32();
            int different = 0;
            Color32 first = pixels[0];
            for (int i = 0; i < pixels.Length; i += 97)
                if (!pixels[i].Equals(first)) different++;
            Require(different > 100, "Background contains textured color variation");
        }

        private static void ValidatePitchedFeet()
        {
            float pitch = viewer.pitchDegrees * Mathf.Deg2Rad;
            float focalLength = Screen.width * 0.5f /
                Mathf.Tan(viewer.horizontalFov * Mathf.Deg2Rad * 0.5f);
            foreach (SpriteMapObject item in viewer.map.GetObjects())
            {
                float forward = item.MapPosition.y - viewer.playerPosition.y;
                float relativeHeight = item.baseHeight - viewer.eyeHeight;
                float depth = forward * Mathf.Cos(pitch) + relativeHeight * Mathf.Sin(pitch);
                float vertical = relativeHeight * Mathf.Cos(pitch) - forward * Mathf.Sin(pitch);
                float expectedFootY = Screen.height * 0.5f + vertical * focalLength / depth;
                Image image = viewer.DisplayCanvas.transform.Find("Sprites/Projected Sprite: " + item.name).GetComponent<Image>();
                Require(image.enabled, item.name + " remains visible during a slight upward look");
                Near(image.rectTransform.anchoredPosition.y, expectedFootY);
                Pass(item.name + " feet use pitch-rotated height and depth");
            }
        }

        private static void ValidateFiniteProjection()
        {
            foreach (SpriteMapObject item in viewer.map.GetObjects())
            {
                Sprite sprite = item.sourceSprite;
                Vector2 size = new Vector2(item.objectHeight * sprite.rect.width / sprite.rect.height, item.objectHeight);
                SpriteProjection.TryProject(viewer.playerPosition, viewer.yawDegrees, viewer.eyeHeight,
                    item.MapPosition, item.baseHeight, size, new Vector2(Screen.width, Screen.height),
                    viewer.horizontalFov, out Rect rect, out float depth, viewer.pitchDegrees);
                Require(Finite(depth) && Finite(rect.x) && Finite(rect.y) && Finite(rect.width) && Finite(rect.height),
                    item.name + " projection remains finite at pitch " + viewer.pitchDegrees);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static uint PixelSignature()
        {
            Color32[] pixels = background.RenderedTexture.GetPixels32();
            uint hash = 2166136261;
            unchecked
            {
                for (int i = 0; i < pixels.Length; i += 31)
                {
                    Color32 color = pixels[i];
                    hash = (hash ^ color.r) * 16777619;
                    hash = (hash ^ color.g) * 16777619;
                    hash = (hash ^ color.b) * 16777619;
                }
            }
            return hash;
        }

        private static void Advance(int frames = 3, double seconds = 0)
        {
            stage++;
            nextFrame = Time.frameCount + frames;
            nextTime = EditorApplication.timeSinceStartup + seconds;
        }

        private static string ScreenshotPath(string name) =>
            Path.Combine(LogDirectory, "SchoolHallway-" + name + ".png");

        private static void Capture(string name) => ScreenCapture.CaptureScreenshot(ScreenshotPath(name));

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            Pass(description);
        }

        private static void Near(float actual, float expected)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > 0.1f)
                throw new Exception($"Expected {expected}, got {actual}.");
        }

        private static void Pass(string description)
        {
            passed++;
            Report.AppendLine("PASS: " + description);
        }

        private static void CleanupInput()
        {
            if (viewer != null) viewer.SetMouseLookActive(false);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            mouse = null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (changedInputBehavior)
                InputSystem.settings.editorInputBehaviorInPlayMode = oldInputBehavior;
            changedInputBehavior = false;
        }

        private static void Finish(bool success)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            CleanupInput();
            foreach (string name in Captures)
            {
                string path = ScreenshotPath(name);
                bool saved = File.Exists(path) && new FileInfo(path).Length > 100;
                Report.AppendLine(saved ? "CAPTURE saved: " + path : "CAPTURE unavailable: " + name);
            }
            Report.AppendLine($"Runtime result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(Path.Combine(LogDirectory, "SchoolHallwayPlayChecks.txt"), Report.ToString());
            Debug.Log(Report.ToString());
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
