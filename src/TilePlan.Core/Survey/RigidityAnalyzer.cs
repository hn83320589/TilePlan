using TilePlan.Core.Geometry;

namespace TilePlan.Core.Survey;

/// <summary>
/// 判斷一組距離量測是否足以決定 n 邊形的形狀（扣掉平移與旋轉共 2n − 3 個自由度）。
/// 做法：在隨機的一般位置組出剛性矩陣，秩 = 2n − 3 即為剛性。
/// </summary>
public static class RigidityAnalyzer
{
    private const int Seed = 20261024;

    public static int RequiredRank(int vertexCount) => 2 * vertexCount - 3;

    public static int Rank(int vertexCount, IEnumerable<(int From, int To)> bars) =>
        RankOf(BuildRows(GenericPositions(vertexCount), bars), 2 * vertexCount);

    /// <summary>
    /// 貪婪挑選能補足剛性的對角線：優先不與既有對角線交錯、跨度短的頂點對。
    /// </summary>
    public static IReadOnlyList<(int From, int To)> SuggestDiagonals(int vertexCount, IReadOnlyList<(int From, int To)> bars)
    {
        var positions = GenericPositions(vertexCount);
        var rows = BuildRows(positions, bars);
        int rank = RankOf(rows, 2 * vertexCount);
        int required = RequiredRank(vertexCount);
        var chosen = new List<(int, int)>();
        var existing = new HashSet<(int, int)>(bars.Select(b => Normalize(b.From, b.To)));

        var candidates = new List<(int From, int To)>();
        for (int i = 0; i < vertexCount; i++)
            for (int j = i + 2; j < vertexCount; j++)
                if (!(i == 0 && j == vertexCount - 1) && !existing.Contains((i, j)))
                    candidates.Add((i, j));

        var ordered = candidates
            .OrderBy(c => bars.Count(b => Interleaves(c, b, vertexCount)))
            .ThenBy(c => Math.Min(c.To - c.From, vertexCount - (c.To - c.From)))
            .ThenBy(c => c.From).ThenBy(c => c.To)
            .ToList();

        foreach (var c in ordered)
        {
            if (rank >= required) break;
            rows.Add(Row(positions, c.From, c.To));
            int newRank = RankOf(rows, 2 * vertexCount);
            if (newRank > rank) { chosen.Add(c); rank = newRank; }
            else rows.RemoveAt(rows.Count - 1);
        }
        return chosen;
    }

    private static (int, int) Normalize(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>兩條弦的端點在多邊形頂點順序上是否交錯（內部對角線交錯 ⇔ 相交）。</summary>
    private static bool Interleaves((int From, int To) a, (int From, int To) b, int n)
    {
        var (a1, a2) = Normalize(a.From, a.To);
        var (b1, b2) = Normalize(b.From, b.To);
        if (a1 == b1 || a1 == b2 || a2 == b1 || a2 == b2) return false;
        bool b1Inside = b1 > a1 && b1 < a2;
        bool b2Inside = b2 > a1 && b2 < a2;
        return b1Inside != b2Inside;
    }

    private static Vec2[] GenericPositions(int n)
    {
        var rng = new Random(Seed);
        return Enumerable.Range(0, n).Select(_ => new Vec2(rng.NextDouble() * 100, rng.NextDouble() * 100)).ToArray();
    }

    private static List<double[]> BuildRows(Vec2[] p, IEnumerable<(int From, int To)> bars) =>
        bars.Select(b => Row(p, b.From, b.To)).ToList();

    private static double[] Row(Vec2[] p, int i, int j)
    {
        var row = new double[2 * p.Length];
        var d = p[i] - p[j];
        row[2 * i] = d.X; row[2 * i + 1] = d.Y;
        row[2 * j] = -d.X; row[2 * j + 1] = -d.Y;
        return row;
    }

    private static int RankOf(List<double[]> rows, int cols)
    {
        var m = rows.Select(r => (double[])r.Clone()).ToArray();
        int rank = 0;
        for (int c = 0; c < cols && rank < m.Length; c++)
        {
            int pivot = rank;
            for (int r = rank + 1; r < m.Length; r++)
                if (Math.Abs(m[r][c]) > Math.Abs(m[pivot][c])) pivot = r;
            if (Math.Abs(m[pivot][c]) < 1e-8) continue;
            (m[rank], m[pivot]) = (m[pivot], m[rank]);
            for (int r = 0; r < m.Length; r++)
            {
                if (r == rank) continue;
                double f = m[r][c] / m[rank][c];
                if (f == 0) continue;
                for (int k = c; k < cols; k++) m[r][k] -= f * m[rank][k];
            }
            rank++;
        }
        return rank;
    }
}
