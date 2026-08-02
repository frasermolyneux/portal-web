@workflow @server-admin @cod4x-lifecycle
Feature: CoD4x plugin lifecycle requests
  Authorized operators queue auditable plugin lifecycle requests without mutating agent-owned runtime state.

  Scenario: Direct lifecycle permission queues an install request
    Given a successful CoD4x lifecycle scenario for a direct lifecycle user
    When the user requests install of version " 1.2.4 "
    Then an "Install" lifecycle request should be queued for version "1.2.4"
    And the lifecycle request should preserve runtime state
    And the lifecycle request should preserve plugin settings and schema
    And the install request should contain Linux artifact metadata
    And the CoD4x lifecycle success toast should be displayed
    And all lifecycle controls should remain disabled after queueing
    And the queued lifecycle request should appear after reload
    And the CoD4x lifecycle browser should report no errors

  Scenario Outline: Head admin queues a versionless lifecycle request
    Given a successful CoD4x lifecycle scenario for a head admin
    When the user requests the "<action>" lifecycle action
    Then an "<action>" lifecycle request should be queued without a target version
    And the lifecycle request should preserve runtime state
    And the lifecycle request should preserve plugin settings and schema
    And the versionless lifecycle request should contain no artifact metadata
    And the CoD4x lifecycle success toast should be displayed
    And all lifecycle controls should remain disabled after queueing
    And the queued lifecycle request should appear after reload
    And the CoD4x lifecycle browser should report no errors

    Examples:
      | action   |
      | Rollback |
      | Unload   |

  Scenario: Game admin cannot see lifecycle controls
    Given a successful CoD4x lifecycle scenario for a game admin
    When the user opens the CoD4x lifecycle panel
    Then the CoD4x lifecycle controls should not be present
    And the lifecycle permission message should be visible

  Scenario: Game admin cannot forge a lifecycle request
    Given a successful CoD4x lifecycle scenario for a game admin
    When the user directly submits a forged rollback request
    Then the forged lifecycle request should be denied
    And no lifecycle request should be queued

  Scenario: Install requires a target version in the browser
    Given a successful CoD4x lifecycle scenario for a direct lifecycle user
    When the user requests install without a target version
    Then the target version warning should be displayed
    And no lifecycle request should be queued

  Scenario: Invalid target version is rejected server-side
    Given a successful CoD4x lifecycle scenario for a direct lifecycle user
    When the user directly submits install version "1.2.4 bad"
    Then the lifecycle response should report "Target version contains invalid characters."
    And no lifecycle request should be queued

  Scenario: Malformed current settings prevent a lifecycle request
    Given a CoD4x lifecycle scenario with malformed current settings
    When the user requests the "Rollback" lifecycle action
    Then the CoD4x lifecycle failure toast should report "Unable to parse current CoD4x plugin settings."
    And no lifecycle request should be queued
    And the CoD4x lifecycle browser should report no errors

  Scenario: Configuration load failure prevents a lifecycle request
    Given a CoD4x lifecycle scenario with unavailable current settings
    When the user requests the "Unload" lifecycle action
    Then the CoD4x lifecycle failure toast should report "Unable to load current CoD4x plugin settings."
    And no lifecycle request should be queued
    And the CoD4x lifecycle browser should report no errors

  Scenario: Repository failure reports that the request was not queued
    Given a failing CoD4x lifecycle repository scenario
    When the user requests the "Rollback" lifecycle action
    Then the CoD4x lifecycle failure toast should report "Failed to queue CoD4x plugin operation request."
    And one lifecycle request should have been attempted
    And the CoD4x lifecycle browser should report no errors

  Scenario: Existing pending request blocks replacement
    Given a CoD4x lifecycle scenario with an existing pending request
    When the user opens the CoD4x lifecycle panel
    Then the lifecycle controls should be disabled
    When the user directly submits a rollback request
    Then the lifecycle response should report "A CoD4x plugin operation request is already pending."
    And no lifecycle request should be queued

  Scenario: Lifecycle controls are disabled while a request is in flight
    Given a delayed CoD4x lifecycle repository scenario
    When the user starts a rollback lifecycle request
    Then all lifecycle controls should be disabled in flight
    When the lifecycle request completes
    Then one lifecycle request should have been attempted
    And the CoD4x lifecycle browser should report no errors