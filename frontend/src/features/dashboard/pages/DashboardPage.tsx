import ApartmentIcon from '@mui/icons-material/Apartment'
import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import GroupIcon from '@mui/icons-material/Group'
import MailOutlinedIcon from '@mui/icons-material/MailOutlined'
import RadioButtonUncheckedIcon from '@mui/icons-material/RadioButtonUnchecked'
import ShieldIcon from '@mui/icons-material/Shield'
import VerifiedUserIcon from '@mui/icons-material/VerifiedUser'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import CardHeader from '@mui/material/CardHeader'
import Chip from '@mui/material/Chip'
import Grid from '@mui/material/Grid'
import LinearProgress from '@mui/material/LinearProgress'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import { useTheme } from '@mui/material/styles'
import { format, parseISO } from 'date-fns'
import { Link as RouterLink } from 'react-router'
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useAuthStore, usePermission } from '@/app/store/authStore'
import { ActivityFeed } from '@/components/ui/ActivityFeed'
import { ChartCard } from '@/components/ui/ChartCard'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { ErrorState } from '@/components/ui/States'
import { describeAction } from '@/features/audit/utils/describeAction'
import { useOverview } from '@/features/organization/hooks/useOrganization'
import { paths } from '@/shared/constants/paths'
import { Permissions } from '@/shared/constants/permissions'
import { formatDateTime, formatNumber } from '@/shared/utils/format'

function greeting(now = new Date()): string {
  const hour = now.getHours()
  return hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'
}

interface ChecklistItem {
  id: string
  label: string
  detail: string
  done: boolean
  to?: string
  soon?: boolean
}

