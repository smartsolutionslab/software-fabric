import { render, screen } from '@testing-library/vue';
import { expect, test } from 'vitest';
import { h } from 'vue';
import { Button } from '.';

test('a button is a button with the default look', () => {
  render(Button, { slots: { default: 'Save' } });

  expect(screen.getByRole('button', { name: 'Save' })).toHaveClass('bg-primary', 'text-primary-foreground');
});

test("with as-child the child takes the button's look and stays what it is", () => {
  render(Button, {
    props: { asChild: true, variant: 'outline' },
    slots: { default: () => h('a', { href: '/billing' }, 'Billing') },
  });

  expect(screen.getByRole('link', { name: 'Billing' })).toHaveClass('border', 'bg-background');
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test.each([
  ['destructive', 'bg-destructive'],
  ['outline', 'bg-background'],
  ['secondary', 'bg-secondary'],
  ['ghost', 'hover:bg-accent'],
  ['link', 'text-primary'],
] as const)('the %s variant takes the theme from %s', (variant, look) => {
  render(Button, { props: { variant }, slots: { default: 'Go' } });

  expect(screen.getByRole('button', { name: 'Go' })).toHaveClass(look);
});

test.each([
  ['sm', 'h-8'],
  ['lg', 'h-10'],
  ['icon', 'size-9'],
] as const)('the %s size is %s', (size, look) => {
  render(Button, { props: { size }, attrs: { 'aria-label': 'Go' } });

  expect(screen.getByRole('button', { name: 'Go' })).toHaveClass(look);
});
