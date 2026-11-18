import { render, screen } from '@testing-library/vue';
import { expect, test } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { notify } from './notify';

test("a mutation's success shows as a status message", async () => {
  render(Toaster);

  notify.success('The invoice was saved.');

  expect(await screen.findByRole('status')).toHaveTextContent('The invoice was saved.');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test("a mutation's failure shows as an alert", async () => {
  render(Toaster);

  notify.failure('The invoice could not be saved.');

  expect(await screen.findByRole('alert')).toHaveTextContent('The invoice could not be saved.');
});
