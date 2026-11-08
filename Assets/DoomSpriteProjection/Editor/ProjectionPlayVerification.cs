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
    public static class ProjectionPlayVerification
    {
        private const string ActiveKey = "DoomProjection.PlayVerification.Active";
        private const string StartKey = "DoomProjection.PlayVerification.Start";
        private static readonly StringBuilder Report = new StringBuilder("Actual Play Mode checks\n");
        private static readonly string[] Captures = { "default", "near", "replacement" };
        private static SpriteProjectionDemo demo;
        private static SpriteMapObject item;
        private static SpriteMapObject otherItem;
        private static Image otherImage;
        private static Canvas canvas;
        private static Image image;
        private static Sprite originalSprite;
        private static Sprite replacementSprite;
        private static Texture2D replacementTexture;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode oldInputBehavior;
        private static bool changedInputBehavior;
        private static int stage;
        private static int nextFrame;
        private static double nextTime;
        private static int passed;

        private static string LogDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));

        static ProjectionPlayVerification()
        {
            if (SessionState.GetBool(ActiveKey, false))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            Directory.CreateDirectory(LogDirectory);
            foreach (string name in Captures)
            {
                string path = ScreenshotPath(name);
                if (File.Exists(path)) File.Delete(path);
            }
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
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
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0) > 60)
                    throw new Exception("60 second timeout while waiting for Play Mode frames.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling ||
                    Time.frameCount < nextFrame || EditorApplication.timeSinceStartup < nextTime)
                    return;

                switch (stage)
                {
                    case 0:
                        if (Time.frameCount < 3) return;
                        demo = UnityEngine.Object.FindFirstObjectByType<SpriteProjectionDemo>();
                        Require(demo != null, "Demo component loaded from SampleScene");
                        canvas = GameObject.Find("Projection Canvas").GetComponent<Canvas>();
                        Require(demo.map != null, "Demo reads a scene map");
                        SpriteMapObject[] objects = demo.map.GetObjects();
                        Require(objects.Length >= 3, "Sample map contains several placed sprites");
                        item = Array.Find(objects, obj => obj.MapPosition == new Vector2(0, 5));
                        Require(item != null, "Reference object is at map position (0, 5)");
                        otherItem = Array.Find(objects, obj => obj != item);
                        image = ImageFor(item);
                        otherImage = ImageFor(otherItem);
                        originalSprite = image.sprite;
                        Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.transform.parent == null,
                            "Overlay canvas exists at the scene root");
                        Require(image.enabled && originalSprite != null, "Default sprite is visible");
                        MatchDefaultRect(1f);
                        Capture("default");
                        Advance(8, 0.2);
                        break;
                    case 1:
                        demo.playerPosition = new Vector2(0, 2.5f);
                        Advance();
                        break;
                    case 2:
                        MatchDefaultRect(2f);
                        Capture("near");
                        Advance(8, 0.2);
                        break;
                    case 3:
                        demo.ResetView();
                        replacementTexture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
                        replacementTexture.filterMode = FilterMode.Point;
                        replacementTexture.SetPixels(new[] { Color.cyan, Color.magenta });
                        replacementTexture.Apply();
                        replacementSprite = Sprite.Create(replacementTexture, new Rect(0, 0, 2, 1), Vector2.zero);
                        item.SetSprite(replacementSprite);
                        Advance();
                        break;
                    case 4:
                        Require(image.sprite == replacementSprite && image.enabled, "SetSprite changes the visible image");
                        Near(image.rectTransform.sizeDelta.x / image.rectTransform.sizeDelta.y, 2f);
                        Near(image.rectTransform.sizeDelta.y, Screen.width * 0.2f);
                        Near(item.objectHeight, 2f);
                        Pass("Replacement keeps 2:1 aspect ratio and world height");
                        Capture("replacement");
                        Advance(8, 0.2);
                        break;
                    case 5:
                        item.SetSprite(null);
                        Advance();
                        break;
                    case 6:
                        Require(image.sprite == originalSprite && image.enabled, "SetSprite(null) restores the default");
                        MatchDefaultRect(1f);
                        demo.playerPosition = new Vector2(1, 0);
                        Advance();
                        break;
                    case 7:
                        Near(image.rectTransform.anchoredPosition.x + image.rectTransform.sizeDelta.x * 0.5f,
                            Screen.width * 0.4f);
                        Pass("Player moving right moves the image left on the next frame");
                        demo.playerPosition = Vector2.zero;
                        demo.yawDegrees = 90;
                        item.SetMapPosition(new Vector2(5, 0));
                        Advance();
                        break;
                    case 8:
                        MatchDefaultRect(1f);
                        Pass("90 degree yaw projects the new forward direction correctly");
                        item.SetMapPosition(new Vector2(0, 5));
                        demo.yawDegrees = 180;
                        Advance();
                        break;
                    case 9:
                        Require(!image.enabled, "Object behind the player is hidden");
                        demo.playerPosition = new Vector2(3, 3);
                        demo.eyeHeight = 3;
                        demo.ResetView();
                        Advance();
                        break;
                    case 10:
                        Require(demo.playerPosition == Vector2.zero && demo.yawDegrees == 0 && demo.eyeHeight == 1,
                            "ResetView restores the starting player state");
                        MatchDefaultRect(1f);
                        demo.enabled = false;
                        Advance();
                        break;
                    case 11:
                        Require(!canvas.gameObject.activeSelf, "Disabling the demo hides its canvas");
                        demo.enabled = true;
                        Advance();
                        break;
                    case 12:
                        Require(canvas.gameObject.activeSelf && image.enabled, "Re-enabling restores the canvas");
                        oldInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
                        changedInputBehavior = true;
                        InputSystem.settings.editorInputBehaviorInPlayMode =
                            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        keyboard = InputSystem.AddDevice<Keyboard>("Projection Verification Keyboard");
                        keyboard.MakeCurrent();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        Advance(4, 0.05);
                        break;
                    case 13:
                        Require(demo.playerPosition.y > 0 && Mathf.Abs(demo.playerPosition.x) < 0.001f,
                            "Real InputSystem W key moves forward");
                        demo.ResetView();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                        Advance(4, 0.05);
                        break;
                    case 14:
                        Require(demo.playerPosition.x > 0 && Mathf.Abs(demo.playerPosition.y) < 0.001f,
                            "Real InputSystem D key strafes right");
                        demo.ResetView();
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                        Advance(4, 0.05);
                        break;
                    case 15:
                        Require(demo.yawDegrees > 0 && demo.yawDegrees < 180,
                            "Real InputSystem E key turns clockwise");
                        CleanupInput();
                        demo.ResetView();
                        Advance(8, 0.2);
                        break;
                    case 16:
                        item.SetMapPosition(new Vector2(0, 4));
                        otherItem.SetMapPosition(new Vector2(0, 8));
                        item.objectHeight = otherItem.objectHeight = 2f;
                        otherItem.SetSprite(null);
                        Advance();
                        break;
                    case 17:
                        Require(image.enabled && otherImage.enabled, "Two overlapping map objects are projected");
                        Require(image.transform.GetSiblingIndex() > otherImage.transform.GetSiblingIndex(),
                            "Near sprite draws after the far sprite");
                        Near(image.rectTransform.sizeDelta.y, otherImage.rectTransform.sizeDelta.y * 2f);
                        Require(image.transform.parent.GetSiblingIndex() > canvas.transform.Find("Horizon").GetSiblingIndex(),
                            "Sprite sorting stays in a container above the backgrounds");
                        item.SetMapPosition(new Vector2(0, 8));
                        otherItem.SetMapPosition(new Vector2(0, 4));
                        Advance();
                        break;
                    case 18:
                        Require(otherImage.transform.GetSiblingIndex() > image.transform.GetSiblingIndex(),
                            "Moving placed objects reverses their draw order");
                        demo.playerPosition = new Vector2(0, 12);
                        demo.yawDegrees = 180;
                        Advance();
                        break;
                    case 19:
                        Require(image.enabled && otherImage.enabled &&
                            image.transform.GetSiblingIndex() > otherImage.transform.GetSiblingIndex(),
                            "Turning around from the opposite side reverses depth order again");
                        demo.ResetView();
                        item.SetMapPosition(new Vector2(0, 4));
                        otherItem.SetMapPosition(new Vector2(0.5f, 4));
                        Advance();
                        break;
                    case 20:
                        Require((image.transform.GetSiblingIndex() < otherImage.transform.GetSiblingIndex()) ==
                            (item.transform.GetSiblingIndex() < otherItem.transform.GetSiblingIndex()),
                            "Equal-depth objects retain their map order");
                        item.enabled = false;
                        Advance();
                        break;
                    case 21:
                        Require(!image.enabled, "Disabling a map object component hides its image");
                        item.enabled = true;
                        item.gameObject.SetActive(false);
                        Advance();
                        break;
                    case 22:
                        Require(!image.enabled, "Inactive map objects are hidden");
                        item.gameObject.SetActive(true);
                        UnityEngine.Object.Destroy(otherItem.gameObject);
                        Advance();
                        break;
                    case 23:
                        Require(image.enabled && (otherImage == null || !otherImage.enabled),
                            "Reactivated objects return and deleted objects stop drawing");
                        otherItem = demo.map.AddObject(new Vector2(0, 7));
                        otherItem.name = "Runtime Added Verification Sprite";
                        demo.RefreshMap();
                        Advance();
                        break;
                    case 24:
                        Require(ImageFor(otherItem).enabled, "RefreshMap displays a newly added object");
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

        private static void MatchDefaultRect(float magnification)
        {
            Require(image.enabled, "Projected image enabled");
            RectTransform rect = image.rectTransform;
            Near(rect.sizeDelta.x, Screen.width * 0.15f * magnification);
            Near(rect.sizeDelta.y, Screen.width * 0.2f * magnification);
            Near(rect.anchoredPosition.x, Screen.width * 0.5f - rect.sizeDelta.x * 0.5f);
            Near(rect.anchoredPosition.y, Screen.height * 0.5f - Screen.width * 0.1f * magnification);
            Pass("Actual UI rectangle matches the independent pixel expectation (x" + magnification + ")");
        }

        private static Image ImageFor(SpriteMapObject target)
        {
            Transform node = canvas.transform.Find("Sprites/Projected Sprite: " + target.name);
            if (node == null) throw new Exception("Missing UI image for map object: " + target.name);
            return node.GetComponent<Image>();
        }

        private static void Advance(int frames = 2, double seconds = 0)
        {
            stage++;
            nextFrame = Time.frameCount + frames;
            nextTime = EditorApplication.timeSinceStartup + seconds;
        }

        private static string ScreenshotPath(string name) =>
            Path.Combine(LogDirectory, "SpriteProjection-" + name + ".png");

        private static void Capture(string name)
        {
            try { ScreenCapture.CaptureScreenshot(ScreenshotPath(name)); }
            catch (Exception error) { Report.AppendLine("CAPTURE unavailable: " + name + ": " + error.Message); }
        }

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
            if (replacementSprite != null) UnityEngine.Object.Destroy(replacementSprite);
            if (replacementTexture != null) UnityEngine.Object.Destroy(replacementTexture);
            foreach (string name in Captures)
            {
                string path = ScreenshotPath(name);
                bool saved = File.Exists(path) && new FileInfo(path).Length > 100;
                Report.AppendLine(saved ? "CAPTURE saved: " + path :
                    "CAPTURE not produced by this Editor graphics session: " + name);
            }
            Report.AppendLine($"Runtime result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            Report.AppendLine("Screenshot availability is reported separately from the runtime assertions.");
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(Path.Combine(LogDirectory, "SpriteProjectionPlayChecks.txt"), Report.ToString());
            Debug.Log(Report.ToString());
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
