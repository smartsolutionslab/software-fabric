{{~ if project["frontend.framework"] == "vue" ~}}
## Styling: CSS Modules

- Style a component with a `<Name>.module.css` beside it: `import styles from './<Name>.module.css';`, then `:class="styles.name"`; for several, Vue's own `:class` array.
- No utility classes, no `class="..."` strings of styling, no `<style>` blocks; global CSS only in `src/index.css`.
- Write class names out (`styles.name`, not `styles[name]`): `src/cssModules.test.ts` fails for one without a rule with declarations in its module.
{{~ else ~}}
## Styling: CSS Modules

- Style a component with a `<Name>.module.css` beside it: `import styles from './<Name>.module.css';`, then `className={styles.name}`; for several, a template string of them.
- No utility classes, no `className="..."` strings of styling, no inline `style` for layout; global CSS only in `src/index.css`.
- Write class names out (`styles.name`, not `styles[name]`): `src/cssModules.test.ts` fails for one without a rule with declarations in its module.
{{~ end ~}}
