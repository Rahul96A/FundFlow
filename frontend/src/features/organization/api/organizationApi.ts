import { http } from '@/shared/api/http'
import type {
  Organization,
  OrganizationOverview,
  UpdateOrganizationInput,
  UpdateSettingsInput,
} from '../types/organization.types'

export const organizationApi = {
  get: () => http.get<Organization>('/organization').then((r) => r.data),

  update: (input: UpdateOrganizationInput) => http.put<Organization>('/organization', input).then((r) => r.data),

  updateSettings: (input: UpdateSettingsInput) =>
    http.put<Organization>('/organization/settings', input).then((r) => r.data),

  overview: () => http.get<OrganizationOverview>('/organization/overview').then((r) => r.data),
}
