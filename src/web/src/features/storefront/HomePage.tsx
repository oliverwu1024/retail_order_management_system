import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { useToast } from '@/hooks/use-toast'

// Storefront home — Phase 0 placeholder. Exercises every primitive in
// src/components/ui/ so the smoke test catches a regression in any of
// them.
export function HomePage() {
  const { toast } = useToast()

  return (
    <main className="container mx-auto max-w-3xl py-10">
      <Card>
        <CardHeader>
          <CardTitle>Retail OMS</CardTitle>
          <CardDescription>
            Frontend skeleton — primitives, providers, and router are wired.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Input placeholder="Search products (Phase 1)" />
          <p className="text-sm text-muted-foreground">
            Tailwind 3.4 theme tokens live in{' '}
            <code className="rounded bg-muted px-1">src/index.css</code>; components in{' '}
            <code className="rounded bg-muted px-1">src/components/ui/</code>.
          </p>
        </CardContent>
        <CardFooter className="gap-2">
          <Button
            onClick={() => toast({ title: 'Hello', description: 'Toast wiring works end-to-end.' })}
          >
            Fire a toast
          </Button>
          <Button asChild variant="outline">
            <Link to="/admin">Go to /admin</Link>
          </Button>
        </CardFooter>
      </Card>
    </main>
  )
}
