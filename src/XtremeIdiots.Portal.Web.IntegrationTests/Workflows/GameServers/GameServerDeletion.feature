@workflow @game-servers @destructive
Feature: Game server deletion
  Senior administrators remove retired game servers through an explicit destructive confirmation.

  Scenario: Senior admin deletes a game server
    Given a successful game server deletion scenario for a senior admin
    When the senior admin confirms game server deletion
    Then the delete command should contain the game server identifier
    And successful game server deletion feedback should be displayed
    And the game server deletion browser should report no errors

  Scenario: Head admin cannot open game server deletion
    Given a successful game server deletion scenario for a head admin
    When the head admin navigates directly to game server deletion
    Then game server deletion should be denied
    And no game server delete command should be recorded

  Scenario: Repository failure reports unsuccessful deletion
    Given a failing game server deletion scenario for a senior admin
    When the senior admin confirms game server deletion
    Then the failed delete command should contain the game server identifier
    And failed game server deletion feedback should be displayed
    And the game server deletion browser should report no errors