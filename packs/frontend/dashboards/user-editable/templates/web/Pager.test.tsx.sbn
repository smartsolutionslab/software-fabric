import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { Pager } from './Pager';

afterEach(cleanup);

test('a total not known yet, as while a page loads, moves to no page and counts no pages', () => {
  const onPage = vi.fn();

  render(<Pager label="widgets" page={2} pageSize={100} total={undefined} onPage={onPage} />);

  expect(onPage).not.toHaveBeenCalled();
  expect(screen.queryByText(/^Page /)).not.toBeInTheDocument();
});

test('a known total below the current page moves to the last page there is', () => {
  const onPage = vi.fn();

  render(<Pager label="widgets" page={3} pageSize={100} total={150} onPage={onPage} />);

  expect(onPage).toHaveBeenCalledWith(2);
});
