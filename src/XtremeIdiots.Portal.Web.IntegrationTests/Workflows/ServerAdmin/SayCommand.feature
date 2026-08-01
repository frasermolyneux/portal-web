@workflow @server-admin @say-command
Feature: Live server Say command
  Authorized administrators broadcast messages to connected players through the game-specific RCON client.

  Scenario: Direct Say permission broadcasts a trimmed message
    Given a successful Say command scenario for a direct-permission user
    When the user broadcasts "  Match starts in five minutes  "
    Then the CoD4 Say command should contain "Match starts in five minutes"
    And the Say success toast should be displayed
    And the Say message field should be cleared
    And the Say browser should report no errors

  Scenario: Game admin broadcasts a message
    Given a successful Say command scenario for a game admin
    When the user broadcasts "Welcome to the server"
    Then the CoD4 Say command should contain "Welcome to the server"
    And the Say success toast should be displayed
    And the Say browser should report no errors

  Scenario: Whitespace-only Say message is rejected server-side
    Given a successful Say command scenario for a direct-permission user
    When the user directly submits a whitespace-only Say message
    Then the Say validation response should report "Message cannot be empty"
    And no Say command should be recorded

  Scenario: Moderator does not see the Say form
    Given a successful Say command scenario for a moderator
    When the moderator opens the server detail page
    Then the Say form should not be visible
    And no Say command should be recorded

  Scenario: Moderator cannot forge a Say command
    Given a successful Say command scenario for a moderator
    When the moderator directly submits a forged Say message
    Then the forged Say command should be denied
    And no Say command should be recorded

  Scenario: RCON backend failure is reported
    Given a failing Say command scenario for a direct-permission user
    When the user broadcasts "Server maintenance soon"
    Then the CoD4 Say command should contain "Server maintenance soon"
    And the Say failure toast should be displayed
    And the Say message field should retain "Server maintenance soon"
    And the Say browser should report no errors