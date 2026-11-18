import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, test } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { notify } from './notify';

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

test("a mutation's success shows as a status message", async () => {
  render(<Toaster />);

  act(() => {
    notify.success('The invoice was saved.');
  });

  expect(await screen.findByRole('status')).toHaveTextContent('The invoice was saved.');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test("a mutation's failure shows as an alert", async () => {
  render(<Toaster />);

  act(() => {
    notify.failure('The invoice could not be saved.');
  });

  expect(await screen.findByRole('alert')).toHaveTextContent('The invoice could not be saved.');
});
