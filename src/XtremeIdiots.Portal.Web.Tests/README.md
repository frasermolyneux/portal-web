# XtremeIdiots.Portal.Web.Tests

Unit test project for the XtremeIdiots Portal Web application.

## Overview

This test project provides basic unit tests aimed at preventing runtime errors caused by package updates. The tests focus on critical extension methods and services that are used throughout the application.

## Technologies

- **xUnit** - Test framework
- **Moq** - Mocking framework for dependencies
- **coverlet.collector** - Code coverage collection

## Test Structure

```
XtremeIdiots.Portal.Web.Tests/
├── Extensions/
│   ├── ClaimsPrincipalExtensionsTests.cs
│   └── IPAddressExtensionsTests.cs
└── Services/
    └── ProxyCheckServiceTests.cs
```

## Test Coverage

### ClaimsPrincipalExtensionsTests (13 tests)
Tests for security claims extraction methods:
- Username, Email, and XtremeIdiots ID extraction
- User profile and photo URL claims
- Game type and server ID claim filtering
- Authorization-related claim processing

### IPAddressExtensionsTests (17 tests)
Tests for IP address formatting and display:
- HTML string generation with various parameters
- Risk score badge rendering
- Proxy and VPN flag display
- Geo-location integration
- Link generation behavior

### ProxyCheckServiceTests (10 tests)
Tests for IP address risk checking service:
- Constructor validation
- Input validation (null/empty IP addresses)
- Caching behavior
- API configuration handling
- HTTP error scenarios
- Risk classification logic

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run tests with coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Run specific test class
```bash
dotnet test --filter "FullyQualifiedName~ClaimsPrincipalExtensionsTests"
```

### Run tests in Release configuration
```bash
dotnet test --configuration Release
```

## Test Patterns

The tests follow standard xUnit patterns:
- **Arrange**: Set up test data and dependencies
- **Act**: Execute the method being tested
- **Assert**: Verify the expected outcome

Mock objects are created using Moq for external dependencies like:
- `IHttpClientFactory`
- `IMemoryCache`
- `IConfiguration`
- `ILogger<T>`

## Future Expansion

This test project establishes the infrastructure for expanding test coverage as the application evolves. Consider adding tests for:
- Controller actions
- ViewModels
- Custom middleware
- Authorization handlers
- Additional services and extensions

## Notes

- These tests are designed to catch breaking changes from package updates
- Tests use the same .NET version (net9.0) as the main application
- Code coverage data is collected using coverlet
- All tests are compatible with CI/CD pipelines
