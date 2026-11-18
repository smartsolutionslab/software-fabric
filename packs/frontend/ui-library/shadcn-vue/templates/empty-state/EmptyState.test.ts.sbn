import { Inbox } from '@lucide/vue';
import { render, screen } from '@testing-library/vue';
import { expect, test } from 'vitest';
import { h } from 'vue';
import { Button } from '../button';
import { EmptyState } from '.';

test('the empty state says what is missing, offers its action and hides its icon from screen readers', () => {
  const { container } = render(EmptyState, {
    slots: {
      icon: () => h(Inbox),
      default: 'No invoices yet.',
      action: () => h(Button, null, () => 'Add an invoice'),
    },
  });

  expect(screen.getByText('No invoices yet.')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Add an invoice' })).toBeInTheDocument();
  // Lucide hides its own svg; the empty state hides whatever icon it is given, a labelled one too.
  expect(container.querySelector('svg')?.parentElement).toHaveAttribute('aria-hidden', 'true');
});
