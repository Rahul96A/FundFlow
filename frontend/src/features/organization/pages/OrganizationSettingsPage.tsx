import Box from '@mui/material/Box'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Tab from '@mui/material/Tab'
import Tabs from '@mui/material/Tabs'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { usePermission } from '@/app/store/authStore'
import { PageHeader } from '@/components/ui/PageHeader'
import { ErrorState, LoadingState } from '@/components/ui/States'
import { Permissions } from '@/shared/constants/permissions'
import { OrganizationProfileFormPanel } from '../components/OrganizationProfileForm'
import { OrganizationSettingsFormPanel } from '../components/OrganizationSettingsForm'
import { useOrganization } from '../hooks/useOrganization'

export default function OrganizationSettingsPage() {
  const [tab, setTab] = useState<'profile' | 'settings'>('profile')
  const canEdit = usePermission(Permissions.Organization.Update)
  const organization = useOrganization()

  return (
    <>
      <PageHeader
        title="Organization"
        subtitle={organization.data ? `Public address: /give/${organization.data.slug}` : 'Your organization profile, regional settings and branding.'}
      />
      <Card>
        <Tabs value={tab} onChange={(_event, value) => setTab(value)} aria-label="Organization settings sections" sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab value="profile" label="Profile" id="org-tab-profile" aria-controls="org-panel-profile" />
          <Tab value="settings" label="Regional & branding" id="org-tab-settings" aria-controls="org-panel-settings" />
        </Tabs>
        <CardContent sx={{ p: { xs: 2, md: 3 } }}>
          {organization.isPending ? (
            <LoadingState variant="skeleton" lines={6} label="Loading organization" />
          ) : organization.isError ? (
            <ErrorState error={organization.error} onRetry={() => void organization.refetch()} />
          ) : (
            <Box role="tabpanel" id={`org-panel-${tab}`} aria-labelledby={`org-tab-${tab}`}>
              {tab === 'profile' ? (
                <OrganizationProfileFormPanel organization={organization.data} canEdit={canEdit} />
              ) : (
                <OrganizationSettingsFormPanel organization={organization.data} canEdit={canEdit} />
              )}
              <Typography variant="caption" color="textSecondary" sx={{ display: 'block', mt: 3 }}>
                The public address (slug) is fixed so shared links keep working.
              </Typography>
            </Box>
          )}
        </CardContent>
      </Card>
    </>
  )
}
