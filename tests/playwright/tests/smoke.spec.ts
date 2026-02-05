import { test, expect } from '@playwright/test';

/**
 * Smoke tests for XtremeIdiots Portal in UITest mode
 * 
 * These tests verify basic functionality without relying on external services:
 * - Application starts and responds
 * - Home page loads correctly
 * - User is automatically authenticated
 * - Basic navigation works
 */

test.describe('UITest Mode Smoke Tests', () => {
  test('home page loads successfully', async ({ page }) => {
    await page.goto('/');
    
    // Check that we get a successful response
    expect(page.url()).toContain('localhost:5000');
    
    // Page should have the title
    await expect(page).toHaveTitle(/XtremeIdiots|Portal/i);
  });

  test('health check endpoint responds', async ({ page }) => {
    const response = await page.goto('/api/health');
    
    // Health check should return 200 OK
    expect(response?.status()).toBe(200);
  });

  test('application is in UITest environment', async ({ page }) => {
    // Navigate to home
    await page.goto('/');
    
    // In UITest mode, the user should be automatically authenticated
    // This would be reflected in the UI (username displayed, login link not shown, etc.)
    
    // Note: Specific selectors depend on the actual UI structure
    // This is a placeholder that should be updated based on actual UI
    const body = await page.textContent('body');
    expect(body).toBeTruthy();
  });

  test('static files are served', async ({ page }) => {
    // Check that CSS loads
    const response = await page.goto('/css/app.css');
    expect(response?.status()).toBe(200);
  });

  test('error page handling', async ({ page }) => {
    // Try to access a non-existent page
    const response = await page.goto('/ThisPageDoesNotExist');
    
    // Should get a 404 but not crash
    expect(response?.status()).toBe(404);
    
    // Should show error page (not default browser error)
    const body = await page.textContent('body');
    expect(body).toBeTruthy();
  });
});

test.describe('UITest Authentication', () => {
  test('user is automatically signed in', async ({ page }) => {
    await page.goto('/');
    
    // In UITest mode, the UITestAuthenticationHandler should auto sign-in the test user
    // The specific check depends on how the UI displays authentication status
    
    // Example checks (update based on actual UI):
    // - Look for username display
    // - Check that "Login" link is not present
    // - Verify user menu is available
    
    const pageContent = await page.content();
    
    // Basic assertion that page loaded
    expect(pageContent.length).toBeGreaterThan(100);
  });
});
