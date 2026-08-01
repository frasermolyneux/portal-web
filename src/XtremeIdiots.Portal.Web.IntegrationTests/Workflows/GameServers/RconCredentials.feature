@workflow @game-servers @credentials
Feature: RCON credentials
  Head administrators rotate game-server RCON credentials while unauthorized users and invalid edits remain blocked.

  Scenario: Head admin rotates the RCON password
    Given a successful RCON credential scenario for a head admin
    When the head admin changes the RCON password to "NewPassword123"
    Then the game server update should preserve the core server details
    And the RCON configuration should contain password "NewPassword123"
    And successful game server update feedback should be displayed
    And the RCON browser should report no errors

  Scenario: RCON password visibility can be toggled
    Given a successful RCON credential scenario for a head admin
    When the head admin toggles RCON password visibility
    Then the RCON password should be visible
    When the head admin toggles RCON password visibility
    Then the RCON password should be hidden
    And the RCON browser should report no errors

  Scenario: Blank RCON password preserves the existing credential
    Given a successful RCON credential scenario for a head admin
    When the head admin clears and saves the RCON password
    Then the RCON configuration should contain password "CurrentPassword"
    And successful game server update feedback should be displayed
    And the RCON browser should report no errors

  Scenario: Missing server title prevents all writes
    Given a successful RCON credential scenario for a head admin
    When the head admin submits the edit form without a server title
    Then the required server title validation should be displayed
    And no game server or RCON writes should be recorded
    And the RCON browser should report no errors

  Scenario: Game admin cannot open the server edit form
    Given a successful RCON credential scenario for a game admin
    When the game admin navigates directly to the server edit form
    Then game server editing should be denied
    And no game server or RCON writes should be recorded

  Scenario: Core server writer cannot edit RCON credentials
    Given a successful RCON credential scenario for a core server writer without RCON permission
    When the core server writer opens the edit form and forges an RCON password
    Then the server edit form should remain available without RCON controls
    And the core game server update should be recorded without an RCON write
    And the RCON browser should report no errors

  Scenario: RCON repository failure is reported after the server update
    Given a failing RCON repository scenario for a head admin
    When the head admin changes the RCON password to "UnpersistedPassword"
    Then the game server update should have been recorded
    And the failed RCON configuration should contain password "UnpersistedPassword"
    And the RCON configuration failure warning should be displayed
    And the RCON browser should report no errors