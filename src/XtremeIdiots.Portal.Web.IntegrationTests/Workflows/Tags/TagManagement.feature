@workflow @tags
Feature: Tag management
  Portal administrators manage the tag definitions that can be assigned to players.

  Background:
    Given an isolated tag scenario

  Scenario: Game admin creates a user-defined tag
    Given I am authenticated as a game admin
    When I create the VIP user-defined tag
    Then the VIP tag command should contain the expected details
    And successful tag creation feedback should be displayed
    And the browser should report no errors

  Scenario: Game admin edits an existing tag
    Given I am authenticated as a game admin
    When I update the existing tag
    Then the update tag command should preserve all expected details
    And successful tag update feedback should be displayed
    And the browser should report no errors

  Scenario: Game admin deletes a user-defined tag
    Given I am authenticated as a game admin
    When I delete the existing user-defined tag
    Then the delete tag command should contain the existing tag identifier
    And successful tag deletion feedback should be displayed
    And the browser should report no errors

  Scenario: Moderator cannot open the create-tag form
    Given I am authenticated as a moderator
    When I navigate directly to the create-tag form
    Then tag creation access should be forbidden
    And no tag should have been created