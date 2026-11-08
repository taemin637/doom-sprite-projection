using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    [CustomEditor(typeof(ProjectionLevel))]
    public class ProjectionLevelEditor : Editor
    {
        private enum Brush { Floor, Wall, Door, Start }
        private static readonly string[] BrushNames = { "바닥 (방·통로)", "벽", "문", "시작 위치" };
        private static readonly Vector3[] CellCorners = new Vector3[4];
        private Brush brush;
        private int brushSize = 1;
        private bool editing, dragging, showNewMap;
        private int strokeGroup = -1, activeControl;
        private Vector2Int previousCell;
        private Tool previousTool;
        private int newWidth, newHeight;
        private float newCellSize;
        private Vector2 newOrigin;

        private void OnEnable()
        {
            ProjectionLevel level = (ProjectionLevel)target;
            newWidth = level.width;
            newHeight = level.height;
            newCellSize = level.cellSize;
            newOrigin = level.origin;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            ProjectionLevel level = (ProjectionLevel)target;
            if (Application.isPlaying && editing) StopEditing();
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("viewer"), new GUIContent("플레이어 표시기"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roomHeight"), new GUIContent("천장 높이"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tileSize"), new GUIContent("텍스처 반복 크기"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wallTexture"), new GUIContent("벽 이미지"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("doorTexture"), new GUIContent("문 이미지"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("floorTexture"), new GUIContent("바닥 이미지"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ceilingTexture"), new GUIContent("천장 이미지"));
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("playerStart"), new GUIContent("시작 위치 (X·Y)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("playerYaw"), new GUIContent("시작 방향"));
            bool changedStart = EditorGUI.EndChangeCheck();
            if (serializedObject.ApplyModifiedProperties())
            {
                level.Touch();
                if (changedStart && !Application.isPlaying) SyncViewer(level);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("맵 크기", $"{level.width} × {level.height}칸 / 한 칸 {level.cellSize:0.##}");
            EditorGUILayout.HelpBox(
                "바닥을 칠하면 방과 통로가 되고, 벽을 칠하면 막힙니다.\n" +
                "문은 현재 닫힌 벽으로 배치됩니다. 시작 위치는 바닥에 지정하세요.\n" +
                "새 맵은 모두 벽입니다. 바닥을 칠한 뒤 시작 위치를 찍어 주세요.\n" +
                "Scene 뷰를 클릭·드래그해 칠하고, Esc로 종료한 뒤 Ctrl+S로 저장하세요.", MessageType.Info);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                brush = (Brush)GUILayout.SelectionGrid((int)brush, BrushNames, 2);
                if (brush == Brush.Floor || brush == Brush.Wall)
                    brushSize = EditorGUILayout.IntSlider("브러시 크기 (칸)", brushSize, 1, 5);
                if (GUILayout.Button(editing ? "맵 편집 종료 (Esc)" : "맵 편집 시작"))
                {
                    if (editing) StopEditing();
                    else
                    {
                        editing = true;
                        previousTool = Tools.current;
                        Tools.current = Tool.None;
                        FrameLevel(level);
                    }
                    SceneView.RepaintAll();
                }
                DrawNewMap(level);
            }
            if (GUILayout.Button("2D 맵 보기")) FrameLevel(level);
            using (new EditorGUI.DisabledScope(level.viewer == null || level.viewer.map == null || Application.isPlaying))
                if (GUILayout.Button("캐릭터 배치"))
                {
                    StopEditing();
                    Selection.activeGameObject = level.viewer.map.gameObject;
                }

            if (!level.CanOccupy(level.playerStart))
                EditorGUILayout.HelpBox("시작 위치가 벽 안에 있습니다. 바닥을 칠하고 '시작 위치' 브러시로 지정하세요.", MessageType.Warning);
        }

        private void DrawNewMap(ProjectionLevel level)
        {
            showNewMap = EditorGUILayout.Foldout(showNewMap, "새 맵 크기", true);
            if (!showNewMap) return;
            newWidth = Mathf.Clamp(EditorGUILayout.IntField("가로 칸 수", newWidth), 4, 64);
            newHeight = Mathf.Clamp(EditorGUILayout.IntField("세로 칸 수", newHeight), 4, 64);
            newCellSize = Mathf.Clamp(EditorGUILayout.FloatField("한 칸 크기", newCellSize), 0.5f, 4f);
            newOrigin = EditorGUILayout.Vector2Field("지도 왼쪽 아래", newOrigin);
            EditorGUILayout.HelpBox("아래 버튼은 벽·바닥 배치를 초기화합니다. Ctrl+Z로 되돌릴 수 있습니다. 캐릭터 배치는 유지됩니다.", MessageType.None);
            if (!GUILayout.Button("이 크기로 새 맵 만들기")) return;
            EndStroke();
            Undo.RegisterCompleteObjectUndo(level, "Create New Level Layout");
            level.Initialize(newWidth, newHeight, newCellSize, newOrigin, LevelCell.Wall);
            MarkChanged(level);
            FrameLevel(level);
        }

        private void OnSceneGUI()
        {
            if (!editing || Application.isPlaying) return;
            ProjectionLevel level = (ProjectionLevel)target;
            Event current = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                StopEditing();
                current.Use();
                Repaint();
                return;
            }
            if (current.alt)
            {
                EndStroke();
                return;
            }
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (current.type == EventType.MouseUp && current.button == 0 && dragging)
            {
                EndStroke();
                current.Use();
                return;
            }
            if (current.type == EventType.MouseLeaveWindow)
            {
                EndStroke();
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance)) return;
            Vector3 point = ray.GetPoint(distance);
            Vector2Int cell = level.WorldToCell(new Vector2(point.x, point.y));
            if (!Inside(level, cell)) return;
            int previewSize = brush == Brush.Floor || brush == Brush.Wall ? brushSize : 1;
            int previewFirst = -(previewSize - 1) / 2;
            for (int y = previewFirst; y < previewFirst + previewSize; y++)
            for (int x = previewFirst; x < previewFirst + previewSize; x++)
            {
                Vector2Int preview = cell + new Vector2Int(x, y);
                if (Inside(level, preview))
                    DrawCell(level, preview.x, preview.y, new Color(1f, 0.9f, 0.1f, 0.2f), Color.yellow, -0.03f);
            }

            if (current.type == EventType.MouseDown && current.button == 0 && GUIUtility.hotControl == 0)
            {
                Undo.IncrementCurrentGroup();
                strokeGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Paint Level Layout");
                activeControl = control;
                GUIUtility.hotControl = control;
                dragging = true;
                previousCell = cell;
                PaintAt(level, cell);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && dragging)
            {
                int steps = Mathf.Max(Mathf.Abs(cell.x - previousCell.x), Mathf.Abs(cell.y - previousCell.y));
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    PaintAt(level, new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(previousCell.x, cell.x, t)),
                                                 Mathf.RoundToInt(Mathf.Lerp(previousCell.y, cell.y, t))));
                }
                previousCell = cell;
                current.Use();
            }
            if (current.type == EventType.MouseMove || dragging) SceneView.RepaintAll();
        }

        private void PaintAt(ProjectionLevel level, Vector2Int cell)
        {
            if (brush == Brush.Start)
            {
                SetStart(level, cell);
                return;
            }
            LevelCell value = brush == Brush.Floor ? LevelCell.Floor : brush == Brush.Wall ? LevelCell.Wall : LevelCell.Door;
            int size = brush == Brush.Door ? 1 : brushSize;
            int first = -(size - 1) / 2;
            for (int y = first; y < first + size; y++)
                for (int x = first; x < first + size; x++)
                    PaintCell(level, cell + new Vector2Int(x, y), value);
            Repaint();
        }

        public static bool PaintCell(ProjectionLevel level, Vector2Int cell, LevelCell value)
        {
            if (level == null || Application.isPlaying || !Inside(level, cell) || level.GetCell(cell.x, cell.y) == value)
                return false;
            Undo.RecordObject(level, "Paint Level Cell");
            bool changed = level.SetCell(cell.x, cell.y, value);
            if (changed) MarkChanged(level);
            return changed;
        }

        public static bool SetStart(ProjectionLevel level, Vector2Int cell)
        {
            if (level == null || Application.isPlaying || !Inside(level, cell)) return false;
            Undo.RecordObject(level, "Set Level Start");
            level.SetCell(cell.x, cell.y, LevelCell.Floor);
            level.playerStart = level.CellCenter(cell.x, cell.y);
            level.Touch();
            SyncViewer(level);
            MarkChanged(level);
            return true;
        }

        private static void SyncViewer(ProjectionLevel level)
        {
            if (level.viewer == null) return;
            Undo.RecordObject(level.viewer, "Set Player Start");
            level.viewer.playerPosition = level.playerStart;
            level.viewer.yawDegrees = level.playerYaw;
            EditorUtility.SetDirty(level.viewer);
        }

        private static bool Inside(ProjectionLevel level, Vector2Int cell) =>
            cell.x >= 0 && cell.y >= 0 && cell.x < level.width && cell.y < level.height;

        private static void MarkChanged(ProjectionLevel level)
        {
            EditorUtility.SetDirty(level);
            if (level.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            SceneView.RepaintAll();
        }

        public static void FrameLevel(ProjectionLevel level)
        {
            SceneView view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.in2DMode = true;
            Rect area = level.Bounds;
            view.Frame(new Bounds(new Vector3(area.center.x, area.center.y, 0),
                new Vector3(area.width + level.cellSize * 2, area.height + level.cellSize * 2, 1)), false);
            view.Repaint();
        }

        [MenuItem("Tools/DOOM Sprite Map/Edit Walls and Floors")]
        public static void SelectLevel()
        {
            if (Application.isPlaying) return;
            SpriteProjectionDemo viewer = Object.FindFirstObjectByType<SpriteProjectionDemo>(FindObjectsInactive.Include);
            ProjectionLevel level = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<ProjectionLevel>() : null;
            if (level == null && viewer != null) level = viewer.level;
            if (level == null) level = Object.FindFirstObjectByType<ProjectionLevel>();
            if (level == null)
            {
                GameObject mapObject = new GameObject("Map Layout", typeof(ProjectionLevel));
                Undo.RegisterCreatedObjectUndo(mapObject, "Create Level Layout");
                level = mapObject.GetComponent<ProjectionLevel>();
                level.Initialize(20, 16, 2f, new Vector2(-20f, -4f));
            }
            if (level.viewer != null) viewer = level.viewer;
            if (viewer == null)
            {
                GameObject playerObject = new GameObject("Sprite Projection Demo", typeof(SpriteProjectionDemo));
                Undo.RegisterCreatedObjectUndo(playerObject, "Create Sprite Viewer");
                viewer = playerObject.GetComponent<SpriteProjectionDemo>();
            }
            if (Object.FindFirstObjectByType<Camera>() == null)
            {
                GameObject cameraObject = new GameObject("Display Camera", typeof(Camera));
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create Display Camera");
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
            }
            Undo.RecordObject(viewer, "Connect Level Viewer");
            Undo.RecordObject(level, "Connect Level Viewer");
            level.viewer = viewer;
            viewer.level = level;
            if (viewer.map == null)
            {
                GameObject sprites = new GameObject("Sprite Map", typeof(SpriteMap));
                Undo.RegisterCreatedObjectUndo(sprites, "Create Sprite Map");
                viewer.map = sprites.GetComponent<SpriteMap>();
            }
            Undo.RecordObject(viewer.map, "Connect Sprite Map Layout");
            viewer.map.level = level;
            SchoolCorridorBackground renderer = viewer.GetComponent<SchoolCorridorBackground>();
            if (renderer == null) renderer = Undo.AddComponent<SchoolCorridorBackground>(viewer.gameObject);
            Undo.RecordObject(renderer, "Connect Level Background");
            renderer.viewer = viewer;
            renderer.spriteDepthShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DoomSpriteProjection/SpriteWorldDepth.shader");
            const string art = "Assets/DoomSpriteProjection/SchoolAssets/";
            if (level.wallTexture == null)
                level.wallTexture = renderer.wallTexture != null ? renderer.wallTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(art + "Wall.png");
            if (level.doorTexture == null)
                level.doorTexture = renderer.doorTexture != null ? renderer.doorTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(art + "ClassroomDoor.png");
            if (level.floorTexture == null)
                level.floorTexture = renderer.floorTexture != null ? renderer.floorTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(art + "Floor.png");
            if (level.ceilingTexture == null)
                level.ceilingTexture = renderer.ceilingTexture != null ? renderer.ceilingTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(art + "Ceiling.png");
            EditorUtility.SetDirty(viewer);
            EditorUtility.SetDirty(viewer.map);
            EditorUtility.SetDirty(renderer);
            MarkChanged(level);
            Selection.activeGameObject = level.gameObject;
            FrameLevel(level);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawLevel(ProjectionLevel level, GizmoType gizmoType)
        {
            if (SceneView.currentDrawingSceneView == null || !level.enabled) return;
            for (int y = 0; y < level.height; y++)
                for (int x = 0; x < level.width; x++)
                {
                    LevelCell value = level.GetCell(x, y);
                    Color fill = value == LevelCell.Floor ? new Color(0.5f, 0.85f, 0.7f, 0.45f)
                        : value == LevelCell.Door ? new Color(0.95f, 0.6f, 0.2f, 0.8f)
                        : new Color(0.16f, 0.18f, 0.22f, 0.7f);
                    DrawCell(level, x, y, fill, Color.clear, 0.05f);
                }
            Rect bounds = level.Bounds;
            Handles.color = new Color(0.5f, 0.65f, 0.65f, 0.5f);
            for (int x = 0; x <= level.width; x++)
            {
                float worldX = bounds.xMin + x * level.cellSize;
                Handles.DrawLine(new Vector3(worldX, bounds.yMin, 0.04f), new Vector3(worldX, bounds.yMax, 0.04f));
            }
            for (int y = 0; y <= level.height; y++)
            {
                float worldY = bounds.yMin + y * level.cellSize;
                Handles.DrawLine(new Vector3(bounds.xMin, worldY, 0.04f), new Vector3(bounds.xMax, worldY, 0.04f));
            }
            float yaw = level.playerYaw * Mathf.Deg2Rad;
            Vector3 start = new Vector3(level.playerStart.x, level.playerStart.y, -0.04f);
            Vector3 forward = new Vector3(Mathf.Sin(yaw), Mathf.Cos(yaw), 0);
            Vector3 right = new Vector3(forward.y, -forward.x, 0);
            Vector3 tip = start + forward * level.cellSize * 0.4f;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(start, Vector3.forward, level.cellSize * 0.14f);
            Handles.DrawLine(start, tip);
            Handles.DrawLine(tip, tip - forward * level.cellSize * 0.15f + right * level.cellSize * 0.1f);
            Handles.DrawLine(tip, tip - forward * level.cellSize * 0.15f - right * level.cellSize * 0.1f);
        }

        private static void DrawCell(ProjectionLevel level, int x, int y, Color fill, Color outline, float z)
        {
            Vector2 center = level.CellCenter(x, y);
            float half = level.cellSize * 0.5f;
            CellCorners[0] = new Vector3(center.x - half, center.y - half, z);
            CellCorners[1] = new Vector3(center.x - half, center.y + half, z);
            CellCorners[2] = new Vector3(center.x + half, center.y + half, z);
            CellCorners[3] = new Vector3(center.x + half, center.y - half, z);
            Handles.DrawSolidRectangleWithOutline(CellCorners, fill, outline);
        }

        private void EndStroke()
        {
            if (activeControl != 0 && GUIUtility.hotControl == activeControl) GUIUtility.hotControl = 0;
            activeControl = 0;
            dragging = false;
            if (strokeGroup >= 0) Undo.CollapseUndoOperations(strokeGroup);
            strokeGroup = -1;
        }

        private void StopEditing()
        {
            EndStroke();
            if (editing) Tools.current = previousTool;
            editing = false;
            SceneView.RepaintAll();
        }

        private void OnUndoRedo()
        {
            if (target != null) ((ProjectionLevel)target).Touch();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            StopEditing();
        }
    }
}
