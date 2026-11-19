import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { onThemeChange, readChartTheme, toRgb } from './chart-theme';

// Both palettes as src/index.css has them: :root the light one, .dark the dark one.
const palettes = `
:root {
  --chart-1: oklch(0.627955 0.257683 29.2339);
  --chart-2: rgb(0, 128, 0);
  --chart-3: oklch(0.452014 0.313214 264.052);
  --chart-4: oklch(0 0 0);
  --chart-5: oklch(1 0 0);
  --foreground: oklch(0 0 0);
  --muted-foreground: rgb(100, 100, 100);
  --border: rgb(200, 200, 200);
}
.dark {
  --chart-1: oklch(1 0 0);
  --foreground: oklch(1 0 0);
  --border: oklch(1 0 0 / 10%);
}`;

let style: HTMLStyleElement;

beforeEach(() => {
  style = document.createElement('style');
  style.textContent = palettes;
  document.head.append(style);
});

afterEach(() => {
  style.remove();
  document.documentElement.removeAttribute('class');
});

test('an oklch colour becomes the rgb a canvas chart library can parse, its alpha kept; any other colour stays as it is', () => {
  expect(toRgb('oklch(0.627955 0.257683 29.2339)')).toBe('rgb(255, 0, 0)');
  expect(toRgb('oklch(62.7955% 0.257683 29.2339deg)')).toBe('rgb(255, 0, 0)');
  expect(toRgb('oklch(0.452014 0.313214 264.052)')).toBe('rgb(0, 0, 255)');
  expect(toRgb('oklch(1 0 0 / 10%)')).toBe('rgba(255, 255, 255, 0.1)');
  expect(toRgb('oklch(0 0 0 / 0.5)')).toBe('rgba(0, 0, 0, 0.5)');
  expect(toRgb('#0000ff')).toBe('#0000ff');
  expect(toRgb('rgb(1, 2, 3)')).toBe('rgb(1, 2, 3)');
});

test("the chart theme is the light palette's --chart-1…5, its text and its lines", () => {
  expect(readChartTheme()).toEqual({
    palette: ['rgb(255, 0, 0)', 'rgb(0, 128, 0)', 'rgb(0, 0, 255)', 'rgb(0, 0, 0)', 'rgb(255, 255, 255)'],
    text: 'rgb(0, 0, 0)',
    mutedText: 'rgb(100, 100, 100)',
    line: 'rgb(200, 200, 200)',
  });
});

test("in dark mode the chart theme is the dark palette's", () => {
  document.documentElement.classList.add('dark');

  const theme = readChartTheme();

  expect(theme.palette[0]).toBe('rgb(255, 255, 255)');
  expect(theme.text).toBe('rgb(255, 255, 255)');
  expect(theme.line).toBe('rgba(255, 255, 255, 0.1)');
});

test('without the theme the chart keeps its library colours: the palette is empty and nothing else is set', () => {
  style.remove();

  expect(readChartTheme()).toEqual({ palette: [], text: undefined, mutedText: undefined, line: undefined });
});

test('a change of theme on <html> is reported until it is stopped', async () => {
  const changed = vi.fn();
  const stop = onThemeChange(changed);

  document.documentElement.classList.add('dark');
  await vi.waitFor(() => {
    expect(changed).toHaveBeenCalledTimes(1);
  });

  stop();
  document.documentElement.classList.remove('dark');
  await Promise.resolve();
  expect(changed).toHaveBeenCalledTimes(1);
});
