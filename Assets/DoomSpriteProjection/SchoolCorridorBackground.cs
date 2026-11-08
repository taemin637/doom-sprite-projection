using UnityEngine;
using UnityEngine.UI;

namespace DoomProjection
{
    public class SchoolCorridorBackground : MonoBehaviour
    {
        public SpriteProjectionDemo viewer;
        public Texture2D wallTexture, doorTexture, floorTexture, ceilingTexture;
        public Rect floorBounds = new Rect(-4f, 0f, 8f, 24f);
        public float roomHeight = 3.4f;
        public float tileSize = 3f;
        public float doorWidth = 2.6f;
        public float doorHeight = 2.8f;
        public float[] doorCenters = { 8f, 16f };
        public int renderWidth = 480;
        public Shader spriteDepthShader;

        public Texture2D RenderedTexture => frame;
        public Texture2D DepthTexture => depthTexture;
        public int RenderCount { get; private set; }

        private RawImage back;
        private Texture2D frame;
        private Texture2D depthTexture;
        private Color32[] output;
        private float[] depthOutput;
        private TexturePixels wall, door, floor, ceiling;
        private Vector2 previousPosition;
        private float previousYaw, previousPitch, previousEye, previousFov;
        private int previousSettings, previousScreenWidth, previousScreenHeight;
        private bool needsRender = true;
        private ProjectionLevel Level => viewer != null && viewer.level != null && viewer.level.isActiveAndEnabled
            ? viewer.level : null;

        private class TexturePixels
        {
            public readonly Texture2D source;
            private readonly Color32[] pixels;
            private readonly int width, height;
            private readonly Color32 fallback;

            public TexturePixels(Texture2D texture, Color32 fallbackColor)
            {
                source = texture;
                fallback = fallbackColor;
                if (texture == null || !texture.isReadable)
                    return;
                width = texture.width;
                height = texture.height;
                pixels = texture.GetPixels32();
            }

            public Color32 Sample(float u, float v)
            {
                if (pixels == null)
                    return fallback;
                int x = Mathf.Clamp((int)(u * width), 0, width - 1);
                int y = Mathf.Clamp((int)(v * height), 0, height - 1);
                return pixels[y * width + x];
            }
        }

        private void Awake()
        {
            if (viewer == null)
                viewer = GetComponent<SpriteProjectionDemo>();
            SetMovementBounds();
        }

        private void SetMovementBounds()
        {
            if (viewer == null)
                return;
            const float margin = 0.3f;
            Rect bounds = Level != null ? Level.Bounds : floorBounds;
            viewer.constrainMovement = true;
            viewer.movementBounds = Rect.MinMaxRect(bounds.xMin + margin, bounds.yMin + margin,
                                                   bounds.xMax - margin, bounds.yMax - margin);
        }

