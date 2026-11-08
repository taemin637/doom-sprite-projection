using UnityEngine;

namespace DoomProjection
{
    public enum LevelCell { Wall = 0, Floor = 1, Door = 2 }

    public struct LevelRayHit
    {
        public float depth, u;
        public int x, y;
        public bool sideX;
        public LevelCell cell;
    }

    [DisallowMultipleComponent]
    public class ProjectionLevel : MonoBehaviour
    {
        public SpriteProjectionDemo viewer;
        public int width = 20, height = 16;
        public float cellSize = 2f;
        public Vector2 origin = new Vector2(-20f, -4f);
        public float roomHeight = 3.4f;
        public float tileSize = 3f;
        public Texture2D wallTexture, doorTexture, floorTexture, ceilingTexture;
        public Vector2 playerStart = new Vector2(0f, 2f);
        public float playerYaw;

        [SerializeField] private LevelCell[] cells;
        [SerializeField] private int storedWidth, storedHeight;
        private int revision;
        public int Revision => revision;
        public Rect Bounds
        {
            get
            {
                EnsureData();
                return new Rect(origin, new Vector2(width * cellSize, height * cellSize));
            }
        }

        public void Initialize(int newWidth, int newHeight, float newCellSize, Vector2 newOrigin,
            LevelCell fill = LevelCell.Wall)
        {
            width = Mathf.Clamp(newWidth, 4, 64);
            height = Mathf.Clamp(newHeight, 4, 64);
            cellSize = Mathf.Clamp(newCellSize, 0.5f, 4f);
            origin = newOrigin;
            cells = new LevelCell[width * height];
            if (fill != LevelCell.Wall)
                for (int i = 0; i < cells.Length; i++) cells[i] = fill;
            storedWidth = width;
            storedHeight = height;
            Touch();
        }

        public LevelCell GetCell(int x, int y)
        {
            EnsureData();
            return ReadCell(x, y);
        }

        public bool SetCell(int x, int y, LevelCell value)
        {
            EnsureData();
            if (x < 0 || y < 0 || x >= width || y >= height || cells[y * width + x] == value)
                return false;
            cells[y * width + x] = value;
            unchecked { revision++; }
            return true;
        }

        public Vector2Int WorldToCell(Vector2 position)
        {
            EnsureData();
            return new Vector2Int(Mathf.FloorToInt((position.x - origin.x) / cellSize),
                                  Mathf.FloorToInt((position.y - origin.y) / cellSize));
        }

        public Vector2 CellCenter(int x, int y)
        {
            EnsureData();
            return origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        }

        public bool CanOccupy(Vector2 position, float radius = 0.2f)
        {
            EnsureData();
            if (!Finite(position.x) || !Finite(position.y) || !Finite(radius)) return false;
            Vector2Int center = WorldToCell(position);
            if (ReadCell(center.x, center.y) != LevelCell.Floor) return false;
            radius = Mathf.Max(0f, radius);
            if (radius == 0f) return true;

            Vector2Int minimum = WorldToCell(position - Vector2.one * radius);
            Vector2Int maximum = WorldToCell(position + Vector2.one * radius);
            float radiusSquared = radius * radius;
            for (int y = minimum.y; y <= maximum.y; y++)
            for (int x = minimum.x; x <= maximum.x; x++)
            {
                if (ReadCell(x, y) == LevelCell.Floor) continue;
                Vector2 corner = origin + new Vector2(x * cellSize, y * cellSize);
                float nearestX = Mathf.Clamp(position.x, corner.x, corner.x + cellSize);
                float nearestY = Mathf.Clamp(position.y, corner.y, corner.y + cellSize);
                float dx = position.x - nearestX, dy = position.y - nearestY;
                if (dx * dx + dy * dy < radiusSquared - 0.000001f) return false;
            }
            return true;
        }

        public Vector2 Move(Vector2 from, Vector2 delta, float radius = 0.2f)
        {
            if (!CanOccupy(from, radius) || !Finite(delta.x) || !Finite(delta.y)) return from;
            float stepLength = Mathf.Min(0.2f, cellSize * 0.25f);
            delta = Vector2.ClampMagnitude(delta, stepLength * 4096f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / stepLength));
            Vector2 step = delta / steps;
            Vector2 result = from;
            for (int i = 0; i < steps; i++)
            {
                Vector2 next = result + new Vector2(step.x, 0f);
                if (CanOccupy(next, radius)) result = next;
                next = result + new Vector2(0f, step.y);
                if (CanOccupy(next, radius)) result = next;
            }
            return result;
        }

