using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoomProjection
{
    public static class ProjectionVerification
    {
        public static void Run()
        {
            var report = new StringBuilder("Sprite projection checks\n");
            int passed = 0;
            int failed = 0;

            void Check(string name, Action test)
            {
                try
                {
                    test();
                    report.AppendLine("PASS: " + name);
                    passed++;
                }
                catch (Exception error)
                {
                    report.AppendLine("FAIL: " + name + " - " + error.Message);
                    failed++;
                }
            }

            Check("Centered object: known pixel rectangle and depth", () =>
            {
                bool visible = SpriteProjection.TryProject(Vector2.zero, 0, 1,
                    new Vector2(0, 4), 0, new Vector2(1, 2), new Vector2(800, 600),
                    90, out Rect rect, out float depth);
                Require(visible);
                Match(rect, 350, 200, 100, 200);
                Near(depth, 4);
            });

            Check("Twice the depth gives half the size", () =>
                Match(Project(new Vector2(0, 8)), 375, 250, 50, 100));

            Check("Object left and right positions", () =>
            {
                Near(Project(new Vector2(1, 4)).x, 450);
                Near(Project(new Vector2(-1, 4)).x, 250);
            });

            Check("Player translation changes relative position", () =>
            {
                Match(Project(new Vector2(1, 6), player: new Vector2(1, 2)),
                    350, 200, 100, 200);
                Near(Project(new Vector2(0, 4), player: new Vector2(1, 0)).x, 250);
            });

            Check("90 degree turn: forward and right axes", () =>
            {
                Match(Project(new Vector2(4, 0), yaw: 90), 350, 200, 100, 200);
                Near(Project(new Vector2(4, -1), yaw: 90).x, 450);
            });

            Check("Higher object base moves the image upward", () =>
                Match(Project(new Vector2(0, 4), baseHeight: 1), 350, 300, 100, 200));

            Check("Higher eye moves the image downward", () =>
                Match(Project(new Vector2(0, 4), eye: 2), 350, 100, 100, 200));

            Check("Behind and on the player plane are hidden", () =>
            {
                Require(!Visible(new Vector2(0, -4)));
                Require(!Visible(Vector2.zero));
            });

            Check("Near depth rejects unsafe division but includes the boundary", () =>
            {
                Require(!Visible(new Vector2(0, SpriteProjection.NearDepth * 0.5f)));
                Require(Visible(new Vector2(0, SpriteProjection.NearDepth)));
            });

            Check("Fully offscreen horizontally is hidden", () =>
            {
                Require(!Visible(new Vector2(5, 4)));
                Require(!Visible(new Vector2(-5, 4)));
            });

            Check("Partially offscreen horizontally keeps its full rectangle", () =>
            {
                Match(Project(new Vector2(4, 4)), 750, 200, 100, 200);
                Match(Project(new Vector2(-4, 4)), -50, 200, 100, 200);
            });

            Check("Vertical culling preserves partially visible objects", () =>
            {
                Near(Project(new Vector2(0, 4), baseHeight: 3.5f).y, 550);
                Require(!Visible(new Vector2(0, 4), baseHeight: 6));
                Require(!Visible(new Vector2(0, 4), baseHeight: -6));
            });

            Check("World width and height preserve the aspect ratio", () =>
                Match(Project(new Vector2(0, 4), size: new Vector2(2, 1)),
                    300, 200, 200, 100));

            Check("Double resolution doubles all pixel coordinates", () =>
                Match(Project(new Vector2(0, 4), screen: new Vector2(1600, 1200)),
                    700, 400, 200, 400));

            Check("60 degree horizontal FOV gives the expected magnification", () =>
                Match(Project(new Vector2(0, 4), fov: 60),
                    313.39746f, 126.79492f, 173.20508f, 346.41016f));

            Check("Invalid dimensions and FOV are rejected", () =>
            {
                Require(!Visible(new Vector2(0, 4), size: new Vector2(0, 2)));
                Require(!Visible(new Vector2(0, 4), size: new Vector2(1, -2)));
                Require(!Visible(new Vector2(0, 4), screen: new Vector2(0, 600)));
                Require(!Visible(new Vector2(0, 4), screen: new Vector2(800, -1)));
                Require(!Visible(new Vector2(0, 4), fov: 0));
                Require(!Visible(new Vector2(0, 4), fov: 180));
            });

            report.AppendLine($"Result: {passed} passed, {failed} failed.");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SpriteProjectionChecks.txt"), report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        private static Rect Project(Vector2 position, float yaw = 0, Vector2? player = null,
            float eye = 1, float baseHeight = 0, Vector2? size = null,
            Vector2? screen = null, float fov = 90)
        {
            bool visible = SpriteProjection.TryProject(player ?? Vector2.zero, yaw, eye,
                position, baseHeight, size ?? new Vector2(1, 2), screen ?? new Vector2(800, 600),
                fov, out Rect rect, out _);
            Require(visible);
            return rect;
        }

        private static bool Visible(Vector2 position, float baseHeight = 0,
            Vector2? size = null, Vector2? screen = null, float fov = 90)
        {
            return SpriteProjection.TryProject(Vector2.zero, 0, 1, position, baseHeight,
                size ?? new Vector2(1, 2), screen ?? new Vector2(800, 600), fov, out _, out _);
        }

        private static void Match(Rect rect, float x, float y, float width, float height)
        {
            Near(rect.x, x);
            Near(rect.y, y);
            Near(rect.width, width);
            Near(rect.height, height);
        }

        private static void Near(float actual, float expected)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > 0.01f)
                throw new Exception($"Expected {expected}, got {actual}.");
        }

        private static void Require(bool condition)
        {
            if (!condition)
                throw new Exception("Visibility or validation did not match the expectation.");
        }
    }
}
