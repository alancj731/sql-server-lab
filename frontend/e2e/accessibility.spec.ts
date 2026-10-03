import { expect, test } from '@playwright/test';

test.describe('360px viewport', () => {
  test.use({ viewport: { width: 360, height: 740 } });

  for (const path of ['/labs', '/labs/new']) {
    test(`no horizontal scroll on ${path}`, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - window.innerWidth,
      );
      expect(overflow).toBeLessThanOrEqual(0);
    });
  }
});

test('keyboard-only user can reach and submit the create form', async ({ page }) => {
  await page.goto('/labs');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();

  const newLab = page.getByRole('link', { name: 'New lab' });
  await newLab.focus();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/labs\/new$/);

  await page.getByLabel('Lab name').focus();
  await page.keyboard.type(`kb-${Date.now().toString(36)}`);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/overview$/);
});
