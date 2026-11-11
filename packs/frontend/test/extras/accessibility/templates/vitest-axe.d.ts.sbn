import 'vitest';
import type { AxeMatchers } from 'vitest-axe/matchers';

// Matchers, not Assertion: @testing-library/jest-dom declares Assertion with type parameters that differ from Vitest's,
// so every declaration of Assertion is an error (TS2428). Each declaration of Matchers repeats Vitest's own.
declare module 'vitest' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type, @typescript-eslint/no-unused-vars
  interface Matchers<R extends void | Promise<void> = void | Promise<void>, T = unknown> extends AxeMatchers {}
}
