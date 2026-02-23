# Players Permissions Matrix

Source: `AuthPolicies`, `PlayersAuthHandler`, `PlayerTagsAuthHandler`, `BaseAuthorizationHelper`

Legend:
- ✓ Granted
- g Game-type scope
- ✗ Not granted
- O Ownership (resource owner)

Roles / Claims:
- SeniorAdmin
- HeadAdmin (game)
- GameAdmin (game)
- Moderator (game)

| Capability / Policy                           | SeniorAdmin | HeadAdmin (game) | GameAdmin (game) | Moderator (game) | Notes                                                  |
| --------------------------------------------- | ----------- | ---------------- | ---------------- | ---------------- | ------------------------------------------------------ |
| Access Players (`AccessPlayers`)              | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded by claim group                      |
| View Players (`ViewPlayers`)                  | ✓           | ✓ (all)          | ✓ (all)          | ✓ (all)          | **See-all**: any admin can view players across all game types |
| Delete Player (`DeletePlayer`)                | ✓           | ✗                | ✗                | ✗                | SeniorAdmin only (code already)                        |
| Create Protected Name (`CreateProtectedName`) | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded                                     |
| Delete Protected Name (`DeleteProtectedName`) | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded                                     |
| View Protected Name (`ViewProtectedName`)     | ✓           | ✓ g              | ✓ g              | ✓ g              | Mirrors ViewPlayers access                             |
| Access Player Tags (`AccessPlayerTags`)       | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded                                     |
| Create Player Tag (`CreatePlayerTag`)         | ✓           | ✓ g              | ✓ g              | ✗                | Matches AccessPlayerTags                               |
| Edit Player Tag (`EditPlayerTag`)             | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded                                     |
| Delete Player Tag (`DeletePlayerTag`)         | ✓           | ✓ g              | ✓ g              | ✗                | Moderator excluded                                     |

## Notes
- **See-all, do-own model**: Admins can view players across all game types but can only perform actions (create admin actions, manage tags, etc.) for their own game types.
- Moderators can view players and protected names but cannot manage tags or delete.
- HeadAdmin inherits GameAdmin privileges per game (except DeletePlayer which is SeniorAdmin only).

