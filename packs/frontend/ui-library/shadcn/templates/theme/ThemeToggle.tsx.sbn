import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { applyTheme, onSystemThemeChange, storedTheme, storeTheme, type Theme, themes } from './theme';

const labels: Record<Theme, string> = { light: 'Light', dark: 'Dark', system: 'System' };

/** Switches the app between the light theme, the dark one and the system's, and remembers the choice. */
export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(storedTheme);

  useEffect(() => {
    applyTheme(theme);
    if (theme !== 'system') {
      return undefined;
    }

    return onSystemThemeChange(() => {
      applyTheme('system');
    });
  }, [theme]);

  return (
    <div role="group" aria-label="Theme" className="ml-auto flex gap-1">
      {themes.map((option) => (
        <Button
          key={option}
          variant={option === theme ? 'secondary' : 'ghost'}
          size="sm"
          aria-pressed={option === theme}
          onClick={() => {
            storeTheme(option);
            setTheme(option);
          }}
        >
          {labels[option]}
        </Button>
      ))}
    </div>
  );
}
