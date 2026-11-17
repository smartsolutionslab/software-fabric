import { render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import ThemeToggle from './ThemeToggle.vue';

// jsdom has no matchMedia: the system's preference is this stand-in, which a test changes.
const system = { dark: false, listeners: new Set<() => void>() };

function systemPrefers(theme: 'light' | 'dark') {
  system.dark = theme === 'dark';
  system.listeners.forEach((listener) => {
    listener();
  });
}

beforeEach(() => {
  vi.stubGlobal('matchMedia', (query: string) => ({
    get matches() {
      return system.dark && query === '(prefers-color-scheme: dark)';
    },
    addEventListener: (_: string, listener: () => void) => system.listeners.add(listener),
    removeEventListener: (_: string, listener: () => void) => system.listeners.delete(listener),
  }));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.removeAttribute('class');
  system.dark = false;
  system.listeners.clear();
});

const dark = () => document.documentElement.classList.contains('dark');
const option = (name: string) => screen.getByRole('button', { name });

test("with no choice stored the app follows the system's theme, also as it changes", () => {
  systemPrefers('dark');

  render(ThemeToggle);

  expect(option('System')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(true);
  systemPrefers('light');
  expect(dark()).toBe(false);
});

test('a choice is shown at once and remembered for the next visit', async () => {
  systemPrefers('dark');
  const { unmount } = render(ThemeToggle);

  await userEvent.click(option('Light'));

  expect(dark()).toBe(false);
  expect(option('Light')).toHaveAttribute('aria-pressed', 'true');
  unmount();
  render(ThemeToggle);
  expect(option('Light')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(false);
});

test('choosing system again forgets the choice', async () => {
  render(ThemeToggle);
  await userEvent.click(option('Dark'));
  expect(dark()).toBe(true);
  expect(localStorage.getItem('theme')).toBe('dark');

  await userEvent.click(option('System'));

  expect(dark()).toBe(false);
  expect(localStorage.getItem('theme')).toBeNull();
});

test('with storage blocked the toggle still renders and switches the theme', async () => {
  const blocked = () => {
    throw new DOMException('Storage is blocked.', 'SecurityError');
  };
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);
  render(ThemeToggle);
  expect(option('System')).toHaveAttribute('aria-pressed', 'true');

  await userEvent.click(option('Dark'));

  expect(option('Dark')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(true);
});
