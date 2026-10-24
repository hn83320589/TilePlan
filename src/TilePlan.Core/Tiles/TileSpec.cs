namespace TilePlan.Core.Tiles;

/// <summary>磁磚規格。長寬為公分；厚度與縫寬依現場習慣以 mm 輸入。</summary>
public sealed record TileSpec(
    double LengthCm,
    double WidthCm,
    double ThicknessMm,
    double JointMm,
    int PiecesPerBox,
    decimal PricePerBox)
{
    public double JointCm => JointMm / 10;

    public double ThicknessCm => ThicknessMm / 10;

    public double AreaCm2 => LengthCm * WidthCm;

    public void Validate()
    {
        if (!(LengthCm > 0) || !(WidthCm > 0)) throw new ArgumentException("磁磚長寬必須大於 0。");
        if (!(ThicknessMm > 0)) throw new ArgumentException("磁磚厚度必須大於 0。");
        if (JointMm < 0) throw new ArgumentException("縫寬不可為負。");
        if (PiecesPerBox <= 0) throw new ArgumentException("每箱片數必須大於 0。");
        if (PricePerBox < 0) throw new ArgumentException("每箱單價不可為負。");
    }
}
