import ApartmentIcon from '@mui/icons-material/Apartment'
import CampaignIcon from '@mui/icons-material/Campaign'
import DashboardIcon from '@mui/icons-material/Dashboard'
import EmailIcon from '@mui/icons-material/Email'
import EventIcon from '@mui/icons-material/Event'
import GavelIcon from '@mui/icons-material/Gavel'
import HandshakeIcon from '@mui/icons-material/Handshake'
import HistoryIcon from '@mui/icons-material/History'
import PaymentsIcon from '@mui/icons-material/Payments'
import PeopleAltIcon from '@mui/icons-material/PeopleAlt'
import SettingsIcon from '@mui/icons-material/Settings'
import ShieldIcon from '@mui/icons-material/Shield'
import SupervisorAccountIcon from '@mui/icons-material/SupervisorAccount'
import VolunteerActivismIcon from '@mui/icons-material/VolunteerActivism'
import BarChartIcon from '@mui/icons-material/BarChart'
import GroupsIcon from '@mui/icons-material/Groups'
import TuneIcon from '@mui/icons-material/Tune'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import type { ReactElement } from 'react'
import { Permissions } from '@/shared/constants/permissions'
import { paths } from '@/shared/constants/paths'

export interface NavItem {
  label: string
  icon: ReactElement
  /** Route to open. Items without one are announced but not yet built. */
  to?: string
  /** Any one of these permissions makes the item visible. Omit for "everyone in the organization". */
  permissions?: readonly string[]
  /** Delivered in a later phase: shown so the product's shape is visible, but not clickable. */
  soon?: boolean
}

export interface NavGroup {
  id: string
  label?: string
  items: NavItem[]
  /** Platform operators see only groups flagged `platform`; organization members never see them. */
  platform?: boolean
}

export const navigation: NavGroup[] = [
  {
    id: 'main',
    items: [{ label: 'Dashboard', icon: <DashboardIcon />, to: paths.dashboard }],
  },
  {
    id: 'fundraising',
    label: 'Fundraising',
    items: [
      { label: 'Campaigns', icon: <CampaignIcon />, permissions: [Permissions.Campaign.Read], soon: true },
      { label: 'Donations', icon: <PaymentsIcon />, permissions: [Permissions.Donation.Read], soon: true },
      { label: 'Recurring donations', icon: <HistoryIcon />, permissions: [Permissions.Donation.Read], soon: true },
    ],
  },
  {
    id: 'donors',
    label: 'Donors',
    items: [
      { label: 'All donors', icon: <PeopleAltIcon />, permissions: [Permissions.Donor.Read], soon: true },
      { label: 'Segments', icon: <TuneIcon />, permissions: [Permissions.Donor.Read], soon: true },
      { label: 'Relationships', icon: <GroupsIcon />, permissions: [Permissions.Donor.Read], soon: true },
    ],
  },
  {
    id: 'events',
    label: 'Events',
    items: [
      { label: 'Events', icon: <EventIcon />, permissions: [Permissions.Event.Read], soon: true },
      { label: 'Registrations & check-in', icon: <ManageAccountsIcon />, permissions: [Permissions.Event.Read], soon: true },
    ],
  },
  {
    id: 'auctions',
    label: 'Auctions',
    items: [{ label: 'Auctions, items & bids', icon: <GavelIcon />, permissions: [Permissions.Auction.Read], soon: true }],
  },
  {
    id: 'network',
    label: 'Network',
    items: [
      { label: 'Sponsors', icon: <HandshakeIcon />, permissions: [Permissions.Sponsor.Read], soon: true },
      { label: 'Volunteers', icon: <VolunteerActivismIcon />, permissions: [Permissions.Volunteer.Read], soon: true },
    ],
  },
  {
    id: 'communications',
    label: 'Communications',
    items: [{ label: 'Email campaigns & templates', icon: <EmailIcon />, permissions: [Permissions.Communication.Read], soon: true }],
  },
  {
    id: 'insights',
    label: 'Insights',
    items: [{ label: 'Reports', icon: <BarChartIcon />, permissions: [Permissions.Report.Read], soon: true }],
  },
  {
    id: 'settings',
    label: 'Settings',
    items: [
      { label: 'Organization', icon: <ApartmentIcon />, to: paths.organization },
      { label: 'Users', icon: <SupervisorAccountIcon />, to: paths.users, permissions: [Permissions.User.Read] },
      { label: 'Roles & permissions', icon: <ShieldIcon />, to: paths.roles, permissions: [Permissions.Role.Read] },
      { label: 'Payments', icon: <SettingsIcon />, permissions: [Permissions.Payment.Manage], soon: true },
      { label: 'Audit log', icon: <HistoryIcon />, to: paths.auditLogs, permissions: [Permissions.Audit.Read] },
    ],
  },
  {
    id: 'platform',
    label: 'Platform',
    platform: true,
    items: [
      {
        label: 'Organizations',
        icon: <ApartmentIcon />,
        to: paths.platformOrganizations,
        permissions: [Permissions.Platform.Manage],
      },
    ],
  },
]

interface Visibility {
  isPlatformUser: boolean
  permissions: readonly string[]
}

export function visibleNavigation(viewer: Visibility): NavGroup[] {
  const canSee = (item: NavItem) =>
    !item.permissions || item.permissions.some((permission) => viewer.permissions.includes(permission))

  return navigation
    .filter((group) => (viewer.isPlatformUser ? group.platform === true : group.platform !== true))
    .map((group) => ({ ...group, items: group.items.filter(canSee) }))
    .filter((group) => group.items.length > 0)
}

/** Where a signed-in user lands: organization members on the dashboard, platform operators on the registry. */
export function landingPath(isPlatformUser: boolean): string {
  return isPlatformUser ? paths.platformOrganizations : paths.dashboard
}
