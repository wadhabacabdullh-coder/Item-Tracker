namespace ShadowContract.Core
{
    /// <summary>Axis-aligned rectangle in world units (x,y = bottom-left).</summary>
    public struct Rect
    {
        public float X, Y, W, H;
        public Rect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public Vec2 Center => new Vec2(X + W * 0.5f, Y + H * 0.5f);
        public bool Contains(Vec2 p) => p.x >= X && p.x <= X + W && p.y >= Y && p.y <= Y + H;
        public override string ToString() => $"Rect({X},{Y},{W},{H})";
    }
}
