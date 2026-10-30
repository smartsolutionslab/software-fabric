## Frontend state: Redux Toolkit

- Server data: use the generated RTK Query hooks; never copy responses into a slice.
- Client state: one slice per concern, created with `createSlice`, registered in `src/store.ts`.
- In components use `useAppSelector` / `useAppDispatch` from `@/hooks`, not the untyped `useSelector` / `useDispatch`.
