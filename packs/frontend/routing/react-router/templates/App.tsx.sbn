import { NavLink, Route, Routes } from 'react-router';
import type { ModuleRoute } from '@/modules';
import { Home } from '@/pages/Home';

const moduleRoutes = Object.values(import.meta.glob<ModuleRoute>('./modules/*/routes.tsx', { eager: true, import: 'route' }));

export function App() {
  return (
    <div>
      <nav aria-label="Modules" className="flex gap-4 p-2">
        <NavLink to="/">Home</NavLink>
        {moduleRoutes.map((route) => (
          <NavLink key={route.path} to={route.path}>
            {route.title}
          </NavLink>
        ))}
      </nav>
      <Routes>
        <Route path="/" element={<Home />} />
        {moduleRoutes.map((route) => (
          <Route key={route.path} path={`${route.path}/*`} element={route.element} />
        ))}
      </Routes>
    </div>
  );
}
