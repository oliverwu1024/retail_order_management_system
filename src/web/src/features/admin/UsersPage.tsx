import { useState, type FormEvent } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { Modal } from '@/components/ui/dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { Pagination } from '@/components/ui/pagination'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import type { AdminUser } from '@/lib/api/types'
import { useAuthStore } from '@/lib/store/auth-store'
import { useAdminUsersQuery, useCreateUser } from './hooks/useAdminUsers'

const PAGE_SIZE = 20

/**
 * Back-office user management (StoreManager + Administrator). Lists accounts via the shared
 * DataTable and creates them via a Modal — the same primitives the rest of the admin uses. Only an
 * Administrator may create a Store Manager (the role option is hidden + the server enforces it).
 */
export function UsersPage() {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError } = useAdminUsersQuery({ page, pageSize: PAGE_SIZE })
  const isAdministrator =
    useAuthStore((state) => state.user?.roles.includes('Administrator')) ?? false

  const columns: Column<AdminUser>[] = [
    { key: 'email', header: 'Email', cell: (user) => user.email },
    { key: 'name', header: 'Display name', cell: (user) => user.displayName ?? '—' },
    {
      key: 'roles',
      header: 'Roles',
      cell: (user) => (
        <div className="flex flex-wrap gap-1">
          {(user.roles ?? []).map((role) => (
            <Badge key={role} variant="secondary">
              {role}
            </Badge>
          ))}
        </div>
      ),
    },
  ]

  return (
    <section className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Users</h1>
          <p className="text-sm text-muted-foreground">
            Back-office accounts.{' '}
            {isAdministrator
              ? 'You can create Staff and Store Managers.'
              : 'You can create Staff accounts.'}
          </p>
        </div>
        <CreateUserDialog canCreateManager={isAdministrator} />
      </div>

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load users. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, index) => (
            <Skeleton key={index} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            label="Users"
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(user) => user.id ?? user.email ?? ''}
            empty={
              <EmptyState
                title="No accounts"
                description="Create a Staff or Store Manager account to get started."
              />
            }
          />
          {data ? (
            <Pagination
              page={data.page ?? page}
              totalPages={data.totalPages ?? 1}
              hasPrevious={data.hasPrevious ?? false}
              hasNext={data.hasNext ?? false}
              onPageChange={setPage}
            />
          ) : null}
        </>
      )}
    </section>
  )
}

function CreateUserDialog({ canCreateManager }: { canCreateManager: boolean }) {
  const [open, setOpen] = useState(false)
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('Staff')
  const createUser = useCreateUser()

  function reset() {
    setEmail('')
    setDisplayName('')
    setPassword('')
    setRole('Staff')
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    createUser.mutate(
      { email, displayName, password, role },
      {
        onSuccess: () => {
          toast({ title: 'Account created' })
          reset()
          setOpen(false)
        },
        onError: (error) =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t create account',
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    )
  }

  return (
    <>
      <Button onClick={() => setOpen(true)}>Add account</Button>
      <Modal
        open={open}
        onOpenChange={setOpen}
        title="Add account"
        description="Create a back-office account. The new user signs in with the temporary password."
      >
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-1">
            <label htmlFor="u-email" className="text-xs font-medium">
              Email
            </label>
            <Input
              id="u-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="u-name" className="text-xs font-medium">
              Display name
            </label>
            <Input
              id="u-name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              required
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="u-pass" className="text-xs font-medium">
              Temporary password
            </label>
            <Input
              id="u-pass"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={12}
            />
            <p className="text-xs text-muted-foreground">
              At least 12 characters, with a letter and a digit.
            </p>
          </div>
          <div className="space-y-1">
            <label htmlFor="u-role" className="text-xs font-medium">
              Role
            </label>
            <Select id="u-role" value={role} onChange={(e) => setRole(e.target.value)}>
              <option value="Staff">Staff</option>
              {canCreateManager ? <option value="StoreManager">Store Manager</option> : null}
            </Select>
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={createUser.isPending}>
              {createUser.isPending ? 'Creating…' : 'Create account'}
            </Button>
          </div>
        </form>
      </Modal>
    </>
  )
}
