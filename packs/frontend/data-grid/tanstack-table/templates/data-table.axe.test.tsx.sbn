import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Inbox } from 'lucide-react';
import { afterEach, expect, test } from 'vitest';
import { axe } from 'vitest-axe';
import { DataTable } from './data-table';
import { columnsOf } from './data-table-columns';
import { EmptyState } from './empty-state';

interface Person {
  name: string;
  team: string;
}

const people = columnsOf<Person>();
const columns = people.columns([
  people.accessor('name', { header: 'Name' }),
  people.accessor('team', { header: 'Team' }),
]);
const data: Person[] = [
  { name: 'first', team: 'blue' },
  { name: 'second', team: 'green' },
];
const none: Person[] = [];
const empty = (
  <EmptyState icon={<Inbox />} action={<button type="button">Add a person</button>}>
    No people yet.
  </EmptyState>
);

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);
afterEach(() => {
  document.documentElement.removeAttribute('class');
  localStorage.clear();
});

const themes = [
  { name: 'light', switchOn: () => undefined },
  {
    name: 'dark',
    // As the theme toggle does: the choice stored, the class on <html>.
    switchOn: () => {
      localStorage.setItem('theme', 'dark');
      document.documentElement.classList.add('dark');
    },
  },
];

const firstOfMany = { page: 1, pageSize: 25, totalCount: 312 };

const states = [
  { state: 'while it loads', table: () => <DataTable columns={columns} data={none} loading empty={empty} /> },
  { state: 'when it is empty', table: () => <DataTable columns={columns} data={none} empty={empty} /> },
  { state: 'with rows', table: () => <DataTable columns={columns} data={data} empty={empty} /> },
  {
    state: 'with actions, a page and columns open',
    table: () => (
      <DataTable
        columns={columns}
        data={data}
        empty={empty}
        actions={(person) => <button type="button">Edit {person.name}</button>}
        paging={firstOfMany}
      />
    ),
    open: true,
  },
];

// Whichever test renders first in a file also pays for warming up jsdom, React and axe.
test.each(
  states.flatMap(({ state, table, open = false }) =>
    themes.map(({ name, switchOn }) => ({ state, table, open, name, switchOn })),
  ),
)(
  'the table has no accessibility violations axe can find $state in the $name theme',
  { timeout: 20_000 },
  async ({ table, open, switchOn }) => {
    switchOn();
    const { container } = render(table());
    if (open) {
      await userEvent.click(screen.getByRole('button', { name: 'Columns' }));
    }

    expect(await axe(container)).toHaveNoViolations();
  },
);
