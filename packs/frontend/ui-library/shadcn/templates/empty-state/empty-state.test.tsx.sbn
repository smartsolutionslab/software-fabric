import { render, screen } from '@testing-library/react';
import { Inbox } from 'lucide-react';
import { expect, test } from 'vitest';
import { Button } from './button';
import { EmptyState } from './empty-state';

test('the empty state says what is missing, offers its action and hides its icon from screen readers', () => {
  const { container } = render(
    <EmptyState icon={<Inbox />} action={<Button>Add an invoice</Button>}>
      No invoices yet.
    </EmptyState>,
  );

  expect(screen.getByText('No invoices yet.')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Add an invoice' })).toBeInTheDocument();
  // Lucide hides its own svg; the empty state hides whatever icon it is given, a labelled one too.
  expect(container.querySelector('svg')?.parentElement).toHaveAttribute('aria-hidden', 'true');
});
