import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { expect, test } from 'vitest';
import { axe } from 'vitest-axe';
import { App } from './App';

test('the start page has no accessibility violations axe can find', async () => {
  const { container } = render(
    <MemoryRouter>
      <App />
    </MemoryRouter>,
  );

  expect(await axe(container)).toHaveNoViolations();
});