        private void LateUpdate()
        {
            if (viewer == null || !viewer.isActiveAndEnabled || viewer.DisplayCanvas == null)
                return;
            if (back == null)
                CreateDisplay();

            ProjectionLevel level = Level;
            CacheTexture(ref wall, level != null && level.wallTexture != null ? level.wallTexture : wallTexture,
                         new Color32(218, 228, 205, 255));
            CacheTexture(ref door, level != null && level.doorTexture != null ? level.doorTexture : doorTexture,
                         new Color32(199, 157, 90, 255));
            CacheTexture(ref floor, level != null && level.floorTexture != null ? level.floorTexture : floorTexture,
                         new Color32(218, 209, 190, 255));
            CacheTexture(ref ceiling, level != null && level.ceilingTexture != null ? level.ceilingTexture : ceilingTexture,
                         new Color32(234, 232, 218, 255));

            int settings = SettingsHash();
            if (!needsRender && previousPosition == viewer.playerPosition && previousYaw == viewer.yawDegrees
                && previousPitch == viewer.pitchDegrees && previousEye == viewer.eyeHeight
                && previousFov == viewer.horizontalFov
                && previousScreenWidth == Screen.width && previousScreenHeight == Screen.height
                && previousSettings == settings)
            {
                viewer.SetWorldDepth(level != null ? depthTexture : null, spriteDepthShader);
                return;
            }

            SetMovementBounds();
            int width = Mathf.Clamp(renderWidth, 120, 960);
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width)));
            if (frame == null || frame.width != width || frame.height != height)
            {
                if (frame != null) Destroy(frame);
                if (depthTexture != null) Destroy(depthTexture);
                frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
                frame.name = "Manually Projected Map";
                frame.filterMode = FilterMode.Point;
                frame.wrapMode = TextureWrapMode.Clamp;
                output = new Color32[width * height];
                depthOutput = new float[width * height];
                depthTexture = new Texture2D(width, height, TextureFormat.RFloat, false, true);
                depthTexture.name = "Map Camera Depth";
                depthTexture.filterMode = FilterMode.Point;
                depthTexture.wrapMode = TextureWrapMode.Clamp;
                back.texture = frame;
            }

            RenderRoom();
            previousPosition = viewer.playerPosition;
            previousYaw = viewer.yawDegrees;
            previousPitch = viewer.pitchDegrees;
            previousEye = viewer.eyeHeight;
            previousFov = viewer.horizontalFov;
            previousScreenWidth = Screen.width;
            previousScreenHeight = Screen.height;
            previousSettings = settings;
            needsRender = false;
            viewer.SetWorldDepth(level != null ? depthTexture : null, spriteDepthShader);
        }

        private void CacheTexture(ref TexturePixels cache, Texture2D texture, Color32 fallback)
        {
            if (cache != null && cache.source == texture)
                return;
            cache = new TexturePixels(texture, fallback);
            needsRender = true;
        }

        private void CreateDisplay()
        {
            back = new GameObject("School Corridor Background", typeof(RectTransform),
                                  typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            back.transform.SetParent(viewer.DisplayCanvas.transform, false);
            Transform sprites = viewer.DisplayCanvas.transform.Find("Sprites");
            if (sprites != null)
                back.transform.SetSiblingIndex(sprites.GetSiblingIndex());
            RectTransform rect = back.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            back.raycastTarget = false;
            needsRender = true;
        }

        private void RenderRoom()
        {
            int width = frame.width, height = frame.height;
            Vector2 position = viewer.playerPosition;
            float angle = viewer.yawDegrees * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            Vector2 right = new Vector2(forward.y, -forward.x);
            float pitch = viewer.pitchDegrees * Mathf.Deg2Rad;
            float sinPitch = Mathf.Sin(pitch), cosPitch = Mathf.Cos(pitch);
            float focal = width / (2f * Mathf.Tan(viewer.horizontalFov * Mathf.Deg2Rad * 0.5f));
            float verticalStep = width * (float)Screen.height / (Mathf.Max(1, Screen.width) * height * focal);
            ProjectionLevel level = Level;
            float repeatSize = Mathf.Max(0.01f, level != null ? level.tileSize : tileSize);
            float safeRoomHeight = Mathf.Max(0.01f, level != null ? level.roomHeight : roomHeight);

            for (int y = 0; y < height; y++)
            {
                float vertical = (y + 0.5f - height * 0.5f) * verticalStep;
                Vector2 rowForward = forward * (cosPitch - vertical * sinPitch);
                float heightRay = sinPitch + vertical * cosPitch;
                bool belowHorizon = heightRay < 0f;
                float planeDepth = BoundaryDepth(viewer.eyeHeight, heightRay, 0f, safeRoomHeight);

                for (int x = 0; x < width; x++)
                {
                    float horizontal = (x + 0.5f - width * 0.5f) / focal;
                    Vector2 ray = rowForward + right * horizontal;
                    float wallDepth;
                    bool sideWall;
                    LevelRayHit mapHit = default;
                    if (level != null)
                    {
                        bool hitWall = level.Raycast(position, ray, planeDepth, out mapHit);
                        wallDepth = hitWall ? mapHit.depth : float.PositiveInfinity;
                        sideWall = mapHit.sideX;
                    }
                    else
                    {
                        float depthX = BoundaryDepth(position.x, ray.x, floorBounds.xMin, floorBounds.xMax);
                        float depthY = BoundaryDepth(position.y, ray.y, floorBounds.yMin, floorBounds.yMax);
                        sideWall = depthX < depthY;
                        wallDepth = sideWall ? depthX : depthY;
                    }
                    Color32 color;
                    float visibleDepth;
                    if (planeDepth <= wallDepth)
                    {
                        Vector2 point = position + ray * planeDepth;
                        float u = Mathf.Repeat(point.x / repeatSize, 1f);
                        float v = Mathf.Repeat(point.y / repeatSize, 1f);
                        color = (belowHorizon ? floor : ceiling).Sample(u, v);
                        visibleDepth = planeDepth;
                    }
                    else
                    {
                        Vector2 wallPoint = position + ray * wallDepth;
                        float wallU = level != null ? mapHit.u
                            : Mathf.Repeat((sideWall ? wallPoint.y - floorBounds.yMin
                                                   : wallPoint.x - floorBounds.xMin) / safeRoomHeight, 1f);
                        float doorU = level != null
                            ? (mapHit.cell == LevelCell.Door ? mapHit.u : -1f)
                            : DoorCoordinate(wallPoint, sideWall, ray.y > 0f);
                        float z = viewer.eyeHeight + heightRay * wallDepth;
                        bool onDoor = doorU >= 0f && z >= 0f && z <= doorHeight;
                        color = onDoor ? door.Sample(doorU, z / Mathf.Max(0.01f, doorHeight))
                                       : wall.Sample(wallU, z / safeRoomHeight);
                        visibleDepth = wallDepth;
                    }
                    int pixel = y * width + x;
                    output[pixel] = color;
                    depthOutput[pixel] = float.IsInfinity(visibleDepth) || float.IsNaN(visibleDepth)
                        ? 1e20f : Mathf.Max(0f, visibleDepth);
                }
            }
            frame.SetPixels32(output);
            frame.Apply(false);
            depthTexture.SetPixelData(depthOutput, 0);
            depthTexture.Apply(false);
            RenderCount++;
        }

        private static float BoundaryDepth(float origin, float direction, float minimum, float maximum)
        {
            if (Mathf.Abs(direction) < 0.000001f)
                return float.PositiveInfinity;
            float depth = ((direction > 0f ? maximum : minimum) - origin) / direction;
            return depth > 0f ? depth : float.PositiveInfinity;
        }

        private float DoorCoordinate(Vector2 hit, bool sideWall, bool farWall)
        {
            if (doorWidth <= 0f)
                return -1f;
            if (sideWall && doorCenters != null)
            {
                foreach (float center in doorCenters)
                    if (Mathf.Abs(hit.y - center) <= doorWidth * 0.5f)
                        return (hit.y - center) / doorWidth + 0.5f;
            }
            if (!sideWall && farWall && Mathf.Abs(hit.x - floorBounds.center.x) <= doorWidth * 0.5f)
                return (hit.x - floorBounds.center.x) / doorWidth + 0.5f;
            return -1f;
        }

        private int SettingsHash()
        {
            unchecked
            {
                int hash = floorBounds.GetHashCode();
                ProjectionLevel level = Level;
                if (level != null)
                {
                    hash = hash * 31 + level.GetInstanceID();
                    hash = hash * 31 + level.Revision;
                    hash = hash * 31 + level.Bounds.GetHashCode();
                    hash = hash * 31 + level.roomHeight.GetHashCode();
                    hash = hash * 31 + level.tileSize.GetHashCode();
                }
                hash = hash * 31 + roomHeight.GetHashCode();
                hash = hash * 31 + tileSize.GetHashCode();
                hash = hash * 31 + doorWidth.GetHashCode();
                hash = hash * 31 + doorHeight.GetHashCode();
                hash = hash * 31 + renderWidth;
                if (doorCenters != null)
                    foreach (float center in doorCenters) hash = hash * 31 + center.GetHashCode();
                return hash;
            }
        }

        private void OnEnable()
        {
            needsRender = true;
            if (back != null) back.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (back != null) back.gameObject.SetActive(false);
            if (viewer != null) viewer.SetWorldDepth(null, null);
        }

        private void OnDestroy()
        {
            if (back != null) Destroy(back.gameObject);
            if (frame != null) Destroy(frame);
            if (depthTexture != null) Destroy(depthTexture);
        }

        private void OnDrawGizmos()
        {
            if (Level != null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 0.65f);
            Gizmos.DrawWireCube(new Vector3(floorBounds.center.x, floorBounds.center.y, 0f),
                                new Vector3(floorBounds.width, floorBounds.height, 0f));
            Gizmos.color = new Color(1f, 0.7f, 0.3f);
            if (doorCenters != null)
                foreach (float center in doorCenters)
                {
                    Vector3 halfDoor = Vector3.up * doorWidth * 0.5f;
                    Vector3 left = new Vector3(floorBounds.xMin, center, 0f);
                    Vector3 right = new Vector3(floorBounds.xMax, center, 0f);
                    Gizmos.DrawLine(left - halfDoor, left + halfDoor);
                    Gizmos.DrawLine(right - halfDoor, right + halfDoor);
                }
            Vector3 end = new Vector3(floorBounds.center.x, floorBounds.yMax, 0f);
            Gizmos.DrawLine(end - Vector3.right * doorWidth * 0.5f, end + Vector3.right * doorWidth * 0.5f);
        }
    }
}
