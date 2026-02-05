import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for XtremeIdiots Portal UITest mode
 * 
 * This configuration runs tests against the portal in UITest mode,
 * which provides offline operation with fake data and auto-authentication.
 */
export default defineConfig({
  testDir: './tests',
  
  // Run tests in parallel
  fullyParallel: true,
  
  // Fail the build on CI if you accidentally left test.only in the source code
  forbidOnly: !!process.env.CI,
  
  // Retry on CI only
  retries: process.env.CI ? 2 : 0,
  
  // Opt out of parallel tests on CI
  workers: process.env.CI ? 1 : undefined,
  
  // Reporter to use
  reporter: 'html',
  
  // Shared settings for all the projects below
  use: {
    // Base URL for the application
    baseURL: 'http://localhost:5000',
    
    // Collect trace when retrying the failed test
    trace: 'on-first-retry',
    
    // Screenshot only on failure
    screenshot: 'only-on-failure',
  },

  // Configure projects for major browsers
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Run the dev server before starting the tests
  webServer: {
    command: 'cd src/XtremeIdiots.Portal.Web && ASPNETCORE_ENVIRONMENT=UITest dotnet run --no-launch-profile',
    url: 'http://localhost:5000',
    reuseExistingServer: !process.env.CI,
    timeout: 120 * 1000, // 2 minutes for .NET to start
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
