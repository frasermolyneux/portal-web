@workflow @admin-actions
Feature: Admin action creation
  Portal administrators create auditable actions against players while role restrictions and downstream side effects remain enforced.

  Scenario: Senior admin creates a ban
    Given a successful admin action scenario for a senior admin
    When the senior admin creates a ban for repeated abusive behaviour
    Then the ban command should contain the expected details
    And a ban notification should be dispatched
    And the admin action browser should report no errors

  Scenario: Moderator creates an observation
    Given a successful admin action scenario for a moderator
    When the moderator creates an observation for disruptive play
    Then the observation command should contain the expected details
    And an admin action notification should be dispatched
    And the admin action browser should report no errors

  Scenario Outline: Invisible or short reasons are rejected
    Given a successful admin action scenario for a senior admin
    When the senior admin submits the "<reason case>" reason case for a ban
    Then the admin action reason validation should be displayed
    And no admin action side effects should be recorded
    And the admin action browser should report no errors

    Examples:
      | reason case              |
      | short text               |
      | style block              |
      | hidden attribute         |
      | quoted display none      |
      | unquoted display none    |
      | unquoted visibility hide |
      | zero-width characters    |

  Scenario: Moderator cannot open the ban form directly
    Given a successful admin action scenario for a moderator
    When the moderator navigates directly to the ban form
    Then the ban form should be denied
    And no admin action side effects should be recorded

  Scenario: Moderator cannot forge a ban submission
    Given a successful admin action scenario for a moderator
    When the moderator forges a ban submission from the observation form
    Then the forged ban submission should be denied
    And no admin action side effects should be recorded

  Scenario: Repository failure reports the partial failure
    Given a failing repository admin action scenario for a senior admin
    When the senior admin submits a valid ban
    Then the partial failure guidance should be displayed
    And the discussion topic should have been created once
    And no admin action notification should be dispatched
    And the admin action browser should report no errors