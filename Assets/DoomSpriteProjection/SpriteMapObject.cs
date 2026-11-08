using UnityEngine;

namespace DoomProjection
{
    [DisallowMultipleComponent]
    public class SpriteMapObject : MonoBehaviour
    {
        public Sprite sourceSprite;
        public float baseHeight;
        public float objectHeight = 2f;

        public Vector2 MapPosition => new Vector2(transform.position.x, transform.position.y);

        public void SetSprite(Sprite sprite)
        {
            sourceSprite = sprite;
        }

        public void SetMapPosition(Vector2 position, float bottomHeight = 0f)
        {
            transform.position = new Vector3(position.x, position.y, 0f);
            baseHeight = bottomHeight;
        }
    }
}
