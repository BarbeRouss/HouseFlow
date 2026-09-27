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

  // P02: the brand is an h2 above the page h1 "Connexion"
  const heading = await page.locator('h1').textContent();
  const brand = await page.locator('h2').first().textContent();
  console.log('Heading:', heading);

  // Log all errors
  if (errors.length > 0) {
    console.log('Errors found:', errors);
  }

  // Assertions
  expect(title).toBe('HouseFlow');
  expect(heading).toContain('Connexion');
  expect(brand).toContain('HouseFlow');
  expect(errors.length).toBe(0);
});
