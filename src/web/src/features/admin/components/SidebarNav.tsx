import { NavLink } from 'react-router-dom'
import { ROLE_SETS, hasAnyRole, type AdminArea } from '@/lib/auth/roleSets'
import { useAuthStore } from '@/lib/store/auth-store'
import { cn } from '@/lib/utils'

interface NavItem {
  to: string
  label: string
  /** The capability area gating this item; undefined = visible to any admin-area role (Dashboard). */
  area?: AdminArea
}

const ITEMS: NavItem[] = [
  { to: '/admin', label: 'Dashboard' },
  { to: '/admin/orders', label: 'Orders', area: 'orders' },
  { to: '/admin/risk', label: 'Risk queue', area: 'risk' },
  { to: '/admin/forecast', label: 'Forecast', area: 'forecast' },
  { to: '/admin/products', label: 'Products', area: 'catalog' },
  { to: '/admin/inventory', label: 'Inventory', area: 'inventory' },
  { to: '/admin/audit', label: 'Audit log', area: 'audit' },
  { to: '/admin/reports', label: 'Reports', area: 'reports' },
  { to: '/admin/chat', label: 'Chat sessions', area: 'chat' },
  { to: '/admin/users', label: 'Users', area: 'users' },
]

/**
 * Role-driven admin navigation — each item appears only if the current user holds a role for its
 * area (mirroring the backend policy). This is what makes Administrator / StoreManager / Staff each
 * see a DIFFERENT sidebar (PHASE_3_SCOPE.md §1 demo).
 */
export function SidebarNav() {
  const roles = useAuthStore((state) => state.user?.roles)
  const visible = ITEMS.filter(
    (item) => item.area === undefined || hasAnyRole(roles, ROLE_SETS[item.area]),
  )

  return (
    <nav className="flex flex-col gap-1">
      {visible.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end={item.to === '/admin'} // Dashboard is active only at exactly /admin
          className={({ isActive }) =>
            cn(
              'rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive
                ? 'bg-primary text-primary-foreground'
                : 'text-muted-foreground hover:bg-muted hover:text-foreground',
            )
          }
        >
          {item.label}
        </NavLink>
      ))}
    </nav>
  )
}
