namespace TilePlan.Core.Geometry;

/// <summary>平面上的點或向量，單位為公分；y 軸向上（數學座標）。</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    public static Vec2 operator *(double k, Vec2 a) => new(a.X * k, a.Y * k);
    public static Vec2 operator /(Vec2 a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized() => this / Length;

    /// <summary>逆時針旋轉 90°。</summary>
    public Vec2 Perp() => new(-Y, X);

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;

    /// <summary>二維外積（z 分量）；&gt; 0 表示 o 在本向量的左側。</summary>
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;

    public Vec2 Rotate(double radians)
    {
        var (s, c) = Math.SinCos(radians);
        return new(X * c - Y * s, X * s + Y * c);
    }

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public double DistanceTo(Vec2 o) => (this - o).Length;

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
