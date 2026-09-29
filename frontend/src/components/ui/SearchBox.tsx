import ClearIcon from '@mui/icons-material/Clear'
import SearchIcon from '@mui/icons-material/Search'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import TextField from '@mui/material/TextField'
import { useEffect, useRef, useState } from 'react'

interface SearchBoxProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  label?: string
  debounceMs?: number
  minWidth?: number
}

/**
 * Debounced search input. The parent owns the committed value (typically a URL parameter); this component owns what
 * is being typed, and only reports it once typing pauses.
 */
export function SearchBox({
  value,
  onChange,
  placeholder = 'Search',
  label = 'Search',
  debounceMs = 350,
  minWidth = 260,
}: SearchBoxProps) {
  const [draft, setDraft] = useState(value)
  const lastCommitted = useRef(value)

  // Follow external changes (e.g. "Clear filters") without fighting the user's typing.
  useEffect(() => {
    if (value !== lastCommitted.current) {
      lastCommitted.current = value
      setDraft(value)
    }
  }, [value])

  useEffect(() => {
    if (draft === lastCommitted.current) {
      return
    }
    const handle = window.setTimeout(() => {
      lastCommitted.current = draft
      onChange(draft)
    }, debounceMs)
    return () => window.clearTimeout(handle)
  }, [draft, debounceMs, onChange])

  return (
    <TextField
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      placeholder={placeholder}
      type="search"
      label={label}
      sx={{ minWidth, maxWidth: { md: 360 } }}
      slotProps={{
        htmlInput: { maxLength: 100 },
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" />
            </InputAdornment>
          ),
          endAdornment: draft ? (
            <InputAdornment position="end">
              <IconButton
                size="small"
                aria-label="Clear search"
                onClick={() => {
                  setDraft('')
                  lastCommitted.current = ''
                  onChange('')
                }}
              >
                <ClearIcon fontSize="small" />
              </IconButton>
            </InputAdornment>
          ) : undefined,
        },
      }}
    />
  )
}
