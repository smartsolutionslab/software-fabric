import { render, screen } from '@testing-library/react';
import { expect, test } from 'vitest';
import { DataTable } from './data-table';
import { columnsOf } from './data-table-columns';

interface Person {
  name: string;
}

const people = columnsOf<Person>();
const columns = people.columns([people.accessor('name', { header: 'Name' })]);
const data: Person[] = [{ name: 'first' }, { name: 'second' }];

test('the table shows a header and one row per item', () => {
  render(<DataTable columns={columns} data={data} />);

  expect(screen.getByRole('columnheader', { name: 'Name' })).toBeInTheDocument();
  expect(screen.getAllByRole('row')).toHaveLength(3);
});
