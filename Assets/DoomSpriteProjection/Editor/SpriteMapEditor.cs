using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoomProjection
{
    [CustomEditor(typeof(SpriteMap))]
    public class SpriteMapEditor : Editor
    {
        private bool placing;
        private static readonly Dictionary<int, Texture2D> Previews = new Dictionary<int, Texture2D>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            SpriteMap map = (SpriteMap)target;
            if (Application.isPlaying) placing = false;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Placement Sprite를 선택하고 배치를 시작한 뒤 Scene 뷰를 클릭하세요.\n" +
                "Sprite를 비우면 기본 테스트 이미지를 사용합니다.\n" +
                "배치 종료 또는 Esc 후 물체를 선택해 W로 이동, Ctrl+D로 복제, Delete로 삭제할 수 있습니다.\n" +
                "편집 모드에서 배치한 뒤 씬을 저장하고 Play로 확인하세요.", MessageType.Info);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(placing ? "배치 종료 (Esc)" : "배치 시작"))
                {
                    placing = !placing;
                    if (placing) FrameMap(map);
                    SceneView.RepaintAll();
                }
            }
            if (GUILayout.Button("2D 맵 보기")) FrameMap(map);
            if (map.level != null && GUILayout.Button("벽·바닥 편집"))
                Selection.activeGameObject = map.level.gameObject;
            EditorGUILayout.LabelField("배치된 물체", map.GetObjects().Length.ToString());
        }

        private void OnSceneGUI()
        {
            if (!placing || Application.isPlaying) return;
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                placing = false;
                current.Use();
                Repaint();
                SceneView.RepaintAll();
                return;
            }
            if (current.alt) return;

            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            SpriteMap map = (SpriteMap)target;
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            Plane mapPlane = new Plane(Vector3.forward, Vector3.zero);
            if (!mapPlane.Raycast(ray, out float distance)) return;

            Vector3 hit = ray.GetPoint(distance);
            Vector2 position = map.SnapPosition(new Vector2(hit.x, hit.y));
            Vector3 point = new Vector3(position.x, position.y, 0f);
            bool canPlace = map.level == null || map.level.CanOccupy(position, 0.05f);
            Handles.color = canPlace ? Color.yellow : Color.red;
            Handles.DrawWireDisc(point, Vector3.forward, 0.2f);
            Handles.Label(point + Vector3.right * 0.3f, canPlace
                ? $"({position.x:0.##}, {position.y:0.##})" : "바닥 위에 배치하세요");
            DrawPreview(point, map.placementSprite);

            if (current.type == EventType.MouseDown && current.button == 0 && GUIUtility.hotControl == 0)
            {
                PlaceObject(map, position);
                Selection.activeGameObject = map.gameObject;
                current.Use();
                Repaint();
            }
            if (current.type == EventType.MouseMove) SceneView.RepaintAll();
        }

        public static SpriteMapObject PlaceObject(SpriteMap map, Vector2 position)
        {
            if (map == null || Application.isPlaying) return null;
            position = map.SnapPosition(position);
            if (map.level != null && !map.level.CanOccupy(position, 0.05f)) return null;
            SpriteMapObject item = map.AddObject(position);
            Undo.RegisterCreatedObjectUndo(item.gameObject, "Place Map Sprite");
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            SceneView.RepaintAll();
            return item;
        }

        [MenuItem("Tools/DOOM Sprite Map/Create or Select Map")]
        public static void CreateOrSelectMap()
        {
            if (Application.isPlaying) return;
            SpriteProjectionDemo demo = Object.FindFirstObjectByType<SpriteProjectionDemo>(FindObjectsInactive.Include);
            SpriteMap map = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<SpriteMap>() : null;
            if (map == null && demo != null) map = demo.map;
            if (map == null) map = Object.FindFirstObjectByType<SpriteMap>(FindObjectsInactive.Include);
            if (map == null)
            {
                GameObject root = new GameObject("Sprite Map", typeof(SpriteMap));
                Undo.RegisterCreatedObjectUndo(root, "Create Sprite Map");
                map = root.GetComponent<SpriteMap>();
                EditorSceneManager.MarkSceneDirty(root.scene);
            }
            if (demo != null && demo.map != map)
            {
                Undo.RecordObject(demo, "Connect Sprite Map");
                demo.map = map;
                EditorUtility.SetDirty(demo);
                EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);
            }
            if (demo != null && map.level != demo.level)
            {
                Undo.RecordObject(map, "Connect Wall Map");
                map.level = demo.level;
                EditorUtility.SetDirty(map);
                EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            }
            Selection.activeGameObject = map.gameObject;
            EditorGUIUtility.PingObject(map);
            FrameMap(map);
        }

        private static void FrameMap(SpriteMap map)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) view = EditorWindow.GetWindow<SceneView>();
            view.in2DMode = true;
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(8f, 8f, 1f));
            if (map.level != null)
            {
                Rect area = map.level.Bounds;
                bounds.Encapsulate(new Vector3(area.xMin, area.yMin, 0f));
                bounds.Encapsulate(new Vector3(area.xMax, area.yMax, 0f));
            }
            foreach (SpriteMapObject item in map.GetObjects())
                bounds.Encapsulate(new Vector3(item.MapPosition.x, item.MapPosition.y, 0f));
            bounds.Expand(new Vector3(4f, 4f, 0f));
            view.Frame(bounds, false);
            view.Repaint();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawMapObject(SpriteMapObject item, GizmoType gizmoType)
        {
            if (SceneView.currentDrawingSceneView == null) return;
            Vector3 point = new Vector3(item.MapPosition.x, item.MapPosition.y, 0f);
            bool selected = Selection.Contains(item.gameObject);
            Gizmos.color = selected ? Color.yellow : new Color(1f, 0.55f, 0.2f);
            Gizmos.DrawCube(point, Vector3.one * 0.18f);
            Gizmos.DrawWireCube(point, new Vector3(0.4f, 0.4f, 0.05f));
            DrawPreview(point, item.sourceSprite);
            if (selected)
                Handles.Label(point + Vector3.down * 0.35f, item.name);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawPlayer(SpriteProjectionDemo demo, GizmoType gizmoType)
        {
            if (SceneView.currentDrawingSceneView == null) return;
            Vector3 origin = new Vector3(demo.playerPosition.x, demo.playerPosition.y, 0f);
            float angle = demo.yawDegrees * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            Vector3 right = new Vector3(forward.y, -forward.x, 0f);
            Vector3 tip = origin + forward * 0.9f;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(origin, Vector3.forward, 0.2f);
            Handles.DrawLine(origin, tip);
            Handles.DrawLine(tip, tip - forward * 0.25f + right * 0.15f);
            Handles.DrawLine(tip, tip - forward * 0.25f - right * 0.15f);
            Handles.Label(origin + Vector3.left * 0.3f, "PLAYER");
        }

        private static void DrawPreview(Vector3 point, Sprite sprite)
        {
            if (sprite == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            int id = sprite.GetInstanceID();
            if (!Previews.TryGetValue(id, out Texture2D texture) || texture == null)
            {
                texture = AssetPreview.GetAssetPreview(sprite);
                if (texture != null) Previews[id] = texture;
                else
                {
                    texture = AssetPreview.GetMiniThumbnail(sprite);
                    if (AssetPreview.IsLoadingAssetPreview(id)) SceneView.RepaintAll();
                }
            }
            if (texture == null) return;
            Vector2 screen = HandleUtility.WorldToGUIPoint(point);
            Rect rect = new Rect(screen.x - 20f, screen.y - 44f, 40f, 40f);
            Handles.BeginGUI();
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            Handles.EndGUI();
        }

        private void OnDisable()
        {
            placing = false;
            SceneView.RepaintAll();
        }
    }
}
