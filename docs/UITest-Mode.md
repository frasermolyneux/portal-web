# UITest Mode

UITest mode enables the XtremeIdiots Portal to run fully offline for automated browser testing (e.g., Playwright) without requiring external dependencies like databases, Azure services, or OAuth providers.

## Features

- **No External Dependencies**: All external services (forums, APIs, OAuth) are replaced with in-memory fakes
- **SQLite In-Memory**: Identity database uses SQLite `:memory:` instead of SQL Server
- **Auto Authentication**: Test user is automatically signed in without OAuth flow
- **Pre-Seeded Data**: Test user has full SeniorAdmin permissions for all game types
- **Deterministic**: Same test environment every time with predictable fake data

## Enabling UITest Mode

### Method 1: Environment Variable
```bash
ASPNETCORE_ENVIRONMENT=UITest dotnet run --project src/XtremeIdiots.Portal.Web
```

### Method 2: Configuration Flag
Add to `appsettings.json` or user secrets:
```json
{
  "UITest": {
    "Enabled": true
  }
}
```

## What Gets Replaced

### Authentication
- **Production**: OAuth 2.0 with XtremeIdiots forums
- **UITest**: Automatic sign-in with test user

### Identity Database
- **Production**: SQL Server with migrations
- **UITest**: SQLite in-memory (`:memory:`)

### External Services
| Service | Production | UITest |
|---------|-----------|--------|
| Repository API | Real API calls with auth | Dummy endpoints (no calls made) |
| Servers Integration API | Real API calls with auth | Dummy endpoints (no calls made) |
| GeoLocation API | Real API calls with auth | Dummy endpoints (no calls made) |
| Forums Integration | Invision Community API | Fake - returns fixed data |
| Admin Action Topics | Creates real forum topics | Fake - logs only |
| Demo Manager | Real forum integration | Fake - returns test data |
| ProxyCheck | External HTTP calls | Fake - returns safe results |
| Azure App Configuration | Loads from Azure | Skipped entirely |
| Application Insights Profiler | Requires connection string | Disabled |

### Test User Details

The test user is automatically created and signed in:

- **User ID**: `1`
- **Username**: `uitest@xtremeidiots.com`
- **Email**: `uitest@xtremeidiots.com`
- **Claims**: 
  - SeniorAdmin (full access to all features)
  - HeadAdmin for COD2, COD4, COD5
  - GameAdmin for COD2, COD4, COD5

## Using with Playwright

```javascript
// playwright.config.ts
export default defineConfig({
  webServer: {
    command: 'ASPNETCORE_ENVIRONMENT=UITest dotnet run --project src/XtremeIdiots.Portal.Web',
    url: 'http://localhost:5000',
    reuseExistingServer: !process.env.CI,
  },
  use: {
    baseURL: 'http://localhost:5000',
  },
});
```

```javascript
// tests/smoke.spec.ts
import { test, expect } from '@playwright/test';

test('home page loads', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/XtremeIdiots/);
});

test('user is automatically authenticated', async ({ page }) => {
  await page.goto('/');
  // User should be signed in automatically in UITest mode
  await expect(page.locator('text=uitest@xtremeidiots.com')).toBeVisible();
});
```

## Verification

To verify UITest mode is working:

1. **Start the application**:
   ```bash
   ASPNETCORE_ENVIRONMENT=UITest dotnet run --project src/XtremeIdiots.Portal.Web
   ```

2. **Check the console output**:
   ```
   info: Microsoft.Hosting.Lifetime[0]
         Hosting environment: UITest
   info: Microsoft.Hosting.Lifetime[0]
         Now listening on: http://localhost:5000
   ```

3. **Visit the application**: Open http://localhost:5000 in your browser

4. **Verify auto sign-in**: You should be automatically signed in as `uitest@xtremeidiots.com`

## Limitations

- **No Real Data**: All API clients return no data (except fakes). Pages that require real data from Repository/Servers APIs will be empty.
- **No Persistence**: SQLite in-memory database is wiped on application restart
- **Single User**: Only one test user exists
- **No Network Operations**: Cannot test actual API integrations or OAuth flows

## Development vs UITest vs Production

| Feature | Development | UITest | Production |
|---------|------------|--------|------------|
| Database | SQL Server | SQLite :memory: | SQL Server |
| OAuth | Real XtremeIdiots | Fake auto sign-in | Real XtremeIdiots |
| API Clients | Real with dev endpoints | Fake/dummy | Real with prod endpoints |
| Azure Config | Optional | Disabled | Required |
| Telemetry | Full | Basic | Full |
| Razor Compilation | Runtime | Runtime | Build-time |

## Troubleshooting

### Application won't start
- Check that `ASPNETCORE_ENVIRONMENT=UITest` is set correctly
- Verify `appsettings.UITest.json` exists
- Check console for error messages

### User not authenticated
- The UITestAuthenticationHandler should auto sign-in on first request
- Check that UITest mode is actually enabled (look for "Hosting environment: UITest" in console)

### Pages are empty
- This is expected - the API clients are registered but don't return real data
- UITest mode is for testing UI structure and authentication, not data display

## Implementation Details

For implementation details, see:
- `Helpers/UITestConfiguration.cs` - UITest mode detection
- `Areas/Identity/UITestAuthenticationHandler.cs` - Auto sign-in handler
- `Areas/Identity/IdentityHostingStartup.cs` - SQLite in-memory setup
- `UITest/UITestDataSeeder.cs` - Test user creation
- `UITest/Fakes/` - Fake service implementations
- `Startup.cs` - Conditional service registration
