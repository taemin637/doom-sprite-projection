using UnityEngine;

namespace DoomProjection
{
    public static class SpriteProjection
    {
        public const float NearDepth = 0.05f;

        public static bool TryProject(
            Vector2 playerPosition, float yawDegrees, float eyeHeight,
            Vector2 objectPosition, float baseHeight, Vector2 objectSize,
            Vector2 screenSize, float horizontalFov,
            out Rect screenRect, out float depth, float pitchDegrees = 0f)
        {
            screenRect = default;
            depth = 0f;
            if (screenSize.x <= 0f || screenSize.y <= 0f ||
                objectSize.x <= 0f || objectSize.y <= 0f ||
                horizontalFov <= 0f || horizontalFov >= 180f)
                return false;

            Vector2 relative = objectPosition - playerPosition;

            float angle = yawDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            float horizontal = relative.x * cos - relative.y * sin;
            float flatDepth = relative.x * sin + relative.y * cos;
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float sinPitch = Mathf.Sin(pitch);
            float cosPitch = Mathf.Cos(pitch);
            float relativeHeight = baseHeight - eyeHeight;
            depth = flatDepth * cosPitch + relativeHeight * sinPitch;
            float vertical = relativeHeight * cosPitch - flatDepth * sinPitch;

            if (depth < NearDepth)
                return false;

            float halfFovRadians = horizontalFov * Mathf.Deg2Rad * 0.5f;
            float focalLength = screenSize.x / (2f * Mathf.Tan(halfFovRadians));
            float scale = focalLength / depth;
            float width = objectSize.x * scale;
            float height = objectSize.y * scale;
            float centerX = screenSize.x * 0.5f + horizontal * scale;
            float bottomY = screenSize.y * 0.5f + vertical * scale;
            screenRect = new Rect(centerX - width * 0.5f, bottomY, width, height);

            return screenRect.xMax > 0f && screenRect.xMin < screenSize.x &&
                   screenRect.yMax > 0f && screenRect.yMin < screenSize.y;
        }
    }
}
