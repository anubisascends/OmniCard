using OmniCard.Tests.Services;
using OmniCard.Web.Api;

namespace OmniCard.Tests.Web;

public class MetaControllerSearchFieldsTests
{
    private static MetaController Create() =>
        new([new FakeFfService()]);

    [Fact]
    public void SearchFields_ForGame_IncludesGameSpecificField()
    {
        var result = Create().SearchFields("FinalFantasy");
        var dto = result.Value;
        Assert.NotNull(dto);
        Assert.Equal("FinalFantasy", dto!.Game);

        var element = dto.Fields.SingleOrDefault(f => f.Canonical == "element");
        Assert.NotNull(element);
        Assert.Contains("e", element!.Aliases);
        Assert.Contains("f=Fire", element.ValueAliases);
        Assert.Equal("game", element.Kind);

        // Core fields are inherited too.
        Assert.Contains(dto.Fields, f => f.Canonical == "set");
    }

    [Fact]
    public void SearchFields_NoGame_ReturnsCoreSchema()
    {
        var dto = Create().SearchFields(null).Value;
        Assert.NotNull(dto);
        Assert.Equal("all", dto!.Game);
        Assert.Contains(dto.Fields, f => f.Canonical == "name");
        Assert.DoesNotContain(dto.Fields, f => f.Canonical == "element");
    }
}
