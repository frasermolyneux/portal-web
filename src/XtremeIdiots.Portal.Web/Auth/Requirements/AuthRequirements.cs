using Microsoft.AspNetCore.Authorization;

namespace XtremeIdiots.Portal.Web.Auth.Requirements;

// Map Rotations
public class MapRotationsRead : IAuthorizationRequirement { }
public class MapRotationsWrite : IAuthorizationRequirement { }
public class MapRotationsDeploy : IAuthorizationRequirement { }

// Maps
public class MapsRead : IAuthorizationRequirement { }

// Game Servers — Core
public class GameServersRead : IAuthorizationRequirement { }
public class GameServersWrite : IAuthorizationRequirement { }
public class GameServersDelete : IAuthorizationRequirement { }

// Game Servers — Credentials
public class GameServersCredentialsFtpRead : IAuthorizationRequirement { }
public class GameServersCredentialsFtpWrite : IAuthorizationRequirement { }
public class GameServersCredentialsRconRead : IAuthorizationRequirement { }
public class GameServersCredentialsRconWrite : IAuthorizationRequirement { }

// Game Servers — Maps
public class GameServersMapsRead : IAuthorizationRequirement { }
public class GameServersMapsDeploy : IAuthorizationRequirement { }

// Game Servers — Ban File Monitors
public class GameServersBanFileMonitorsRead : IAuthorizationRequirement { }
public class GameServersBanFileMonitorsWrite : IAuthorizationRequirement { }

// Game Servers — Admin
public class GameServersAdminRead : IAuthorizationRequirement { }
public class GameServersAdminRcon : IAuthorizationRequirement { }
public class GameServersAdminRconKick : IAuthorizationRequirement { }
public class GameServersAdminRconBan : IAuthorizationRequirement { }
public class GameServersAdminRconMap : IAuthorizationRequirement { }
public class GameServersAdminRconSay : IAuthorizationRequirement { }
public class GameServersAdminRconRestart : IAuthorizationRequirement { }

// Chat Log
public class ChatLogRead : IAuthorizationRequirement { }
public class ChatLogReadServer : IAuthorizationRequirement { }
public class ChatLogLock : IAuthorizationRequirement { }

// Admin Actions
public class AdminActionsRead : IAuthorizationRequirement { }
public class AdminActionsCreate : IAuthorizationRequirement { }
public class AdminActionsEdit : IAuthorizationRequirement { }
public class AdminActionsDelete : IAuthorizationRequirement { }
public class AdminActionsClaim : IAuthorizationRequirement { }
public class AdminActionsLift : IAuthorizationRequirement { }
public class AdminActionsReassign : IAuthorizationRequirement { }
public class AdminActionsCreateTopic : IAuthorizationRequirement { }

// Players
public class PlayersRead : IAuthorizationRequirement { }
public class PlayersDelete : IAuthorizationRequirement { }
public class PlayersProtectedNamesWrite : IAuthorizationRequirement { }
public class PlayersTagsWrite : IAuthorizationRequirement { }

// Tags
public class TagsRead : IAuthorizationRequirement { }
public class TagsWrite : IAuthorizationRequirement { }

// Dashboard
public class DashboardRead : IAuthorizationRequirement { }

// Demos
public class DemosRead : IAuthorizationRequirement { }
public class DemosWrite : IAuthorizationRequirement { }
public class DemosDelete : IAuthorizationRequirement { }

// Global Settings
public class GlobalSettingsAdmin : IAuthorizationRequirement { }

// Users
public class UsersRead : IAuthorizationRequirement { }
public class UsersManageClaims : IAuthorizationRequirement { }
public class UsersSearch : IAuthorizationRequirement { }
public class UsersActivityLog : IAuthorizationRequirement { }
