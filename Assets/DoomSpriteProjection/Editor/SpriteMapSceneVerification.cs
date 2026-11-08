using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    public static class SpriteMapSceneVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static readonly FieldInfo PlacingField = typeof(SpriteMapEditor).GetField(
            "placing", BindingFlags.Instance | BindingFlags.NonPublic);
        private static SpriteMap map;
        private static SceneView view;
        private static SpriteMapEditor inspector;
        private static Vector2 click;
        private static Vector2 expectedPosition;
        private static bool observedMouseDown;
        private static string guiError;
        private static int stage;
        private static int passed;
        private static int repaintCount;
        private static int sceneEvents;
        private static double started;
        private static double nextTime;
        private static Tool previousTool;

        public static void Run()
        {
            Report.Clear();
            Report.AppendLine("Actual Scene view click placement checks");
            stage = passed = repaintCount = sceneEvents = 0;
            guiError = null;
            started = EditorApplication.timeSinceStartup;
            previousTool = Tools.current;
            Application.logMessageReceived += OnLog;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                map = new GameObject("Scene Click Verification Map").AddComponent<SpriteMap>();
                map.placementSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                map.placementHeight = 3f;
                map.placementBaseHeight = 0.5f;
                map.gridSize = 1f;
                map.snapToGrid = true;
                var demo = new GameObject("Scene Click Verification Player").AddComponent<SpriteProjectionDemo>();
                demo.map = map;

                Type inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
                if (inspectorType != null) EditorWindow.GetWindow(inspectorType).Show();
                Selection.activeGameObject = map.gameObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                view = EditorWindow.GetWindow<SceneView>();
                view.position = new Rect(50, 50, 960, 700);
                view.in2DMode = true;
                view.drawGizmos = true;
                view.wantsMouseMove = true;
                view.wantsLessLayoutEvents = false;
                view.Show();
                view.Frame(new Bounds(new Vector3(0, 4, 0), new Vector3(12, 12, 1)), true);
                view.Focus();
                Tools.current = Tool.None;
                SceneView.beforeSceneGui += ObserveScene;
                EditorApplication.update += Tick;
                nextTime = started + 0.5;
                view.Repaint();
            }
            catch (Exception error)
            {
                Report.AppendLine("SETUP FAIL: " + error);
                Finish(false);
            }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - started > 45)
                    throw new Exception("45 second timeout. Scene GUI callbacks=" + sceneEvents +
                        ", repaints=" + repaintCount + ". This batch Editor may not route visible Scene view events.");
                if (guiError != null) throw new Exception(guiError);
                if (EditorApplication.timeSinceStartup < nextTime) return;

                switch (stage)
                {
                    case 0:
                        inspector = FindMapInspector();
                        if (inspector == null || repaintCount == 0)
                        {
                            view.Repaint();
                            return;
                        }
                        Require(PlacingField != null, "Placement mode field is available to this test");
                        PlacingField.SetValue(inspector, true);
                        Require(Selection.activeGameObject == map.gameObject, "Real Inspector tracks the selected map");
                        click = new Vector2(view.position.width * 0.55f, view.position.height * 0.45f);
                        SendMouse(EventType.MouseMove, click);
                        Advance();
                        break;
                    case 1:
                        observedMouseDown = false;
                        Undo.IncrementCurrentGroup();
                        SendClick(click);
                        Advance();
                        break;
                    case 2:
                        Require(map.GetObjects().Length == 1, "SceneView MouseDown creates one map object");
                        Require(observedMouseDown, "MouseDown reached the Scene GUI event pipeline");
                        SpriteMapObject item = map.GetObjects()[0];
                        Require(Vector2.Distance(item.MapPosition, expectedPosition) < 0.001f,
                            "Created position matches the clicked XY plane point after grid snap");
                        Require(Mathf.Abs(item.transform.position.z) < 0.001f,
                            "Placed object is on the XY map plane");
                        Require(item.sourceSprite == map.placementSprite && item.objectHeight == 3f && item.baseHeight == 0.5f,
                            "Actual click copies the selected brush sprite and height values");
                        Require(Selection.activeGameObject == map.gameObject,
                            "Map stays selected for repeated placement");
                        click += new Vector2(100, 65);
                        SendMouse(EventType.MouseMove, click);
                        Advance();
                        break;
                    case 3:
                        Undo.IncrementCurrentGroup();
                        observedMouseDown = false;
                        SendClick(click);
                        Advance();
                        break;
                    case 4:
                        Require(map.GetObjects().Length == 2 && observedMouseDown,
                            "A second Scene view click places another object without reselecting the map");
                        Require(Vector2.Distance(map.GetObjects()[1].MapPosition, expectedPosition) < 0.001f,
                            "Second click also uses its own snapped map position");
                        Undo.PerformUndo();
                        Advance();
                        break;
                    case 5:
                        Require(map.GetObjects().Length == 1, "Undo removes the last object created by a Scene view click");
                        inspector = FindMapInspector();
                        Require(inspector != null, "Selected map inspector remains available after Undo");
                        PlacingField.SetValue(inspector, true);
                        view.Focus();
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                        view.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape });
                        Advance();
                        break;
                    case 6:
                        Require(!(bool)PlacingField.GetValue(inspector), "Scene view Escape key ends placement mode");
                        click += new Vector2(80, 40);
                        SendMouse(EventType.MouseMove, click);
                        Advance();
                        break;
                    case 7:
                        SendClick(click);
                        Advance();
                        break;
                    case 8:
                        Require(map.GetObjects().Length == 1, "Click after Escape does not place another object");
                        Require(repaintCount > 0 && guiError == null, "Scene view rendering completed without GUI exceptions");
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

        private static SpriteMapEditor FindMapInspector()
        {
            foreach (Editor editor in ActiveEditorTracker.sharedTracker.activeEditors)
                if (editor is SpriteMapEditor mapEditor && editor.target == map) return mapEditor;
            return null;
        }

        private static void ObserveScene(SceneView currentView)
        {
            if (currentView != view || Event.current == null) return;
            sceneEvents++;
            Event current = Event.current;
            if (current.type == EventType.Repaint) repaintCount++;
            if (current.rawType != EventType.MouseDown || current.button != 0) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 point = ray.GetPoint(distance);
            expectedPosition = map.SnapPosition(new Vector2(point.x, point.y));
            observedMouseDown = true;
        }

        private static void SendMouse(EventType type, Vector2 position)
        {
            view.SendEvent(new Event { type = type, mousePosition = position, button = 0, clickCount = 1 });
        }

        private static void SendClick(Vector2 position)
        {
            SendMouse(EventType.MouseDown, position);
            SendMouse(EventType.MouseUp, position);
        }

        private static void Advance()
        {
            stage++;
            nextTime = EditorApplication.timeSinceStartup + 0.35;
            view.Repaint();
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            passed++;
            Report.AppendLine("PASS: " + description);
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert ||
                (type == LogType.Error && (stack.Contains("SpriteMap") || message.Contains("GUI"))))
                guiError = message + "\n" + stack;
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            SceneView.beforeSceneGui -= ObserveScene;
            Application.logMessageReceived -= OnLog;
            Tools.current = previousTool;
            Report.AppendLine($"Scene events: {sceneEvents}; repaints: {repaintCount}.");
            Report.AppendLine($"Result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            Report.AppendLine("No SampleScene changes were saved. Placement was tested through SceneView.SendEvent.");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SpriteMapSceneChecks.txt"), Report.ToString());
            Debug.Log(Report.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
