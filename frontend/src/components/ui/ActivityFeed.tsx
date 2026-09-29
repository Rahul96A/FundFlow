import Avatar from '@mui/material/Avatar'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemAvatar from '@mui/material/ListItemAvatar'
import ListItemText from '@mui/material/ListItemText'
import type { ReactNode } from 'react'
import { EmptyState } from './States'

export interface ActivityItem {
  id: string
  /** Who or what did it, already formatted. */
  actor: string
  /** What happened, in a sentence. */
  summary: ReactNode
  /** Already formatted for display. */
  time: string
  icon: ReactNode
}

interface ActivityFeedProps {
  items: ActivityItem[]
  emptyTitle?: string
  emptyDescription?: string
  ariaLabel: string
}

/** Compact "who did what, when" list used on the dashboard. */
export function ActivityFeed({ items, emptyTitle = 'No activity yet', emptyDescription, ariaLabel }: ActivityFeedProps) {
  if (items.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />
  }

  return (
    <List disablePadding aria-label={ariaLabel}>
      {items.map((item) => (
        <ListItem key={item.id} disableGutters alignItems="flex-start" divider>
          <ListItemAvatar>
            <Avatar sx={{ width: 34, height: 34, bgcolor: 'action.hover', color: 'primary.main' }}>{item.icon}</Avatar>
          </ListItemAvatar>
          <ListItemText
            primary={item.summary}
            secondary={`${item.actor} · ${item.time}`}
            slotProps={{ primary: { variant: 'body2', sx: { fontWeight: 600 } }, secondary: { variant: 'caption' } }}
          />
        </ListItem>
      ))}
    </List>
  )
}
