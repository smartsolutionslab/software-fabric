/** The theme the user chose: light, dark, or system, which follows the operating system's (prefers-color-scheme). */
export type Theme = 'light' | 'dark' | 'system';

export const themes: readonly Theme[] = ['light', 'dark', 'system'];

// index.html's script reads the same key and query before the first paint, and every screen's axe check stores the key.
const key = 'theme';
const darkScheme = '(prefers-color-scheme: dark)';

/** The stored choice, or system when there is none or storage is blocked. */
export function storedTheme(): Theme {
  try {
    const stored = localStorage.getItem(key);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    // Storage is blocked (privacy settings, a sandboxed frame): the app follows the system.
    return 'system';
  }
}

/** Remembers the choice; system forgets it, so the app follows the system again. */
export function storeTheme(theme: Theme): void {
  try {
    if (theme === 'system') {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, theme);
    }
  } catch {
    // Storage is blocked: the choice holds until the page is loaded again.
  }
}

/** Whether the system prefers dark; false where the browser cannot say (jsdom has no matchMedia). */
function systemPrefersDark(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(darkScheme).matches;
}

/** Shows the theme: the dark one is the .dark class on <html>, which switches src/index.css to its dark palette. */
export function applyTheme(theme: Theme): void {
  const dark = theme === 'dark' || (theme === 'system' && systemPrefersDark());
  document.documentElement.classList.toggle('dark', dark);
}

/** Calls back whenever the system's preference changes, and returns what stops it. */
export function onSystemThemeChange(callback: () => void): () => void {
  if (typeof window.matchMedia !== 'function') {
    return () => undefined;
  }

  const query = window.matchMedia(darkScheme);
  query.addEventListener('change', callback);
  return () => {
    query.removeEventListener('change', callback);
  };
}
