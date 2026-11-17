import { afterEach, expect, test, vi } from 'vitest';
import page from '../../index.html?raw';

// The script index.html runs before the app, so the page shows the chosen theme from its first paint.
const script = /<script>([\s\S]*?)<\/script>/.exec(page)?.[1] ?? '';

function prefersDark(dark: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: dark && query === '(prefers-color-scheme: dark)' }));
}

function runScript() {
  // index.html's own text, run in the page's global scope as the browser runs it.
  new Function(script)();
}

const dark = () => document.documentElement.classList.contains('dark');

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.removeAttribute('class');
});

test('index.html runs the theme script in its head', () => {
  expect(script).toContain("localStorage.getItem('theme')");
});

test('before the first paint index.html sets the stored dark theme', () => {
  prefersDark(false);
  localStorage.setItem('theme', 'dark');

  runScript();

  expect(dark()).toBe(true);
});

test('before the first paint index.html keeps a stored light theme against a dark system', () => {
  prefersDark(true);
  localStorage.setItem('theme', 'light');

  runScript();

  expect(dark()).toBe(false);
});

test('before the first paint index.html follows the system without a stored choice', () => {
  prefersDark(true);

  runScript();

  expect(dark()).toBe(true);
});

test('before the first paint index.html follows the system with storage blocked', () => {
  prefersDark(true);
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new DOMException('Storage is blocked.', 'SecurityError');
  });

  runScript();

  expect(dark()).toBe(true);
});
