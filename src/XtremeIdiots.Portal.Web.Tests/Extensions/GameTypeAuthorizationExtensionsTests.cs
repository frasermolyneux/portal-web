using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class GameTypeAuthorizationExtensionsTests
{
    [Fact]
    public void TeamAccessGameTypes_ContainsOnlySupportedGames()
    {
        Assert.Equal(
            [GameType.CallOfDuty2, GameType.CallOfDuty4, GameType.CallOfDuty5],
            GameTypeAuthorizationExtensions.TeamAccessGameTypes);
    }

    [Theory]
    [InlineData(GameType.CallOfDuty4, GameType.CallOfDuty4)]
    [InlineData(GameType.CallOfDuty4x, GameType.CallOfDuty4)]
    [InlineData(GameType.CallOfDuty5, GameType.CallOfDuty5)]
    public void NormalizeTeamAccessGameType_ReturnsCanonicalGameType(GameType gameType, GameType expected)
    {
        Assert.Equal(expected, GameTypeAuthorizationExtensions.NormalizeTeamAccessGameType(gameType));
    }

    [Fact]
    public void GetTeamAccessIncludedGameTypes_Cod4_IncludesCod4x()
    {
        Assert.Equal(
            [GameType.CallOfDuty4, GameType.CallOfDuty4x],
            GameTypeAuthorizationExtensions.GetTeamAccessIncludedGameTypes(GameType.CallOfDuty4));
    }
}
