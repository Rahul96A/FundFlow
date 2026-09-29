import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import { endOfDay, formatISO, parseISO, startOfDay } from 'date-fns'

export interface DateRange {
  /** Inclusive start, as an ISO timestamp at the start of the local day. */
  from: string | null
  /** Inclusive end, as an ISO timestamp at the end of the local day. */
  to: string | null
}

interface DateRangePickerProps {
  value: DateRange
  onChange: (range: DateRange) => void
  fromLabel?: string
  toLabel?: string
}

const toInput = (iso: string | null) => (iso ? formatISO(parseISO(iso), { representation: 'date' }) : '')

/**
 * Two native date inputs (accessible, keyboard-friendly, mobile-friendly). Values are converted to whole local days:
 * picking 12 March means 00:00:00 to 23:59:59.999 of the 12th, on the viewer's clock.
 */
export function DateRangePicker({ value, onChange, fromLabel = 'From', toLabel = 'To' }: DateRangePickerProps) {
  return (
    <Stack direction="row" spacing={1.5}>
      <TextField
        type="date"
        label={fromLabel}
        value={toInput(value.from)}
        onChange={(event) =>
          onChange({
            ...value,
            from: event.target.value ? startOfDay(parseISO(event.target.value)).toISOString() : null,
          })
        }
        slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: toInput(value.to) || undefined } }}
        fullWidth={false} sx={{ minWidth: 160 }}
      />
      <TextField
        type="date"
        label={toLabel}
        value={toInput(value.to)}
        onChange={(event) =>
          onChange({
            ...value,
            to: event.target.value ? endOfDay(parseISO(event.target.value)).toISOString() : null,
          })
        }
        slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: toInput(value.from) || undefined } }}
        fullWidth={false} sx={{ minWidth: 160 }}
      />
    </Stack>
  )
}
