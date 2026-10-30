import { type ColumnDef, type RowData, useTable } from '@tanstack/react-table';
import { features, type Features } from './data-table-columns';

interface DataTableProps<TRow extends RowData> {
  columns: ColumnDef<Features, TRow, unknown>[];
  data: TRow[];
}

/** A plain table over TanStack Table; keep columns and data stable (module scope or memoised). */
export function DataTable<TRow extends RowData>({ columns, data }: DataTableProps<TRow>) {
  const table = useTable({ features, columns, data });

  return (
    <table className="w-full text-sm">
      <thead>
        {table.getHeaderGroups().map((group) => (
          <tr key={group.id}>
            {group.headers.map((header) => (
              <th key={header.id} className="border-b p-2 text-left font-medium">
                {header.isPlaceholder ? null : <table.FlexRender header={header} />}
              </th>
            ))}
          </tr>
        ))}
      </thead>
      <tbody>
        {table.getRowModel().rows.map((row) => (
          <tr key={row.id}>
            {row.getAllCells().map((cell) => (
              <td key={cell.id} className="border-b p-2">
                <table.FlexRender cell={cell} />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
