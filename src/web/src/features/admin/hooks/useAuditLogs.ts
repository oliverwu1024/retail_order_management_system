import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { AuditLogPage } from '@/lib/api/types'

export interface AuditLogParams {
  page: number
  pageSize: number
  actor?: string
  entityType?: string
  entityId?: string
}

export const auditKeys = {
  all: ['admin', 'audit'] as const,
  list: (params: AuditLogParams) => ['admin', 'audit', 'list', params] as const,
}

/** Searches the audit trail (Audit.View). PascalCase query params match the AuditLogListQuery DTO. */
export function useAuditLogsQuery(params: AuditLogParams) {
  return useQuery({
    queryKey: auditKeys.list(params),
    queryFn: async (): Promise<AuditLogPage> => {
      const { data, error } = await apiClient.GET('/api/v1/audit-logs', {
        params: {
          query: {
            Actor: params.actor,
            EntityType: params.entityType,
            EntityId: params.entityId,
            Page: params.page,
            PageSize: params.pageSize,
          },
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load audit logs.')
      }
      return data.data
    },
  })
}