export default function DashboardPage() {
  const theme = useTheme()
  const user = useAuthStore((s) => s.user)
  const overview = useOverview()
  const canManageUsers = usePermission(Permissions.User.Manage)
  const canEditOrg = usePermission(Permissions.Organization.Update)
  const timeZone = user?.organization?.timeZoneId

  const data = overview.data
  const checklist: ChecklistItem[] = [
    { id: 'account', label: 'Create your account', detail: 'Your organization workspace is ready.', done: true },
    { id: 'email', label: 'Verify your email', detail: 'Confirmed when you signed in.', done: true },
    {
      id: 'profile',
      label: 'Complete your organization profile',
      detail: 'Add a contact phone number and address for receipts.',
      done: data?.profileCompleted ?? false,
      to: canEditOrg ? paths.organization : undefined,
    },
    ...(canManageUsers
      ? [
          {
            id: 'team',
            label: 'Invite your team',
            detail: 'Give colleagues the right role from day one.',
            done: (data?.team?.totalUsers ?? 1) > 1,
            to: paths.users,
          },
        ]
      : []),
    {
      id: 'campaign',
      label: 'Launch your first campaign',
      detail: 'Campaigns, donations and giving pages arrive in the next release.',
      done: false,
      soon: true,
    },
  ]
  const completed = checklist.filter((item) => item.done).length
  const progress = Math.round((completed / checklist.length) * 100)

  const chartData =
    data?.activity?.byDay.map((point) => ({ label: format(parseISO(point.date), 'MMM d'), count: point.count })) ?? []
  const totalEvents = chartData.reduce((sum, point) => sum + point.count, 0)

  return (
    <>
      <PageHeader
        title={`${greeting()}, ${user?.firstName ?? ''}`}
        subtitle={user?.organization ? `Here is what is happening at ${user.organization.name}.` : undefined}
        actions={
          canManageUsers ? (
            <Button component={RouterLink} to={paths.users} variant="contained" startIcon={<GroupIcon />}>
              Invite teammates
            </Button>
          ) : undefined
        }
      />

      {overview.isError && !data ? (
        <ErrorState error={overview.error} onRetry={() => void overview.refetch()} title="We could not load your dashboard" />
      ) : (
        <Grid container spacing={2.5}>
          {data?.team || overview.isPending ? (
            <>
              <Grid size={{ xs: 6, lg: 3 }}>
                <StatCard label="Team members" value={data ? formatNumber(data.team?.totalUsers ?? 0) : ''} icon={<GroupIcon />} loading={overview.isPending} />
              </Grid>
              <Grid size={{ xs: 6, lg: 3 }}>
                <StatCard label="Active" value={data ? formatNumber(data.team?.activeUsers ?? 0) : ''} icon={<VerifiedUserIcon />} loading={overview.isPending} />
              </Grid>
              <Grid size={{ xs: 6, lg: 3 }}>
                <StatCard label="Pending invitations" value={data ? formatNumber(data.team?.pendingInvitations ?? 0) : ''} icon={<MailOutlinedIcon />} loading={overview.isPending} />
              </Grid>
              <Grid size={{ xs: 6, lg: 3 }}>
                <StatCard label="Roles" value={data ? formatNumber(data.team?.roleCount ?? 0) : ''} icon={<ShieldIcon />} loading={overview.isPending} />
              </Grid>
            </>
          ) : null}

          <Grid size={{ xs: 12, lg: data?.activity ? 5 : 12 }}>
            <Card sx={{ height: '100%' }}>
              <CardHeader
                title="Get set up"
                subheader={`${completed} of ${checklist.length} steps complete`}
                slotProps={{ title: { variant: 'h5', component: 'h2' } }}
                avatar={<ApartmentIcon color="primary" />}
              />
              <CardContent sx={{ pt: 0 }}>
                <LinearProgress
                  variant="determinate"
                  value={progress}
                  aria-label="Setup progress"
                  sx={{ height: 8, borderRadius: 4, mb: 1 }}
                />
                <List disablePadding aria-label="Setup checklist">
                  {checklist.map((item) => (
                    <ListItem
                      key={item.id}
                      disableGutters
                      secondaryAction={
                        item.soon ? (
                          <Chip label="Soon" size="small" variant="outlined" />
                        ) : item.to && !item.done ? (
                          <Button component={RouterLink} to={item.to} size="small">
                            Start
                          </Button>
                        ) : undefined
                      }
                    >
                      <ListItemIcon sx={{ minWidth: 36 }}>
                        {item.done ? (
                          <CheckCircleIcon color="success" aria-label="Done" />
                        ) : (
                          <RadioButtonUncheckedIcon color="disabled" aria-label="Not done" />
                        )}
                      </ListItemIcon>
                      <ListItemText
                        primary={item.label}
                        secondary={item.detail}
                        slotProps={{ primary: { sx: { textDecoration: item.done ? 'line-through' : 'none', color: item.done ? 'text.secondary' : 'text.primary' } } }}
                      />
                    </ListItem>
                  ))}
                </List>
              </CardContent>
            </Card>
          </Grid>

          {data?.activity || overview.isPending ? (
            <Grid size={{ xs: 12, lg: 7 }}>
              <ChartCard
                title="Team activity"
                subtitle="Recorded actions per day, last 14 days (UTC)"
                loading={overview.isPending}
                error={overview.isError ? overview.error : undefined}
                onRetry={() => void overview.refetch()}
                empty={totalEvents === 0}
                emptyTitle="No activity recorded yet"
                emptyDescription="Sign-ins, invitations and changes will show up here."
              >
                <div role="img" aria-label={`Team activity over the last 14 days: ${totalEvents} recorded actions.`} style={{ width: '100%', height: '100%' }}>
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={chartData} margin={{ top: 8, right: 8, bottom: 0, left: -16 }}>
                      <defs>
                        <linearGradient id="activityFill" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor={theme.palette.primary.main} stopOpacity={0.35} />
                          <stop offset="100%" stopColor={theme.palette.primary.main} stopOpacity={0.02} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" stroke={theme.palette.divider} vertical={false} />
                      <XAxis dataKey="label" tick={{ fontSize: 12, fill: theme.palette.text.secondary }} tickLine={false} axisLine={false} interval="preserveStartEnd" />
                      <YAxis allowDecimals={false} tick={{ fontSize: 12, fill: theme.palette.text.secondary }} tickLine={false} axisLine={false} />
                      <Tooltip
                        contentStyle={{ background: theme.palette.background.paper, border: `1px solid ${theme.palette.divider}`, borderRadius: 8 }}
                        labelStyle={{ color: theme.palette.text.primary, fontWeight: 600 }}
                      />
                      <Area type="monotone" dataKey="count" name="Actions" stroke={theme.palette.primary.main} strokeWidth={2} fill="url(#activityFill)" />
                    </AreaChart>
                  </ResponsiveContainer>
                </div>
              </ChartCard>
            </Grid>
          ) : null}

          {data?.activity ? (
            <Grid size={{ xs: 12 }}>
              <Card>
                <CardHeader
                  title="Recent activity"
                  action={
                    <Button component={RouterLink} to={paths.auditLogs} size="small">
                      View audit log
                    </Button>
                  }
                  slotProps={{ title: { variant: 'h5', component: 'h2' } }}
                />
                <CardContent sx={{ pt: 0 }}>
                  <ActivityFeed
                    ariaLabel="Recent activity"
                    emptyTitle="Nothing has happened yet"
                    items={data.activity.recent.map((entry) => {
                      const info = describeAction(entry.action)
                      return {
                        id: entry.id,
                        actor: entry.userEmail ?? 'System',
                        summary: info.label,
                        time: formatDateTime(entry.timestamp, timeZone),
                        icon: info.icon,
                      }
                    })}
                  />
                </CardContent>
              </Card>
            </Grid>
          ) : null}
        </Grid>
      )}
    </>
  )
}
