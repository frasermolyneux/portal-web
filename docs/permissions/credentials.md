# Credentials Permissions Matrix

Source: `AuthPolicies`, `CredentialsAuthHandler`, `GameServersAuthHandler`, `BaseAuthorizationHelper`, `Views/Credentials/Index.cshtml`

Legend:
- ✓ Granted
- (all) All servers
- g Game-type scope
- s Specific server (claim value is server GUID)
- ✗ Not granted

Roles / Claims considered (minimal single-claim scenarios):
- SeniorAdmin
- HeadAdmin (game)
- GameAdmin (game)
- GameServers.Credentials.Ftp.Read (server)
- GameServers.Credentials.Rcon.Read (server)
- GameServers.Admin.Rcon (game) (included due to RCON credential view logic)

| Capability / Policy                           | SeniorAdmin | HeadAdmin (game) | GameAdmin (game) | GameServers.Credentials.Ftp.Read (server) | GameServers.Credentials.Rcon.Read (server) | GameServers.Admin.Rcon (game) | Notes                                       |
| --------------------------------------------- | ----------- | ---------------- | ---------------- | ----------------------------------------- | ------------------------------------------ | ----------------------------- | ------------------------------------------- |
| Access Credentials Page (`AccessCredentials`) | ✓ (all)     | ✓ g              | ✓ g              | ✓ s                                       | ✓ s                                        | ✗                             | GameServers.Admin.Rcon NOT in access claim group |
| View FTP Hostname (`ViewFtpCredential`)       | ✓ (all)     | ✓ g              | ✗                | ✓ s                                       | ✗                                          | ✗                             | HeadAdmin or per-server FTP claim           |
| View FTP Username (`ViewFtpCredential`)       | ✓ (all)     | ✓ g              | ✗                | ✓ s                                       | ✗                                          | ✗                             | Same rule                                   |
| View FTP Password (`ViewFtpCredential`)       | ✓ (all)     | ✓ g              | ✗                | ✓ s                                       | ✗                                          | ✗                             | Same rule                                   |
| View RCON Password (`ViewRconCredential`)     | ✓ (all)     | ✓ g              | ✓ g              | ✗                                         | ✓ s                                        | ✓ g                           | HeadAdmin added; per-server claim supported |

## Nuances
- **Credentials are exempt from the see-all, do-own model.** Both data retrieval and authorization remain game-type-scoped due to the sensitive nature of RCON/FTP credentials.
- HeadAdmin now included in `ViewRconCredential` for parity with FTP credential access.
- `GameServers.Credentials.Rcon.Read` claim grants per-server RCON password visibility.
- `GameServers.Admin.Rcon` cannot reach page alone (not in AccessCredentials claim set) though it enables viewing RCON.

## Potential Alignments
- Optionally include GameServers.Admin.Rcon in `AccessCredentials` claim set if those users should access the page directly.

