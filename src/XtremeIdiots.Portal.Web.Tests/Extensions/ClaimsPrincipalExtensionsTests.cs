using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void Username_WithValidClaim_ReturnsUsername()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "TestUser")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.Username();

        // Assert
        Assert.Equal("TestUser", result);
    }

    [Fact]
    public void Username_WithoutClaim_ReturnsNull()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = principal.Username();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Email_WithValidClaim_ReturnsEmail()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.Email();

        // Assert
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public void Email_WithoutClaim_ReturnsNull()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = principal.Email();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void XtremeIdiotsId_WithValidClaim_ReturnsId()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.XtremeIdiotsId, "12345")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.XtremeIdiotsId();

        // Assert
        Assert.Equal("12345", result);
    }

    [Fact]
    public void UserProfileId_WithValidClaim_ReturnsId()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.UserProfileId, "profile-123")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.UserProfileId();

        // Assert
        Assert.Equal("profile-123", result);
    }

    [Fact]
    public void PhotoUrl_WithValidClaim_ReturnsUrl()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.PhotoUrl, "https://example.com/photo.jpg")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.PhotoUrl();

        // Assert
        Assert.Equal("https://example.com/photo.jpg", result);
    }

    [Fact]
    public void ClaimedGamesAndItems_WithSeniorAdminClaim_ReturnsAllGameTypes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.SeniorAdmin, "true")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.SeniorAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItems(requiredClaims);

        // Assert
        Assert.NotEmpty(gameTypes);
        Assert.Empty(itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItems_WithGameTypeClaim_ReturnsSpecificGameType()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.HeadAdmin, "CallOfDuty2")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.HeadAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItems(requiredClaims);

        // Assert
        Assert.Contains(GameType.CallOfDuty2, gameTypes);
        Assert.Empty(itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItems_WithServerGuidClaim_ReturnsServerId()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.BanFileMonitor, serverId.ToString())
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.BanFileMonitor };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItems(requiredClaims);

        // Assert
        Assert.Empty(gameTypes);
        Assert.Contains(serverId, itemIds);
    }

    [Fact]
    public void ClaimedGameTypes_WithSeniorAdminClaim_ReturnsAllGameTypes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.SeniorAdmin, "true")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.SeniorAdmin };

        // Act
        var result = principal.ClaimedGameTypes(requiredClaims);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetGameTypesForGameServers_WithHeadAdminClaim_ReturnsGameTypes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.HeadAdmin, "CallOfDuty4")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = principal.GetGameTypesForGameServers();

        // Assert
        Assert.Contains(GameType.CallOfDuty4, result);
    }

    [Fact]
    public void ClaimedGamesAndItems_WithNoClaims_ReturnsEmptyArrays()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var requiredClaims = new[] { UserProfileClaimType.GameAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItems(requiredClaims);

        // Assert
        Assert.Empty(gameTypes);
        Assert.Empty(itemIds);
    }

    #region ClaimedGamesAndItemsForViewing Tests

    [Fact]
    public void ClaimedGamesAndItemsForViewing_WithSeniorAdmin_ReturnsAllGameTypes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.SeniorAdmin, "true")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.SeniorAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItemsForViewing(requiredClaims);

        // Assert
        var allGameTypes = Enum.GetValues<GameType>();
        Assert.Equal(allGameTypes.Length, gameTypes.Length);
        Assert.Empty(itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItemsForViewing_WithSingleGameTypeClaim_ReturnsAllGameTypes()
    {
        // Arrange — HeadAdmin for COD2 only
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.HeadAdmin, "CallOfDuty2")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.HeadAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItemsForViewing(requiredClaims);

        // Assert — should see ALL game types for viewing, not just COD2
        var allGameTypes = Enum.GetValues<GameType>();
        Assert.Equal(allGameTypes.Length, gameTypes.Length);
        Assert.Empty(itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItemsForViewing_WithServerGuidClaim_ReturnsAllGameTypesAndServerId()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.BanFileMonitor, serverId.ToString())
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.BanFileMonitor };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItemsForViewing(requiredClaims);

        // Assert — should see ALL game types for viewing, and the specific server ID
        var allGameTypes = Enum.GetValues<GameType>();
        Assert.Equal(allGameTypes.Length, gameTypes.Length);
        Assert.Contains(serverId, itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItemsForViewing_WithNoClaims_ReturnsEmptyArrays()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var requiredClaims = new[] { UserProfileClaimType.GameAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItemsForViewing(requiredClaims);

        // Assert
        Assert.Empty(gameTypes);
        Assert.Empty(itemIds);
    }

    [Fact]
    public void ClaimedGamesAndItemsForViewing_WithUnrelatedClaim_ReturnsEmptyArrays()
    {
        // Arrange — has a Moderator claim but we're checking for HeadAdmin
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.Moderator, "CallOfDuty4")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.HeadAdmin };

        // Act
        var (gameTypes, itemIds) = principal.ClaimedGamesAndItemsForViewing(requiredClaims);

        // Assert — claim doesn't match required claims, so empty
        Assert.Empty(gameTypes);
        Assert.Empty(itemIds);
    }

    #endregion

    #region ClaimedGameTypesForViewing Tests

    [Fact]
    public void ClaimedGameTypesForViewing_WithSeniorAdmin_ReturnsAllGameTypes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.SeniorAdmin, "true")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.SeniorAdmin };

        // Act
        var result = principal.ClaimedGameTypesForViewing(requiredClaims);

        // Assert
        var allGameTypes = Enum.GetValues<GameType>();
        Assert.Equal(allGameTypes.Length, result.Count);
    }

    [Fact]
    public void ClaimedGameTypesForViewing_WithSingleGameAdminClaim_ReturnsAllGameTypes()
    {
        // Arrange — GameAdmin for COD4 only
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.GameAdmin, "CallOfDuty4")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.GameAdmin };

        // Act
        var result = principal.ClaimedGameTypesForViewing(requiredClaims);

        // Assert — should see ALL game types for viewing
        var allGameTypes = Enum.GetValues<GameType>();
        Assert.Equal(allGameTypes.Length, result.Count);
    }

    [Fact]
    public void ClaimedGameTypesForViewing_WithNoClaims_ReturnsEmpty()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var requiredClaims = new[] { UserProfileClaimType.GameAdmin };

        // Act
        var result = principal.ClaimedGameTypesForViewing(requiredClaims);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ClaimedGameTypesForViewing_VsClaimedGameTypes_ViewingReturnsMore()
    {
        // Arrange — GameAdmin for COD2 only
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.GameAdmin, "CallOfDuty2")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var requiredClaims = new[] { UserProfileClaimType.GameAdmin };

        // Act
        var restrictedResult = principal.ClaimedGameTypes(requiredClaims);
        var viewingResult = principal.ClaimedGameTypesForViewing(requiredClaims);

        // Assert — restricted returns only COD2, viewing returns all
        Assert.Single(restrictedResult);
        Assert.Equal(GameType.CallOfDuty2, restrictedResult[0]);
        Assert.True(viewingResult.Count > restrictedResult.Count);
    }

    #endregion
}
