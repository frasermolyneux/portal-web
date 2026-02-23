# Server Administration Permissions Matrix

Source: `AuthPolicies`, `ServerAdminAuthHandler`, `BaseAuthorizationHelper`

Legend:
- ✓ Granted
- g Game-type scope
- ✗ Not granted

Roles / Claims:
- SeniorAdmin
- HeadAdmin (game)
- GameAdmin (game)
- Moderator (game)
- ServerAdmin (game) (additional admin-esque claim)
- LiveRcon (game)

| Capability / Policy                                          | SeniorAdmin | HeadAdmin (game) | GameAdmin (game) | Moderator (game) | ServerAdmin (game) | LiveRcon (game) | Notes                                                                                                 |
| ------------------------------------------------------------ | ----------- | ---------------- | ---------------- | ---------------- | ------------------ | --------------- | ----------------------------------------------------------------------------------------------------- |
| Access Server Admin (`AccessServerAdmin`)                    | ✓           | ✓ g              | ✓ g              | ✓ g              | ✓ g                | ✗               | Claim group includes ServerAdmin but excludes LiveRcon                                                |
| Access Live RCON (`AccessLiveRcon`)                          | ✓           | ✓ g              | ✓ g              | ✗                | ✗                  | ✓ g             | LiveRcon group excludes Moderator & ServerAdmin                                                       |
| View Live RCON (`ViewLiveRcon`)                              | ✓           | ✓ g              | ✓ g              | ✗                | ✗                  | ✓ g             | Same as AccessLiveRcon composite                                                                      |
| View Game Chat Log (`ViewGameChatLog`)                       | ✓           | ✓ (all)          | ✓ (all)          | ✗                | ✗                  | ✗               | **See-all**: any admin (excl. moderators) can view game chat logs across all game types               |
| View Global Chat Log (`ViewGlobalChatLog`)                   | ✓           | ✓ g              | ✓ g              | ✗                | ✗                  | ✗               | Excludes moderator/serveradmin/liveRcon                                                               |
| View Server Chat Log (`ViewServerChatLog`)                   | ✓           | ✓ (all)          | ✓ (all)          | ✓ (all)          | ✗                  | ✗               | **See-all**: any admin (incl. moderators) can view server chat logs across all game types             |
| Manage Maps (`ManageMaps`)                                   | ✓           | ✓ g              | ✗                | ✗                | ✗                  | ✗               | Senior & Head only                                                                                    |
| Access Map Manager Controller (`AccessMapManagerController`) | ✓           | ✓ g              | ✗                | ✗                | ✗                  | ✗               | Same group                                                                                            |
| Push Map To Remote (`PushMapToRemote`)                       | ✓           | ✓ g              | ✗                | ✗                | ✗                  | ✗               | Same group                                                                                            |
| Delete Map From Host (`DeleteMapFromHost`)                   | ✓           | ✓ g              | ✗                | ✗                | ✗                  | ✗               | Same group                                                                                            |
| Lock Chat Messages (`LockChatMessages`)                      | ✓           | ✓ g              | ✓ g              | ✓ g              | ✗                  | ✗               | AdminLevelsExcludingModerators + moderator specific game check allows Moderator; ServerAdmin excluded |

## Notes
- **See-all, do-own model**: Chat log viewing (ViewGameChatLog, ViewServerChatLog) is now available across all game types for any qualifying admin. Write/action operations (LockChatMessages, ManageMaps, etc.) remain game-type-scoped.
- ServerAdmin claim only grants AccessServerAdmin page; lacks many specific capabilities.
- LiveRcon limited to RCON access policies; cannot access broader ServerAdmin page.

