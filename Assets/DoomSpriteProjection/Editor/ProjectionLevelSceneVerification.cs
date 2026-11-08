using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    public static class ProjectionLevelSceneVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static readonly FieldInfo EditingField = typeof(ProjectionLevelEditor).GetField("editing", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BrushField = typeof(ProjectionLevelEditor).GetField("brush", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector2Int[] Targets = {
            new Vector2Int(1, 2), new Vector2Int(5, 2), new Vector2Int(3, 2),
            new Vector2Int(4, 2), new Vector2Int(2, 4), new Vector2Int(6, 5)
        };
        private static readonly Vector2[] GuiPoints = new Vector2[Targets.Length];
        private static ProjectionLevel level;
        private static SpriteProjectionDemo viewer;
        private static SceneView view;
        private static ProjectionLevelEditor inspector;
        private static Vector2Int observedDown, observedDrag;
        private static Vector2 observedDownPoint, observedDragPoint, observedMovePoint;
        private static bool sawDown, sawDrag, sawMove;
        private static int stage, passed, repaintCount, eventCount;
        private static double started, nextTime;
        private static string guiError;
        private static Tool previousTool;
        private static Vector2 originalStart;

        public static void Run()
        {
            Report.Clear();
            Report.AppendLine("Actual Scene GUI level painting checks");
            stage = passed = repaintCount = eventCount = 0;
            guiError = null;
            started = EditorApplication.timeSinceStartup;
            previousTool = Tools.current;
            Application.logMessageReceived += OnLog;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                level = new GameObject("Scene GUI Layout Verification").AddComponent<ProjectionLevel>();
                level.Initialize(8, 8, 2f, new Vector2(-8, -8));
                level.playerStart = originalStart = new Vector2(5, 5);
                level.playerYaw = 35;
                viewer = new GameObject("Scene GUI Viewer Verification").AddComponent<SpriteProjectionDemo>();
                viewer.playerPosition = originalStart;
                viewer.level = level;
                level.viewer = viewer;
                viewer.map = new GameObject("Scene GUI Sprite Map Verification").AddComponent<SpriteMap>();
                viewer.map.level = level;

                Type inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
                if (inspectorWindowType != null) EditorWindow.GetWindow(inspectorWindowType).Show();
                Selection.activeGameObject = level.gameObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                view = EditorWindow.GetWindow<SceneView>();
                view.position = new Rect(50, 50, 960, 700);
                view.in2DMode = true;
                view.drawGizmos = true;
                view.wantsMouseMove = true;
                view.wantsLessLayoutEvents = false;
                view.Show();
                ProjectionLevelEditor.FrameLevel(level);
                view.Frame(new Bounds(Vector3.zero, new Vector3(20f, 20f, 1f)), true);
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
                if (EditorApplication.timeSinceStartup - started > 90)
                    throw new Exception($"90 second timeout; GUI events={eventCount}, repaints={repaintCount}, stage={stage}.");
                if (guiError != null) throw new Exception(guiError);
                if (EditorApplication.timeSinceStartup < nextTime) return;
                switch (stage)
                {
                    case 0:
                        inspector = FindInspector();
                        if (inspector == null || repaintCount == 0) { view.Repaint(); return; }
                        Require(EditingField != null && BrushField != null, "Test can select the editing mode and brush");
                        EditingField.SetValue(inspector, true);
                        SetBrush(0);
                        Require(Selection.activeGameObject == level.gameObject, "Inspector tracks the selected level layout");
                        CalibrateGuiPoints();
                        SendMouse(EventType.MouseMove, GuiPoints[0]);
                        Advance();
                        break;
                    case 1:
                        CalibrateGuiPoints();
                        sawDown = sawDrag = false;
                        SendMouse(EventType.MouseDown, GuiPoints[0]);
                        SendMouse(EventType.MouseDrag, GuiPoints[1]);
                        SendMouse(EventType.MouseUp, GuiPoints[1]);
                        Advance();
                        break;
                    case 2:
                        Require(sawDown && sawDrag, "MouseDown and MouseDrag passed through the Scene GUI pipeline");
                        Report.AppendLine($"STROKE coordinates: sent down={GuiPoints[0]}, drag={GuiPoints[1]}; " +
                            $"observed down={observedDownPoint} / cell {observedDown} (expected {Targets[0]}), " +
                            $"drag={observedDragPoint} / cell {observedDrag} (expected {Targets[1]}).");
                        Require(observedDown == Targets[0] && observedDrag == Targets[1], "Mouse coordinates intersect the intended XY grid cells");
                        Require(HasFloorStroke(), "A real drag paints every cell between its endpoints without gaps");
                        Require(Count(LevelCell.Floor) == 5, "A one-cell brush changes only the intended five cells");
                        Require(level.gameObject.scene.isDirty, "Scene GUI painting marks the scene as changed");
                        Require(Selection.activeGameObject == level.gameObject, "Layout remains selected after a stroke");
                        Undo.FlushUndoRecordObjects();
                        Undo.PerformUndo();
                        Advance();
                        break;
                    case 3:
                        Require(Count(LevelCell.Floor) == 0, "One Undo restores the entire drag stroke");
                        Undo.PerformRedo();
                        Advance();
                        break;
                    case 4:
                        Require(HasFloorStroke() && Count(LevelCell.Floor) == 5, "Redo restores the complete stroke");
                        inspector = FindInspector();
                        Require(inspector != null, "Layout Inspector is available after Undo and Redo");
                        EditingField.SetValue(inspector, true);
                        SetBrush(1);
                        CalibrateGuiPoints();
                        SendClick(GuiPoints[2]);
                        Advance();
                        break;
                    case 5:
                        Require(level.GetCell(3, 2) == LevelCell.Wall && Count(LevelCell.Floor) == 4,
                            "The wall brush closes an existing floor cell through a real click");
                        SetBrush(2);
                        CalibrateGuiPoints();
                        SendClick(GuiPoints[3]);
                        Advance();
                        break;
                    case 6:
                        Require(level.GetCell(4, 2) == LevelCell.Door && Count(LevelCell.Door) == 1,
                            "The door brush records a door cell through a real click");
                        SetBrush(0);
                        CalibrateGuiPoints();
                        SendClick(GuiPoints[5], EventModifiers.Alt);
                        Advance();
                        break;
                    case 7:
                        Require(level.GetCell(6, 5) == LevelCell.Wall, "Alt navigation does not paint the map");
                        SetBrush(3);
                        CalibrateGuiPoints();
                        SendClick(GuiPoints[4]);
                        Advance();
                        break;
                    case 8:
                        Require(level.GetCell(2, 4) == LevelCell.Floor, "A start-position click creates a walkable floor cell");
                        Require(Vector2.Distance(level.playerStart, level.CellCenter(2, 4)) < 0.001f,
                            "A start-position click stores the chosen cell center");
                        Require(viewer.playerPosition == level.playerStart && Mathf.Abs(viewer.yawDegrees - 35) < 0.001f,
                            "The start-position click also updates the viewer position and heading");
                        Undo.FlushUndoRecordObjects();
                        Undo.PerformUndo();
                        Advance();
                        break;
                    case 9:
                        Require(level.GetCell(2, 4) == LevelCell.Wall && level.playerStart == originalStart && viewer.playerPosition == originalStart,
                            "One Undo restores both the start cell and the viewer position");
                        Undo.PerformRedo();
                        Advance();
                        break;
                    case 10:
                        Require(level.GetCell(2, 4) == LevelCell.Floor && viewer.playerPosition == level.CellCenter(2, 4),
                            "Redo restores the start placement and viewer position");
                        inspector = FindInspector();
                        Require(inspector != null, "Inspector remains available before ending edit mode");
                        EditingField.SetValue(inspector, true);
                        view.Focus();
                        view.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                        view.SendEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape });
                        Advance();
                        break;
                    case 11:
                        Require(!(bool)EditingField.GetValue(inspector), "Escape ends map painting mode");
                        CalibrateGuiPoints();
                        SendClick(GuiPoints[5]);
                        Advance();
                        break;
                    case 12:
                        Require(level.GetCell(6, 5) == LevelCell.Wall, "Clicking after Escape leaves the layout unchanged");
                        Require(repaintCount > 0 && guiError == null, "The grid and Scene GUI render without exceptions");
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

        private static ProjectionLevelEditor FindInspector()
        {
            foreach (Editor editor in ActiveEditorTracker.sharedTracker.activeEditors)
                if (editor is ProjectionLevelEditor candidate && editor.target == level) return candidate;
            return null;
        }

        private static void SetBrush(int value) => BrushField.SetValue(inspector, Enum.ToObject(BrushField.FieldType, value));

        private static void ObserveScene(SceneView currentView)
        {
            if (currentView != view || Event.current == null) return;
            eventCount++;
            Event current = Event.current;
            if (current.type == EventType.Repaint) repaintCount++;
            if (current.rawType != EventType.MouseMove && current.rawType != EventType.MouseDown && current.rawType != EventType.MouseDrag) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance)) return;
            Vector3 pointOnMap = ray.GetPoint(distance);
            if (current.rawType == EventType.MouseMove)
            {
                sawMove = true;
                observedMovePoint = new Vector2(pointOnMap.x, pointOnMap.y);
                return;
            }
            if (current.button != 0) return;
            Vector2Int cell = level.WorldToCell(new Vector2(pointOnMap.x, pointOnMap.y));
            if (current.rawType == EventType.MouseDown)
            {
                sawDown = true;
                observedDown = cell;
                observedDownPoint = new Vector2(pointOnMap.x, pointOnMap.y);
            }
            else
            {
                sawDrag = true;
                observedDrag = cell;
                observedDragPoint = new Vector2(pointOnMap.x, pointOnMap.y);
            }
        }

        private static void CalibrateGuiPoints()
        {
            Vector2 a = new Vector2(view.position.width * 0.35f, view.position.height * 0.35f);
            Vector2 b = a + new Vector2(100f, 0f);
            Vector2 c = a + new Vector2(0f, 100f);
            Vector2 worldA = ObserveMove(a), worldB = ObserveMove(b), worldC = ObserveMove(c);
            Vector2 dx = worldB - worldA, dy = worldC - worldA;
            float determinant = dx.x * dy.y - dx.y * dy.x;
            if (Mathf.Abs(determinant) < 0.00001f)
                throw new Exception($"Scene GUI calibration is singular: A={worldA}, B={worldB}, C={worldC}.");
            for (int i = 0; i < Targets.Length; i++)
            {
                Vector2 relative = level.CellCenter(Targets[i].x, Targets[i].y) - worldA;
                float u = (relative.x * dy.y - relative.y * dy.x) / determinant;
                float v = (dx.x * relative.y - dx.y * relative.x) / determinant;
                GuiPoints[i] = a + (b - a) * u + (c - a) * v;
            }
            Report.AppendLine($"GUI calibration at stage {stage}: GUI {a}/{b}/{c} => XY {worldA}/{worldB}/{worldC}.");
        }

        private static Vector2 ObserveMove(Vector2 position)
        {
            sawMove = false;
            SendMouse(EventType.MouseMove, position);
            if (!sawMove) throw new Exception("Scene view did not synchronously dispatch the calibration MouseMove.");
            return observedMovePoint;
        }

        private static void SendMouse(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None) =>
            view.SendEvent(new Event { type = type, mousePosition = position, button = 0, clickCount = 1, modifiers = modifiers });

        private static void SendClick(Vector2 position, EventModifiers modifiers = EventModifiers.None)
        {
            SendMouse(EventType.MouseDown, position, modifiers);
            SendMouse(EventType.MouseUp, position, modifiers);
        }

        private static bool HasFloorStroke()
        {
            for (int x = 1; x <= 5; x++) if (level.GetCell(x, 2) != LevelCell.Floor) return false;
            return true;
        }

        private static int Count(LevelCell type)
        {
            int count = 0;
            for (int y = 0; y < level.height; y++)
                for (int x = 0; x < level.width; x++) if (level.GetCell(x, y) == type) count++;
            return count;
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
                (type == LogType.Error && (stack.Contains("ProjectionLevel") || message.Contains("GUI"))))
                guiError = message + "\n" + stack;
        }

        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick;
            SceneView.beforeSceneGui -= ObserveScene;
            Application.logMessageReceived -= OnLog;
            Tools.current = previousTool;
            Report.AppendLine($"Scene events: {eventCount}; repaints: {repaintCount}.");
            Report.AppendLine($"Result: {(success ? "PASS" : "FAIL")}; {passed} assertions passed.");
            if (!success && level != null)
            {
                Report.AppendLine("Actual cells (top row first; W=wall, .=floor, D=door):");
                for (int y = level.height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < level.width; x++)
                    {
                        LevelCell cell = level.GetCell(x, y);
                        Report.Append(cell == LevelCell.Wall ? 'W' : cell == LevelCell.Floor ? '.' : 'D');
                    }
                    Report.AppendLine();
                }
            }
            Report.AppendLine("An unsaved test scene was used. Production scene files were not written.");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ProjectionLevelSceneChecks.txt"), Report.ToString());
            Debug.Log(Report.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
