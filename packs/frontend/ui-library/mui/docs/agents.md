## UI components: MUI

- Compose screens from `@mui/material` (`import { Button, Stack, Typography } from '@mui/material'`); lay out with `Stack` and `Box`, and style through their props and `sx`.
- Take colours, spacing and radii from the theme in `sx` (`color: 'text.secondary'`, `bgcolor: 'primary.main'`, `p: 1`, `borderRadius: 1`), never as values written in a component.
- Form fields stay labelled native controls (`<label>` around `<Box component="input" />`), so React Hook Form and TanStack Form register them as they are.
- Do not install Ant Design, Mantine, Chakra, Fluent UI, Radix or Headless UI (lint refuses them).
