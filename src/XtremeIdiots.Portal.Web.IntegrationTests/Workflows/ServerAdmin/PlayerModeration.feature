@workflow @server-admin @player-moderation
Feature: Live player moderation
  Connected-player moderation respects separate Kick and Ban permissions and records successful RCON actions.

  Scenario: Direct Kick permission exposes only Kick and records the action
    Given a successful player moderation scenario for a direct Kick user
    When the user opens the live player table
    Then only the Kick player control should be available
    When the user kicks the connected player
    Then the CoD4 "kick" command should target slot 7
    And a "Kick" admin action should be recorded for the connected player
    And the player moderation success toast should be displayed
    And the player moderation browser should report no errors

  Scenario: Direct Ban permission exposes TempBan and Ban without Kick
    Given a successful player moderation scenario for a direct Ban user
    When the user opens the live player table
    Then only the Ban player controls should be available

  Scenario: Kick permission without admin action creation does not expose controls
    Given a successful player moderation scenario for a Kick-only user without admin action creation
    When the user opens the live player table
    Then no player moderation controls should be available

  Scenario Outline: Game admin applies a ban-class action
    Given a successful player moderation scenario for a game admin
    When the user applies the "<action>" action to the connected player
    Then the CoD4 "<command>" command should target slot 7
    And a "<adminAction>" admin action should be recorded for the connected player
    And the player moderation success toast should be displayed
    And the player moderation browser should report no errors

    Examples:
      | action   | command  | adminAction |
      | TempBan  | temp ban | TempBan     |
      | Ban      | ban      | Ban         |

  Scenario: Moderator sees Kick but not ban controls
    Given a successful player moderation scenario for a moderator
    When the user opens the live player table
    Then only the Kick player control should be available

  Scenario: Direct Kick user cannot forge a permanent ban
    Given a successful player moderation scenario for a direct Kick user
    When the user directly submits a forged "BanRconPlayer" action
    Then the player moderation response should report "You don't have permission to ban players"
    And no player moderation command should be recorded
    And no player admin action should be recorded

  Scenario: Forged player identity is replaced by live server identity
    Given a successful player moderation scenario for a direct Kick user
    When the user directly submits a Kick action with forged player identity
    Then the player moderation response should report "Player ConnectedPlayer has been kicked"
    And the CoD4 "kick" command should target slot 7
    And a "Kick" admin action should be recorded for the connected player

  Scenario: Stale player slot is rejected before RCON
    Given a successful player moderation scenario for a direct Kick user
    When the user directly submits a Kick action for stale slot 99
    Then the player moderation response should report "Player is no longer connected or cannot be recorded"
    And no player moderation command should be recorded
    And no player admin action should be recorded

  Scenario: Mismatched repository search result is rejected before RCON
    Given a mismatched repository player moderation scenario for a direct Kick user
    When the user directly submits a Kick action for the connected player
    Then the player moderation response should report "Player is no longer connected or cannot be recorded"
    And no player moderation command should be recorded
    And no player admin action should be recorded

  Scenario: Live player HTML is escaped in moderation feedback
    Given a player moderation scenario with an HTML-bearing live player name
    When the user kicks the connected player
    Then the player moderation toast should contain the live player name as text
    And the player moderation toast should contain no injected image
    And the player moderation browser should report no errors

  Scenario: Kick RCON failure is reported without an admin action
    Given a failing RCON player moderation scenario for a direct Kick user
    When the user kicks the connected player
    Then the player moderation failure toast should report "Failed to kick player from server"
    And the CoD4 "kick" command should target slot 7
    And no player admin action should be recorded
    And the player moderation browser should report no errors

  Scenario: Admin action persistence failure reports partial completion
    Given a failing persistence player moderation scenario for a direct Kick user
    When the user kicks the connected player
    Then the player moderation failure toast should report "was kicked, but the admin action could not be recorded"
    And the CoD4 "kick" command should target slot 7
    And one player admin action should have been attempted
    And the player moderation browser should report no errors