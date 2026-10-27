using Microsoft.AspNetCore.WebUtilities;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class RedirectManagerLinksTests
{
    [Theory]
    [InlineData(GameType.CallOfDuty4, "cod4")]
    [InlineData(GameType.CallOfDuty4x, "cod4")]
    [InlineData(GameType.CallOfDuty5, "cod5")]
    public void MapLinks_PreserveNamesAsEncodedQueryValuesAndNormalizeGameAliases(GameType gameType, string expectedGame)
    {
        const string mapName = "mp_Map With & Symbols";

        var uri = new Uri(gameType.RedirectManagerUrl(mapName)!);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("maps.xtremeidiots.com", uri.Host);
        Assert.Equal(expectedGame, query["game"]);
        Assert.Equal(mapName, query["map"]);
        Assert.Equal(2, query.Count);
    }

    [Fact]
    public void BuiltInAndUnsupportedMaps_DoNotAdvertiseNonexistentRedirectDownloads()
    {
        Assert.Null(GameType.CallOfDuty4.RedirectManagerUrl("mp_backlot"));
        Assert.Null(GameType.CallOfDuty4x.RedirectManagerUrl("mp_bog"));
        Assert.Null(GameType.CallOfDuty2.RedirectManagerUrl("mp_custom"));
        Assert.DoesNotContain("map=", GameType.CallOfDuty4.RedirectManagerUrl());
    }
}
