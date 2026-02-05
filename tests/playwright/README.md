# Playwright Tests for XtremeIdiots Portal

This directory contains end-to-end tests for the XtremeIdiots Portal using Playwright in UITest mode.

## Prerequisites

- Node.js 18+ 
- .NET 9.0 SDK
- Portal application built (`dotnet build src/XtremeIdiots.Portal.Web`)

## Installation

```bash
cd tests/playwright
npm install
npx playwright install chromium
```

## Running Tests

### Run all tests
```bash
npm test
```

### Run tests with UI
```bash
npm run test:ui
```

### Run tests in headed mode (see browser)
```bash
npm run test:headed
```

### Debug tests
```bash
npm run test:debug
```

### View test report
```bash
npm run report
```

## How It Works

1. **Playwright starts the portal** in UITest mode using `ASPNETCORE_ENVIRONMENT=UITest`
2. **Portal runs offline** with in-memory fakes and SQLite database
3. **Tests execute** against the running application on `http://localhost:5000`
4. **Portal shuts down** after tests complete

## Test Structure

```
tests/
└── smoke.spec.ts         # Basic smoke tests for UITest mode
```

## Writing Tests

Tests should focus on UI structure and functionality that works without real data:

```typescript
test('example test', async ({ page }) => {
  await page.goto('/');
  
  // Test UI elements, navigation, authentication, etc.
  await expect(page).toHaveTitle(/XtremeIdiots/);
});
```

## Limitations

Since UITest mode uses fake data:
- Don't test specific data values (IDs, usernames from real DB)
- Focus on UI structure, navigation, authentication
- Test that pages load and render without errors
- Verify forms and controls are present

## CI Integration

Tests can run in CI pipelines:

```yaml
- name: Install Playwright
  run: |
    cd tests/playwright
    npm ci
    npx playwright install chromium

- name: Run Playwright tests
  run: |
    cd tests/playwright
    npm test
```

## Troubleshooting

### Tests timeout waiting for server
- Increase `webServer.timeout` in `playwright.config.ts`
- Check that .NET 9.0 SDK is installed
- Ensure port 5000 is not in use

### Tests fail with auth errors
- Verify UITest mode is enabled (check server logs)
- Ensure UITestDataSeeder ran successfully

### Static files not found
- Build the application first: `dotnet build src/XtremeIdiots.Portal.Web`
- Check that `wwwroot` directory exists

## More Information

- [Playwright Documentation](https://playwright.dev/)
- [UITest Mode Documentation](../../docs/UITest-Mode.md)
