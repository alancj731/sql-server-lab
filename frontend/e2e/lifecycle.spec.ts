import { expect, test } from '@playwright/test';
import { action, createLab, labState, uniqueName } from './helpers';

test('simulated lab can be created, deallocated, started, and deleted', async ({ page }) => {
  const name = uniqueName('life');
  await createLab(page, name);

  await expect(page.getByRole('note')).toContainText('simulated');

  // Never optimistic: the lab passes through backend-reported states before Ready.
  await expect(labState(page)).toHaveText(/Requested|Provisioning|Configuring/);
  await expect(labState(page)).toHaveText('Ready');
  await expect(action(page, 'Start')).toBeDisabled();
  await expect(action(page, 'Deallocate')).toBeEnabled();

  await action(page, 'Deallocate').click();
  await expect(action(page, 'Delete…')).toBeDisabled();
  await expect(labState(page)).toHaveText('Stopped');
  await expect(action(page, 'Start')).toBeEnabled();
  await expect(action(page, 'Deallocate')).toBeDisabled();

  await action(page, 'Start').click();
  await expect(labState(page)).toHaveText('Ready');

  await action(page, 'Delete…').click();
  const dialog = page.getByRole('dialog');
  const confirm = dialog.getByRole('button', { name: 'Delete lab' });
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel(/Type .* to confirm/).fill(name.toUpperCase());
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel(/Type .* to confirm/).fill(name);
  await confirm.click();
  await expect(dialog).toBeHidden();

  await expect(labState(page)).toHaveText('Deleted');
  await page.goto('/labs');
  await expect(page.getByRole('link', { name })).toHaveCount(0);

  // The operation history and audit trail record everything.
  await page.goBack();
  await page.getByRole('link', { name: 'Audit' }).click();
  await expect(page.getByRole('cell', { name: 'lab.requested' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'job.succeeded' }).first()).toBeVisible();
});

test('refreshing during a job keeps accurate backend state', async ({ page }) => {
  const name = uniqueName('refresh');
  await createLab(page, name);
  await expect(labState(page)).toHaveText(/Requested|Provisioning/);

  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
  const progress = page.getByRole('status').filter({ hasText: 'Provision lab' });
  await expect(progress).toBeVisible();
  await expect(labState(page)).toHaveText('Ready');
  await expect(progress).toHaveCount(0);
});

test('conflicting actions are disabled while a job is active', async ({ page }) => {
  await createLab(page, uniqueName('busy'));
  await expect(labState(page)).toHaveText(/Requested|Provisioning|Configuring/);
  await expect(action(page, 'Start')).toBeDisabled();
  await expect(action(page, 'Deallocate')).toBeDisabled();
  await expect(action(page, 'Delete…')).toBeDisabled();
  await expect(action(page, 'Extend 2h')).toBeEnabled();
  await expect(labState(page)).toHaveText('Ready');
});

test('lab list shows state, region, owner, expiry, power, and current job', async ({ page }) => {
  const name = uniqueName('list');
  await createLab(page, name);
  await page.goto('/labs');
  const row = page.getByRole('row').filter({ has: page.getByRole('link', { name }) });
  await expect(row).toContainText('centralus');
  await expect(row).toContainText('dev-user');
  await expect(row).toContainText(/in 1h|in 2h/);
  await expect(row.getByRole('status')).toContainText('Provision lab');
  await expect(row).toContainText('Ready');
  await expect(row).toContainText('Running');
});

test('a failing lab reports the backend failure', async ({ page }) => {
  await createLab(page, uniqueName('fail'));
  await expect(labState(page)).toHaveText('Failed');
  await expect(page.locator('.reason')).toContainText('unavailable');
  await expect(action(page, 'Delete…')).toBeEnabled();
  await expect(page.getByRole('cell', { name: 'failed' })).toBeVisible();
});

test('form validation is shown inline', async ({ page }) => {
  await page.goto('/labs/new');
  await page.getByLabel('Lab name').fill('Bad Name');
  await page.getByRole('button', { name: 'Create lab' }).click();
  await expect(page.getByText(/lowercase letters, digits, or hyphens; start/)).toBeVisible();
  await expect(page).toHaveURL(/\/labs\/new$/);
});
