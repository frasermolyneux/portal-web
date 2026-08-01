@workflow @server-admin @map-control
Feature: Live server map and restart commands
  Map operations and full server restarts use separate scoped permissions and game-specific RCON calls.

  Scenario: Direct map permission loads the selected map
    Given a successful map control scenario for a direct map user
    When the user directly loads map "mp_crash"
    Then the CoD4 map command should contain "mp_crash"
    And the map response should report "Map 'mp_crash' is now loading"

  Scenario: Empty map name is rejected server-side
    Given a successful map control scenario for a direct map user
    When the user directly loads a whitespace-only map
    Then the map response should report "Map name is required"
    And no map control command should be recorded

  Scenario Outline: Game admin sends a map lifecycle command
    Given a successful map control scenario for a game admin
    When the user sends the "<command>" map command
    Then the "<command>" map control command should be recorded
    And the map command success toast should be displayed
    And the map control browser should report no errors

    Examples:
      | command      |
      | restart      |
      | fast restart |
      | next         |

  Scenario: Direct map permission does not expose full server restart
    Given a successful map control scenario for a direct map user
    When the user opens map control
    Then the map command controls should be available
    And the server restart control should not be present

  Scenario: Head admin restarts the server
    Given a successful map control scenario for a head admin
    When the user sends the server restart command
    Then the server restart command should be recorded
    And the map command success toast should be displayed
    And the map control browser should report no errors

  Scenario: Game admin cannot forge a server restart
    Given a successful map control scenario for a game admin
    When the user directly submits a forged server restart
    Then the forged map control command should be denied
    And no map control command should be recorded

  Scenario: Map restart backend failure is reported
    Given a failing map control scenario for a game admin
    When the user sends the "restart" map command
    Then the map command failure toast should report "Failed to restart map"
    And the "restart" map control command should be recorded
    And the map control browser should report no errors

  Scenario: Direct restart permission restarts the server without map controls
    Given a successful map control scenario for a direct restart user
    When the user sends the server restart command
    Then the server restart command should be recorded
    And the map command controls should not be present
    And the map command success toast should be displayed
    And the map control browser should report no errors