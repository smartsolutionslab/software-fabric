import { render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { expect, test, vi } from 'vitest';
import { computed } from 'vue';
import UserMenu from './UserMenu.vue';

// The name is the host's answer to /me; here it is the test's.
const me = vi.hoisted(() => ({ name: undefined as string | undefined }));
vi.mock('@/api/useCurrentUser', () => ({ useCurrentUser: () => computed(() => me.name) }));

const signoutRedirect = vi.hoisted(() => vi.fn(() => Promise.resolve()));
vi.mock('@/auth/useAuth', () => ({ useAuth: () => ({ signoutRedirect }) }));

test('the user menu names the signed-in user as /me does', () => {
  me.name = 'developer';
  render(UserMenu);

  expect(screen.getByText('Signed in as developer')).toBeInTheDocument();
});

test('until /me answers the user menu says only that someone is signed in', () => {
  me.name = undefined;
  render(UserMenu);

  expect(screen.getByText('Signed in')).toBeInTheDocument();
});

test('Sign out signs the user out at the identity provider', async () => {
  const user = userEvent.setup();
  render(UserMenu);

  await user.click(screen.getByRole('button', { name: 'Sign out' }));

  expect(signoutRedirect).toHaveBeenCalledOnce();
});
