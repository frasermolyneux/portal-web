@workflow @server-admin @server-feed
Feature: Live server feed lifecycle
  The unified feed merges authorized sources and owns polling, pause, filtering, and disposal behavior.

  Scenario: Initial feed renders chat and events safely
    Given a server feed with chat and event items
    When the user opens the server feed
    Then the feed should show the chat and event items
    And the feed should contain no injected image
    And the server feed browser should report no errors

  Scenario: Event type filter narrows rendered events
    Given a server feed with multiple event types
    When the user opens the server feed
    And the user filters events by "map"
    Then only the map event should be visible

  Scenario: Disabling events resets the cursor and changes the source query
    Given a server feed with chat and event items
    When the user opens the server feed
    And the user disables event feed entries
    Then the latest feed request should exclude events
    And the latest feed request should not contain a cursor

  Scenario: Disabling events while paused clears stale event rows
    Given a server feed with chat and event items
    When the user opens the server feed
    And the user pauses feed refresh
    And the user disables event feed entries
    Then no event feed item should be visible
    And the feed should report "1 new" pending item

  Scenario: Paused feed buffers new items until resumed
    Given a server feed with one chat item
    When the user opens the server feed
    And the user pauses feed refresh
    And a new chat item is refreshed
    Then the feed should report "1 new" pending item
    And the new chat item should not be visible
    When the user resumes feed refresh
    Then the new chat item should be visible

  Scenario: Repeated polling deduplicates feed items
    Given a server feed with one chat item
    When the user opens the server feed
    And the same chat item is refreshed
    Then the feed should contain one chat item

  Scenario: Empty incremental response preserves the polling cursor
    Given a server feed with chat and event items
    When the user opens the server feed
    And an empty incremental response is refreshed
    And another incremental refresh is requested
    Then the latest feed request should retain all cursor values

  Scenario: Feed overrun displays one persistent notice
    Given an overrun server feed response
    When the user opens the server feed
    And another overrun response is refreshed
    Then the high-volume indicator should be visible
    And one high-volume notice should be visible

  Scenario: Forced reload recreates the overrun notice
    Given an overrun server feed response
    When the user opens the server feed
    And an overrun forced reload is requested
    Then the high-volume indicator should be visible
    And one high-volume notice should be visible

  Scenario: Initially backgrounded page does not start polling
    Given a server feed with one chat item
    And the page is initially backgrounded
    When the user opens the server feed
    Then no initial feed request should be sent

  Scenario: Backgrounded page suppresses refresh until visible
    Given a server feed with one chat item
    When the user opens the server feed
    And the page is backgrounded and refresh is requested
    Then no background feed request should be sent
    When the page becomes visible and refresh is requested
    Then one visible feed request should be sent

  Scenario: Overlapping refresh and disposed refresh are suppressed
    Given a server feed with one chat item
    When the user opens the server feed
    And two refreshes are requested while one is active
    Then one overlapping feed request should be sent
    When the server feed is disposed and refresh is requested
    Then no disposed feed request should be sent
    And the server feed browser should report no errors

  Scenario: Forced reload supersedes an active request
    Given a server feed with one chat item
    When the user opens the server feed
    And a forced reload supersedes a delayed refresh
    Then two supersession feed requests should be sent
    And only the replacement feed item should be visible
    And the superseded feed request should be the only browser failure