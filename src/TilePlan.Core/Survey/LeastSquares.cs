using TilePlan.Core.Geometry;

namespace TilePlan.Core.Survey;

/// <summary>
/// Levenberg–Marquardt：最小化 Σ(|pᵢ − pⱼ| − dᵢⱼ)²。
/// 固定 A 在原點、B 在 x 軸上（去掉平移與旋轉），未知數為其餘 2n − 3 個座標。
/// </summary>
internal static class LeastSquares
{
    public static Vec2[] Refine(Vec2[] initial, IReadOnlyList<(int From, int To, double Length)> bars, int maxIterations = 100)
    {
        int n = initial.Length;
        // 未知數索引：x1, x2, y2, x3, y3, …（x0、y0、y1 固定）
        int m = 2 * n - 3;
        double[] x = Pack(initial);
        double cost = Cost(Unpack(x, n), bars);
        double lambda = 1e-3;
        var row = new (int Index, double Value)[4];

        for (int iter = 0; iter < maxIterations && cost > 1e-18; iter++)
        {
            var p = Unpack(x, n);
            var jtj = new double[m, m];
            var jtr = new double[m];
            foreach (var (i, j, len) in bars)
            {
                var d = p[i] - p[j];
                double dist = d.Length;
                if (dist < 1e-12) continue;
                double r = dist - len;
                var g = d / dist;
                int count = 0;
                Add(ref count, row, VarX(i), g.X); Add(ref count, row, VarY(i), g.Y);
                Add(ref count, row, VarX(j), -g.X); Add(ref count, row, VarY(j), -g.Y);
                for (int a = 0; a < count; a++)
                {
                    jtr[row[a].Index] += row[a].Value * r;
                    for (int b = 0; b < count; b++)
                        jtj[row[a].Index, row[b].Index] += row[a].Value * row[b].Value;
                }
            }

            var system = (double[,])jtj.Clone();
            for (int k = 0; k < m; k++) system[k, k] += lambda * (1 + jtj[k, k]);
            var step = Solve(system, jtr.Select(v => -v).ToArray());
            if (step is null) break;

            var candidate = x.Zip(step, (a, b) => a + b).ToArray();
            double newCost = Cost(Unpack(candidate, n), bars);
            if (newCost < cost)
            {
                bool converged = step.Max(Math.Abs) < 1e-10;
                x = candidate; cost = newCost; lambda = Math.Max(lambda / 3, 1e-12);
                if (converged) break;
            }
            else
            {
                lambda *= 4;
                if (lambda > 1e12) break;
            }
        }
        return Unpack(x, n);
    }

    private static void Add(ref int count, (int, double)[] row, int index, double value)
    {
        if (index >= 0) row[count++] = (index, value);
    }

    private static int VarX(int v) => v == 0 ? -1 : v == 1 ? 0 : 2 * v - 3;
    private static int VarY(int v) => v <= 1 ? -1 : 2 * v - 2;

    private static double[] Pack(Vec2[] p)
    {
        var x = new double[2 * p.Length - 3];
        x[0] = p[1].X;
        for (int v = 2; v < p.Length; v++) { x[VarX(v)] = p[v].X; x[VarY(v)] = p[v].Y; }
        return x;
    }

    private static Vec2[] Unpack(double[] x, int n)
    {
        var p = new Vec2[n];
        p[0] = Vec2.Zero;
        p[1] = new Vec2(x[0], 0);
        for (int v = 2; v < n; v++) p[v] = new Vec2(x[VarX(v)], x[VarY(v)]);
        return p;
    }

    private static double Cost(Vec2[] p, IReadOnlyList<(int From, int To, double Length)> bars) =>
        bars.Sum(b => { double r = p[b.From].DistanceTo(p[b.To]) - b.Length; return r * r; });

    /// <summary>高斯消去（部分選主元）；奇異時回傳 null。</summary>
    private static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
                if (Math.Abs(a[r, c]) > Math.Abs(a[pivot, c])) pivot = r;
            if (Math.Abs(a[pivot, c]) < 1e-14) return null;
            if (pivot != c)
            {
                for (int k = 0; k < n; k++) (a[c, k], a[pivot, k]) = (a[pivot, k], a[c, k]);
                (b[c], b[pivot]) = (b[pivot], b[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = a[r, c] / a[c, c];
                if (f == 0) continue;
                for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k];
            x[r] = s / a[r, r];
        }
        return x;
    }
}
