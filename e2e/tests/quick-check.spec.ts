import { test, expect } from '@playwright/test';

test('Page loads without errors', async ({ page }) => {
  // Listen for console errors
  const errors: string[] = [];
  page.on('console', msg => {
    // Without a session, the silent refresh the app attempts at boot gets an expected
    // 401 that Chromium reports as a console error; anything else is a real error.
    const expectedBootRefresh401 =
      msg.location().url.includes('/api/v1/auth/refresh') && /\b401\b/.test(msg.text());
    if (msg.type() === 'error' && !expectedBootRefresh401) {
      errors.push(msg.text());
    }
  });

  // Listen for page errors
  page.on('pageerror', error => {
    errors.push(`Page error: ${error.message}`);
  });

  // Navigate to login page
  await page.goto('/fr/login');

  // Wait for page to load
  await page.waitForLoadState('networkidle');

  // Check for HouseFlow title
  const title = await page.title();
  console.log('Page title:', title);

  // Check if HouseFlow heading exists
  const heading = await page.locator('h1').textContent();
  console.log('Heading:', heading);

  // Log all errors
  if (errors.length > 0) {
    console.log('Errors found:', errors);
  }

  // Assertions
  expect(title).toBe('HouseFlow');
  expect(heading).toContain('HouseFlow');
  expect(errors.length).toBe(0);
});
