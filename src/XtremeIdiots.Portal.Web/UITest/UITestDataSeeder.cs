using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;

namespace XtremeIdiots.Portal.Web.UITest;

/// <summary>
/// Service to seed test data for UITest mode
/// </summary>
public class UITestDataSeeder
{
    private readonly IdentityDataContext _identityContext;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<UITestDataSeeder> _logger;

    public UITestDataSeeder(
        IdentityDataContext identityContext,
        UserManager<IdentityUser> userManager,
        ILogger<UITestDataSeeder> logger)
    {
        _identityContext = identityContext;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the database with test data for UITest mode
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure database is created
            await _identityContext.Database.EnsureCreatedAsync(cancellationToken);

            // Seed test user
            await SeedTestUserAsync(cancellationToken);

            _logger.LogInformation("UITest data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding UITest data");
            throw;
        }
    }

    private async Task SeedTestUserAsync(CancellationToken cancellationToken)
    {
        const string testUserId = "1";
        const string testUsername = "uitest@xtremeidiots.com";
        const string testEmail = "uitest@xtremeidiots.com";

        var existingUser = await _userManager.FindByNameAsync(testUsername);
        if (existingUser is not null)
        {
            _logger.LogDebug("Test user already exists");
            return;
        }

        var testUser = new IdentityUser
        {
            Id = testUserId,
            UserName = testUsername,
            Email = testEmail,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(testUser);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create test user: {errors}");
        }

        // Add claims for full access
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, testUserId),
            new(ClaimTypes.Name, "UITest User"),
            new(ClaimTypes.Email, testEmail),
            new(UserProfileClaimType.XtremeIdiotsId, testUserId),
            new(UserProfileClaimType.UserProfileId, testUserId),
            new(UserProfileClaimType.SeniorAdmin, "true"), // Senior admin gets access to everything
            new(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty2.ToString()),
            new(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()),
            new(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString()),
            new(UserProfileClaimType.GameAdmin, GameType.CallOfDuty2.ToString()),
            new(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()),
            new(UserProfileClaimType.GameAdmin, GameType.CallOfDuty5.ToString())
        };

        await _userManager.AddClaimsAsync(testUser, claims);

        _logger.LogInformation("Created test user with ID {UserId} and username {Username}", testUserId, testUsername);
    }
}
