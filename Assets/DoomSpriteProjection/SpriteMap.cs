using UnityEngine;

namespace DoomProjection
{
    [DisallowMultipleComponent]
    public class SpriteMap : MonoBehaviour
    {
        public ProjectionLevel level;

        public Sprite placementSprite;
        public float placementHeight = 2f;
        public float placementBaseHeight;

        public bool snapToGrid = true;
        public float gridSize = 1f;

        public Vector2 SnapPosition(Vector2 position)
        {
            if (!snapToGrid)
                return position;
            float step = Mathf.Max(0.1f, gridSize);
            return new Vector2(Mathf.Round(position.x / step) * step,
                               Mathf.Round(position.y / step) * step);
        }

        public SpriteMapObject AddObject(Vector2 position)
        {
            string label = placementSprite != null ? placementSprite.name : "Test Sprite";
            GameObject obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            SpriteMapObject item = obj.AddComponent<SpriteMapObject>();
            item.SetSprite(placementSprite);
            item.objectHeight = Mathf.Max(0.01f, placementHeight);
            item.SetMapPosition(SnapPosition(position), placementBaseHeight);
            return item;
        }

        public SpriteMapObject[] GetObjects()
        {
            return GetComponentsInChildren<SpriteMapObject>(true);
        }
    }
}
