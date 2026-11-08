using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DoomProjection
{
    public class SpriteProjectionDemo : MonoBehaviour
    {
        public Vector2 playerPosition = Vector2.zero;
        public float yawDegrees;
        public float pitchDegrees;
        public float eyeHeight = 1f;
        public float moveSpeed = 6f;
        public float turnSpeed = 90f;
        public float horizontalFov = 90f;

        public bool mouseLookEnabled = true;
        public float mouseSensitivity = 0.12f;
        public bool MouseLookActive => Cursor.lockState == CursorLockMode.Locked;

        public SpriteMap map;
        public ProjectionLevel level;

        private Canvas canvas;
        private RectTransform spriteLayer;
        private RectTransform ceilingDisplay, floorDisplay, horizonDisplay;
        private Sprite fallbackSprite;
        private Texture2D fallbackTexture;
        private Vector2 startPosition;
        private float startYaw;
        private float startPitch;
        private float startEyeHeight;

        private class SpriteView
        {
            public SpriteMapObject item;
            public Image image;
            public Material depthMaterial;
            public float depth;
            public int order;
        }

        private readonly List<SpriteView> views = new List<SpriteView>();

        private void Awake()
        {
            if (level != null && level.TryFindSpawn(out Vector2 spawn))
            {
                playerPosition = spawn;
                yawDegrees = level.playerYaw;
            }
            startPosition = playerPosition;
            startYaw = yawDegrees;
            startPitch = pitchDegrees;
            startEyeHeight = eyeHeight;
            CreateDisplay();
            CreateFallbackSprite();
            RefreshMap();
        }

        private void Update()
        {
            ReadLook();
            ReadMovement();
            UpdateFlatBackground();
            UpdateProjection();
        }

        public void RefreshMap()
        {
            if (spriteLayer == null)
                return;
            foreach (SpriteView view in views)
            {
                view.image.gameObject.SetActive(false);
                Destroy(view.image.gameObject);
                if (view.depthMaterial != null) Destroy(view.depthMaterial);
            }
            views.Clear();
            if (map == null)
                return;

            foreach (SpriteMapObject item in map.GetObjects())
            {
                Image image = CreateImage("Projected Sprite: " + item.name, Color.white, spriteLayer);
                image.rectTransform.anchorMin = Vector2.zero;
                image.rectTransform.anchorMax = Vector2.zero;
                image.rectTransform.pivot = Vector2.zero;
                image.useSpriteMesh = false;
                image.enabled = false;
                views.Add(new SpriteView { item = item, image = image, order = views.Count });
            }
        }

        public void ResetView()
        {
            playerPosition = startPosition;
            yawDegrees = startYaw;
            pitchDegrees = startPitch;
            eyeHeight = startEyeHeight;
        }

        public void SetWorldDepth(Texture2D depthTexture, Shader shader)
        {
            foreach (SpriteView view in views)
            {
                if (view.image == null) continue;
                if (depthTexture == null || shader == null)
                {
                    view.image.material = null;
                    continue;
                }
                if (view.depthMaterial == null || view.depthMaterial.shader != shader)
                {
                    if (view.depthMaterial != null) Destroy(view.depthMaterial);
                    view.depthMaterial = new Material(shader);
                }
                view.depthMaterial.SetTexture("_WorldDepthTex", depthTexture);
                view.depthMaterial.SetFloat("_SpriteDepth", view.depth);
                view.image.material = view.depthMaterial;
            }
        }

        private void ReadLook()
        {
            Keyboard keyboard = Keyboard.current;
            float tilt = (keyboard != null && keyboard.pageUpKey.isPressed ? 1f : 0f)
                       - (keyboard != null && keyboard.pageDownKey.isPressed ? 1f : 0f);
            pitchDegrees = Mathf.Clamp(pitchDegrees + tilt * turnSpeed * Time.deltaTime, -89f, 89f);

            if (!mouseLookEnabled || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            {
                SetMouseLookActive(false);
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            if (!MouseLookActive && mouse.leftButton.wasPressedThisFrame)
            {
                SetMouseLookActive(true);
                return;
            }
            if (!MouseLookActive)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            yawDegrees = Mathf.Repeat(yawDegrees + delta.x * mouseSensitivity, 360f);
            pitchDegrees = Mathf.Clamp(pitchDegrees + delta.y * mouseSensitivity, -89f, 89f);
        }

        public void SetMouseLookActive(bool active)
        {
            active = active && mouseLookEnabled && isActiveAndEnabled;
            Cursor.lockState = active ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !active;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                SetMouseLookActive(false);
        }

        private void ReadMovement()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.rKey.wasPressedThisFrame)
                ResetView();

            float turn = (keyboard.eKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                       - (keyboard.qKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            yawDegrees = Mathf.Repeat(yawDegrees + turn * turnSpeed * Time.deltaTime, 360f);

            float forwardInput = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                               - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            float sideInput = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            Vector2 input = Vector2.ClampMagnitude(new Vector2(sideInput, forwardInput), 1f);
            float angle = yawDegrees * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            Vector2 right = new Vector2(forward.y, -forward.x);
            playerPosition = ClampMovement(playerPosition + (right * input.x + forward * input.y) * moveSpeed * Time.deltaTime);
        }

        private void UpdateProjection()
        {
            foreach (SpriteView view in views)
            {
                SpriteMapObject item = view.item;
                if (map == null || !map.isActiveAndEnabled || item == null || !item.isActiveAndEnabled)
                {
                    view.image.enabled = false;
                    continue;
                }

                Sprite sprite = item.sourceSprite != null ? item.sourceSprite : fallbackSprite;
                view.image.sprite = sprite;
                float aspect = sprite.rect.width / sprite.rect.height;
                Vector2 size = new Vector2(item.objectHeight * aspect, item.objectHeight);

                bool visible = SpriteProjection.TryProject(
                    playerPosition, yawDegrees, eyeHeight, item.MapPosition, item.baseHeight, size,
                    new Vector2(Screen.width, Screen.height), horizontalFov,
                    out Rect rect, out view.depth, pitchDegrees);

                view.image.enabled = visible;
                if (!visible)
                    continue;
                view.image.rectTransform.anchoredPosition = rect.position;
                view.image.rectTransform.sizeDelta = rect.size;
            }

            views.Sort((a, b) => a.depth == b.depth ? a.order.CompareTo(b.order) : b.depth.CompareTo(a.depth));
            for (int i = 0; i < views.Count; i++)
                views[i].image.transform.SetSiblingIndex(i);
        }

        private void CreateDisplay()
        {
            GameObject root = new GameObject("Projection Canvas", typeof(RectTransform), typeof(Canvas));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            Image ceiling = CreateImage("Ceiling", new Color32(27, 32, 43, 255));
            ceilingDisplay = ceiling.rectTransform;
            Stretch(ceiling.rectTransform, new Vector2(0f, 0.5f), Vector2.one);
            Image floor = CreateImage("Floor", new Color32(61, 57, 52, 255));
            floorDisplay = floor.rectTransform;
            Stretch(floor.rectTransform, Vector2.zero, new Vector2(1f, 0.5f));
            Image horizon = CreateImage("Horizon", new Color32(110, 103, 86, 255));
            horizonDisplay = horizon.rectTransform;
            Stretch(horizon.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            horizon.rectTransform.sizeDelta = new Vector2(0f, 1f);

            spriteLayer = new GameObject("Sprites", typeof(RectTransform)).GetComponent<RectTransform>();
            spriteLayer.SetParent(canvas.transform, false);
            Stretch(spriteLayer, Vector2.zero, Vector2.one);
        }

        private void UpdateFlatBackground()
        {
            float focal = Screen.width / (2f * Mathf.Tan(horizontalFov * Mathf.Deg2Rad * 0.5f));
            float horizon = 0.5f - focal * Mathf.Tan(pitchDegrees * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            float split = Mathf.Clamp01(horizon);
            ceilingDisplay.anchorMin = new Vector2(0f, split);
            floorDisplay.anchorMax = new Vector2(1f, split);
            horizonDisplay.anchorMin = new Vector2(0f, split);
            horizonDisplay.anchorMax = new Vector2(1f, split);
            horizonDisplay.gameObject.SetActive(horizon >= 0f && horizon <= 1f);
        }

        private Image CreateImage(string objectName, Color color, Transform parent = null)
        {
            GameObject obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent != null ? parent : canvas.transform, false);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void CreateFallbackSprite()
        {
            string[] rows = {
                "....OOOO....", "...OOOOOO...", "..OOOEEOOO..", "..OOOEEOOO..",
                "..OOOOOOOO..", "....OOOO....", "..OOOOOOOO..", ".OOOOOOOOOO.",
                ".OO.OOOO.OO.", ".OO.OEEO.OO.", "....OOOO....", "...OOOOOO...",
                "...OO..OO...", "...OO..OO...", "..OOO..OOO..", "..OOO..OOO.."
            };
            fallbackTexture = new Texture2D(12, 16, TextureFormat.RGBA32, false);
            fallbackTexture.name = "Test Robot";
            fallbackTexture.filterMode = FilterMode.Point;
            fallbackTexture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 12; x++)
            {
                char pixel = rows[15 - y][x];
                Color color = pixel == 'O' ? new Color32(226, 122, 61, 255)
                            : pixel == 'E' ? new Color32(118, 238, 224, 255) : Color.clear;
                fallbackTexture.SetPixel(x, y, color);
            }
            fallbackTexture.Apply();
            fallbackSprite = Sprite.Create(fallbackTexture, new Rect(0, 0, 12, 16), new Vector2(0.5f, 0f));
        }

        private void OnDisable()
        {
            SetMouseLookActive(false);
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (canvas != null)
                canvas.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            foreach (SpriteView view in views)
                if (view.depthMaterial != null) Destroy(view.depthMaterial);
            if (canvas != null) Destroy(canvas.gameObject);
            if (fallbackSprite != null) Destroy(fallbackSprite);
            if (fallbackTexture != null) Destroy(fallbackTexture);
        }

        public bool constrainMovement;
        public Rect movementBounds;
        public Canvas DisplayCanvas => canvas;

        private Vector2 ClampMovement(Vector2 position)
        {
            if (level != null && level.isActiveAndEnabled)
                return level.Move(playerPosition, position - playerPosition);
            if (!constrainMovement)
                return position;
            return new Vector2(Mathf.Clamp(position.x, movementBounds.xMin, movementBounds.xMax),
                               Mathf.Clamp(position.y, movementBounds.yMin, movementBounds.yMax));
        }
    }
}
