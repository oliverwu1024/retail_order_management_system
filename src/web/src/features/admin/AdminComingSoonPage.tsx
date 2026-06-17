import { EmptyState } from '@/components/ui/empty-state'

/** Placeholder for an admin area whose page lands in a later Phase-3 chunk. The route + sidebar
 *  entry exist now (so the role-driven nav demo works); the real page replaces this. */
export function AdminComingSoon({ title, note }: { title: string; note: string }) {
  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <EmptyState title="Coming soon" description={note} />
    </section>
  )
}
