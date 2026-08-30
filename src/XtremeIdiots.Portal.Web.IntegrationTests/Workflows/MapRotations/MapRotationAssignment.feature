@workflow @map-rotations
Feature: Map rotation server assignment with COD4/COD4x equivalence
  A COD5 HeadAdmin with direct COD4 map rotation permissions and a server-scoped
  deploy grant assigns a COD4x server to a COD4 rotation. The equivalence logic
  must include the COD4x server in the compatible-server list and accept the
  assignment while exact-server filtering still blocks non-permitted servers.

  Scenario: Assign permitted COD4x server to COD4 rotation
    Given a map rotation assignment scenario for a map rotation deployer
    When the deployer views the rotation details
    Then the assign to server link should be visible
    When the deployer navigates to the create assignment page
    Then only the permitted server should appear in the server selector
    When the deployer submits the assignment for the permitted server
    Then the assignment should be created with the correct server
    And the map rotation assignment browser should report no errors

  Scenario: Non-permitted server is excluded from the selector
    Given a map rotation assignment scenario for a map rotation deployer
    When the deployer navigates to the create assignment page
    Then the non-permitted server should not appear in the server selector
    And the map rotation assignment browser should report no errors
