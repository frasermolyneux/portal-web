@workflow @game-servers @credentials @file-transport
Feature: File transport credentials
  Head administrators manage FTP and SFTP connectivity without exposing credentials to unauthorized server editors.

  Scenario: Head admin rotates all SFTP credentials
    Given a successful file transport scenario for a head admin
    When the head admin updates all SFTP connection fields
    Then the SFTP configuration should contain the updated connection details
    And the file transport server update should preserve enabled SFTP state
    And successful file transport update feedback should be displayed
    And the file transport browser should report no errors

  Scenario: Switching to FTP updates the transport controls
    Given a successful file transport scenario for a head admin
    When the head admin switches the transport type to FTP
    Then the file transport label and default port should show FTP values
    And the SFTP fingerprint control should be hidden
    And the file transport browser should report no errors

  Scenario: Blank SFTP secrets preserve current values
    Given a successful file transport scenario for a head admin
    When the head admin clears and saves the SFTP password and fingerprint
    Then the SFTP configuration should preserve the current password and fingerprint
    And the file transport browser should report no errors

  Scenario: Missing SFTP fingerprint prevents writes
    Given a file transport scenario with no existing SFTP fingerprint
    When the head admin submits SFTP without a host key fingerprint
    Then the SFTP fingerprint validation should be displayed
    And no file transport writes should be recorded
    And the file transport browser should report no errors

  Scenario: Traversal maps root prevents writes
    Given a successful file transport scenario for a head admin
    When the head admin submits a maps root containing path traversal
    Then the maps root validation should be displayed
    And no file transport writes should be recorded
    And the file transport browser should report no errors

  Scenario: Core server writer cannot edit file transport credentials
    Given a successful file transport scenario for a core server writer without credential permission
    When the core server writer opens the edit form and forges file transport credentials
    Then the edit form should remain available without file transport controls
    And the core update should be recorded without a file transport write
    And the file transport browser should report no errors

  Scenario: File transport password visibility can be toggled
    Given a successful file transport scenario for a head admin
    When the head admin toggles file transport password visibility
    Then the file transport password should be visible
    When the head admin toggles file transport password visibility
    Then the file transport password should be hidden
    And the file transport browser should report no errors

  Scenario: SFTP repository failure is reported after core update
    Given a failing file transport repository scenario for a head admin
    When the head admin updates all SFTP connection fields
    Then the core game server update should precede the failed SFTP write
    Then the failed SFTP configuration should contain the updated connection details
    And the file transport configuration failure warning should be displayed
    And the file transport browser should report no errors