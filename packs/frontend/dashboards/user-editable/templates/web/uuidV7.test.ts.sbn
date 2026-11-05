import { expect, test } from 'vitest';
import { uuidV7 } from './uuidV7';

test('the id is a version 7 UUID whose first 48 bits are the time in milliseconds', () => {
  const id = uuidV7(0x0196_1234_5678);

  expect(id).toMatch(/^01961234-5678-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
});

test('ids made later sort after earlier ones', () => {
  expect(uuidV7(1_000) < uuidV7(2_000)).toBe(true);
  expect(uuidV7()).not.toBe(uuidV7());
});
