import { expect, test } from 'vitest';
import { z } from 'zod';
import { toTypedSchema } from './toTypedSchema';

const orderSchema = z.object({
  customer: z.object({ name: z.string().min(1, 'Enter a name') }),
  lines: z.array(z.object({ quantity: z.number().int().min(1, 'Order at least one') })),
  note: z.string().trim(),
});

test("a schema's messages reach the fields they belong to, nested and listed ones too", async () => {
  const result = await toTypedSchema(orderSchema).parse({
    customer: { name: '' },
    lines: [{ quantity: 1 }, { quantity: 0 }],
    note: '',
  });

  expect(result.value).toBeUndefined();
  expect(result.errors).toEqual([
    { path: 'customer.name', errors: ['Enter a name'] },
    { path: 'lines[1].quantity', errors: ['Order at least one'] },
  ]);
});

test('valid values come back as the schema puts them out', async () => {
  const result = await toTypedSchema(orderSchema).parse({
    customer: { name: 'Ada' },
    lines: [{ quantity: 2 }],
    note: '  fragile  ',
  });

  expect(result).toEqual({
    value: { customer: { name: 'Ada' }, lines: [{ quantity: 2 }], note: 'fragile' },
    errors: [],
  });
});
