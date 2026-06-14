import { Link, Outlet } from 'react-router-dom'

/** Storefront layout: header + routed content. Rendered as a React Router layout route. */
export function StorefrontShell() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-4">
          <Link to="/" className="text-lg font-semibold tracking-tight">
            Retail OMS
          </Link>
          <nav className="text-sm">
            <Link to="/" className="text-muted-foreground hover:text-foreground">
              Catalog
            </Link>
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
