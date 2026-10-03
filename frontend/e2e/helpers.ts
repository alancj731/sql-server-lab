import { expect, type Page } from '@playwright/test';

export function uniqueName(prefix = 'e2e'): string {
  return `${prefix}-${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;
}

export async function createLab(page: Page, name: string): Promise<void> {
  await page.goto('/labs/new');
  await page.getByLabel('Lab name').fill(name);
  await page.getByLabel('Time to live').selectOption({ label: '2 hours' });
  await page.getByRole('button', { name: 'Create lab' }).click();
  await expect(page).toHaveURL(/\/labs\/[0-9a-f-]+\/overview$/);
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
}

export function labState(page: Page) {
  return page.locator('header.header app-status-pill').first();
}

export function action(page: Page, name: string) {
  return page.getByRole('group', { name: 'Lab actions' }).getByRole('button', { name });
}
