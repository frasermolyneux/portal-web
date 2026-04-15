using Microsoft.ApplicationInsights.DataContracts;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Demos;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class TelemetryExtensions
{
    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, ClaimsPrincipal claimsPrincipal)
    {
        exceptionTelemetry.Properties.TryAdd("LoggedInAdminId", claimsPrincipal.XtremeIdiotsId());
        exceptionTelemetry.Properties.TryAdd("LoggedInUsername", claimsPrincipal.Username());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, AdminActionDto adminActionDto)
    {
        exceptionTelemetry.Properties.TryAdd("PlayerId", adminActionDto.PlayerId.ToString());
        exceptionTelemetry.Properties.TryAdd("AdminActionId", adminActionDto.AdminActionId.ToString());
        exceptionTelemetry.Properties.TryAdd("AdminActionType", adminActionDto.Type.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, CreateAdminActionDto createAdminActionDto)
    {
        exceptionTelemetry.Properties.TryAdd("PlayerId", createAdminActionDto.PlayerId.ToString());
        exceptionTelemetry.Properties.TryAdd("AdminActionType", createAdminActionDto.Type.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, EditAdminActionDto editAdminActionDto)
    {
        exceptionTelemetry.Properties.TryAdd("AdminActionId", editAdminActionDto.AdminActionId.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, PlayerDto playerDto)
    {
        exceptionTelemetry.Properties.TryAdd("PlayerId", playerDto.PlayerId.ToString());
        exceptionTelemetry.Properties.TryAdd("GameType", playerDto.GameType.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, GameServerDto gameServerDto)
    {
        exceptionTelemetry.Properties.TryAdd("GameServerId", gameServerDto.GameServerId.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, BanFileMonitorDto banFileMonitorDto)
    {
        exceptionTelemetry.Properties.TryAdd("BanFileMonitorId", banFileMonitorDto.BanFileMonitorId.ToString());
        exceptionTelemetry.Properties.TryAdd("GameServerId", banFileMonitorDto.GameServerId.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, CreateBanFileMonitorDto createBanFileMonitorDto)
    {
        exceptionTelemetry.Properties.TryAdd("GameServerId", createBanFileMonitorDto.GameServerId.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, EditBanFileMonitorDto editBanFileMonitorDto)
    {
        exceptionTelemetry.Properties.TryAdd("BanFileMonitorId", editBanFileMonitorDto.BanFileMonitorId.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, DemoDto demoDto)
    {
        exceptionTelemetry.Properties.TryAdd("DemoId", demoDto.DemoId.ToString());
        exceptionTelemetry.Properties.TryAdd("GameType", demoDto.GameType.ToString());
        exceptionTelemetry.Properties.TryAdd("DemoTitle", demoDto.Title ?? "Unknown");

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, TagDto tagDto)
    {
        exceptionTelemetry.Properties.TryAdd("TagId", tagDto.TagId.ToString());
        exceptionTelemetry.Properties.TryAdd("TagName", tagDto.Name);
        exceptionTelemetry.Properties.TryAdd("UserDefined", tagDto.UserDefined.ToString());

        return exceptionTelemetry;
    }

    public static ExceptionTelemetry Enrich(this ExceptionTelemetry exceptionTelemetry, UserProfileDto userProfileDto)
    {
        exceptionTelemetry.Properties.TryAdd("UserProfileId", userProfileDto.UserProfileId.ToString());
        exceptionTelemetry.Properties.TryAdd("DisplayName", userProfileDto.DisplayName ?? "Unknown");

        return exceptionTelemetry;
    }
}