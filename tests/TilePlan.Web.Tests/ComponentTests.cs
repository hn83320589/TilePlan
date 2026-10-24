using Bunit;
using TilePlan.Core.Geometry;
using TilePlan.Web.Components;

namespace TilePlan.Web.Tests;

public class ComponentTests : BunitContext
{
    [Fact]
    public void Area_text_shows_m2_and_ping_with_two_decimals()
    {
        var cut = Render<AreaText>(p => p.Add(x => x.Area, Area.FromCm2(300 * 200)));

        cut.Find(".m2").MarkupMatches("<span class=\"m2\">6.00 m²</span>");
        cut.Find(".ping").MarkupMatches("<span class=\"ping\">1.82 坪</span>");
    }

    [Fact]
    public void Area_text_after_exclusion()
    {
        var cut = Render<AreaText>(p => p.Add(x => x.Area, Area.FromCm2(300 * 200 - 60 * 60)));

        Assert.Equal("5.64 m²", cut.Find(".m2").TextContent);
    }
}
