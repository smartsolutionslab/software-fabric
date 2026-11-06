import { expect, type Locator, type Page, test } from '@playwright/test';

/** Where the element is on the page; it must be visible. */
async function box(locator: Locator) {
  const found = await locator.boundingBox();
  if (!found) {
    throw new Error('The element is not on the page.');
  }
  return found;
}

/** How many of the grid's 12 columns the widget spans: its width against the grid's, which a scroll bar may change. */
async function span(widget: Locator, page: Page) {
  const grid = await box(page.locator('.react-grid-layout'));
  return Math.round(((await box(widget)).width / grid.width) * 12);
}

test('a user builds a dashboard, resizes a widget, saves it and finds it again after a reload', async ({ page }) => {
  const name = `E2E ${Date.now()}`;
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Signed in as developer')).toBeVisible();
  await page.getByRole('link', { name: 'Dashboards' }).click();

  await page.getByRole('button', { name: 'New dashboard' }).click();
  await page.getByLabel('Dashboard name').fill(name);
  await page.getByLabel('Widget').selectOption({ label: 'Widgets per dashboard' });
  await page.getByLabel('Shown as').selectOption('bar');
  await page.getByRole('button', { name: 'Add widget' }).click();
  const widget = page.getByRole('region', { name: 'Widgets per dashboard' });
  await expect(widget).toBeVisible();

  const before = await box(widget);
  const handle = widget
    .locator('xpath=ancestor::*[contains(@class, "react-grid-item")]')
    .locator('.react-resizable-handle');
  const grip = await box(handle);
  await page.mouse.move(grip.x + grip.width / 2, grip.y + grip.height / 2);
  await page.mouse.down();
  await page.mouse.move(grip.x + 300, grip.y + 120, { steps: 10 });
  await page.mouse.up();
  await expect.poll(async () => (await box(widget)).width).toBeGreaterThan(before.width + 100);

  await page.getByRole('button', { name: 'Save dashboard' }).click();
  await expect(page.getByRole('status')).toHaveText('Saved.');
  // Measured once the grid's resize transition has ended; the saved span is what the reload must show.
  const resized = await span(widget, page);
  expect(resized).toBeGreaterThan(6);

  await page.reload();
  await expect(page.getByText('Signed in as developer')).toBeVisible();
  await page.getByRole('link', { name: 'Dashboards' }).click();
  await page.getByRole('button', { name }).click();
  const reloaded = page.getByRole('region', { name: 'Widgets per dashboard' });
  await expect(reloaded).toBeVisible();
  await expect(reloaded.getByRole('img', { name: 'Widgets per dashboard' })).toBeVisible();
  await expect.poll(async () => span(reloaded, page)).toBe(resized);
  await expect(page.getByLabel('Dashboard name')).toHaveValue(name);
});
