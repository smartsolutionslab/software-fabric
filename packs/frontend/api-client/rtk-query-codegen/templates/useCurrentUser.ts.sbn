import { useGetMeQuery } from '@/api/generated';

/** The signed-in user's name as the host's /me answers it; undefined until it has answered, or when it names nobody. */
export function useCurrentUser(): string | undefined {
  const { data } = useGetMeQuery();
  return data?.name ?? undefined;
}