        public bool TryFindSpawn(out Vector2 position)
        {
            EnsureData();
            position = playerStart;
            if (CanOccupy(position)) return true;
            bool found = false;
            float nearest = float.PositiveInfinity;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (ReadCell(x, y) != LevelCell.Floor) continue;
                Vector2 candidate = CellCenter(x, y);
                float distance = (candidate - playerStart).sqrMagnitude;
                if (distance >= nearest || !CanOccupy(candidate)) continue;
                position = candidate;
                nearest = distance;
                found = true;
            }
            return found;
        }

        public bool Raycast(Vector2 start, Vector2 direction, float maxDepth, out LevelRayHit hit)
        {
            EnsureData();
            hit = new LevelRayHit { depth = float.PositiveInfinity };
            if (!Finite(start.x) || !Finite(start.y) || !Finite(direction.x) || !Finite(direction.y)
                || float.IsNaN(maxDepth) || maxDepth < 0f) return false;
            int stepX = Mathf.Abs(direction.x) < 0.0000001f ? 0 : (direction.x > 0f ? 1 : -1);
            int stepY = Mathf.Abs(direction.y) < 0.0000001f ? 0 : (direction.y > 0f ? 1 : -1);
            if (stepX == 0 && stepY == 0) return false;
            Vector2Int cell = WorldToCell(start);
            int x = cell.x, y = cell.y;
            if (ReadCell(x, y) != LevelCell.Floor)
                return MakeHit(start, direction, x, y, 0f, true, out hit);

            float nextX = stepX == 0 ? float.PositiveInfinity
                : (origin.x + (x + (stepX > 0 ? 1 : 0)) * cellSize - start.x) / direction.x;
            float nextY = stepY == 0 ? float.PositiveInfinity
                : (origin.y + (y + (stepY > 0 ? 1 : 0)) * cellSize - start.y) / direction.y;
            nextX = Mathf.Max(0f, nextX);
            nextY = Mathf.Max(0f, nextY);
            float addX = stepX == 0 ? float.PositiveInfinity : cellSize / Mathf.Abs(direction.x);
            float addY = stepY == 0 ? float.PositiveInfinity : cellSize / Mathf.Abs(direction.y);

            for (int crossed = 0; crossed < width + height + 2; crossed++)
            {
                float depth = Mathf.Min(nextX, nextY);
                if (depth > maxDepth || float.IsInfinity(depth)) return false;
                bool corner = Mathf.Abs(nextX - nextY) <= 0.000001f * Mathf.Max(1f, depth);
                if (corner)
                {
                    if (ReadCell(x + stepX, y) != LevelCell.Floor)
                        return MakeHit(start, direction, x + stepX, y, depth, true, out hit);
                    if (ReadCell(x, y + stepY) != LevelCell.Floor)
                        return MakeHit(start, direction, x, y + stepY, depth, false, out hit);
                    x += stepX;
                    y += stepY;
                    nextX += addX;
                    nextY += addY;
                    if (ReadCell(x, y) != LevelCell.Floor)
                        return MakeHit(start, direction, x, y, depth, true, out hit);
                }
                else
                {
                    bool sideX = nextX < nextY;
                    if (sideX) { x += stepX; nextX += addX; }
                    else { y += stepY; nextY += addY; }
                    if (ReadCell(x, y) != LevelCell.Floor)
                        return MakeHit(start, direction, x, y, depth, sideX, out hit);
                }
            }
            return false;
        }

        private bool MakeHit(Vector2 start, Vector2 direction, int x, int y, float depth,
            bool sideX, out LevelRayHit hit)
        {
            Vector2 point = start + direction * depth;
            float u = sideX ? (point.y - origin.y) / cellSize - y
                            : (point.x - origin.x) / cellSize - x;
            if ((sideX && direction.x > 0f) || (!sideX && direction.y < 0f)) u = 1f - u;
            hit = new LevelRayHit
            {
                depth = depth, u = Mathf.Clamp01(u), x = x, y = y,
                sideX = sideX, cell = ReadCell(x, y)
            };
            return true;
        }

        private LevelCell ReadCell(int x, int y)
        {
            return x < 0 || y < 0 || x >= width || y >= height ? LevelCell.Wall : cells[y * width + x];
        }

        private void EnsureData()
        {
            width = Mathf.Clamp(width, 4, 64);
            height = Mathf.Clamp(height, 4, 64);
            cellSize = Finite(cellSize) ? Mathf.Clamp(cellSize, 0.5f, 4f) : 2f;
            if (cells != null && storedWidth == width && storedHeight == height && cells.Length == width * height)
                return;
            int oldWidth = storedWidth > 0 ? storedWidth : width;
            int oldHeight = storedHeight > 0 ? storedHeight : (cells != null ? cells.Length / oldWidth : 0);
            LevelCell[] resized = new LevelCell[width * height];
            if (cells != null)
                for (int y = 0; y < Mathf.Min(height, oldHeight); y++)
                for (int x = 0; x < Mathf.Min(width, oldWidth); x++)
                    if (y * oldWidth + x < cells.Length) resized[y * width + x] = cells[y * oldWidth + x];
            cells = resized;
            storedWidth = width;
            storedHeight = height;
            unchecked { revision++; }
        }

        public void Touch()
        {
            EnsureData();
            unchecked { revision++; }
        }

        private void OnValidate()
        {
            roomHeight = Finite(roomHeight) ? Mathf.Max(0.1f, roomHeight) : 3.4f;
            tileSize = Finite(tileSize) ? Mathf.Max(0.01f, tileSize) : 3f;
            Touch();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
