import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Autocomplete from '@mui/material/Autocomplete'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Grid from '@mui/material/Grid'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useEffect, useMemo, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { FormSelect, FormTextField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { COMMON_CURRENCIES, COMMON_LOCALES, MONTHS, timeZones } from '@/shared/utils/regional'
import { useUpdateSettings } from '../hooks/useOrganization'
import { organizationSettingsSchema, type OrganizationSettingsForm } from '../schemas/organizationSchemas'
import type { Organization } from '../types/organization.types'

const FIELDS = ['timeZoneId', 'currencyCode', 'locale', 'fiscalYearStartMonth', 'logoUrl', 'brandColor'] as const

function toForm(org: Organization): OrganizationSettingsForm {
  const s = org.settings
  return {
    timeZoneId: s.timeZoneId,
    currencyCode: s.currencyCode,
    locale: s.locale,
    fiscalYearStartMonth: String(s.fiscalYearStartMonth),
    logoUrl: s.logoUrl ?? '',
    brandColor: s.brandColor ?? '',
  }
}

export function OrganizationSettingsFormPanel({ organization, canEdit }: { organization: Organization; canEdit: boolean }) {
  const update = useUpdateSettings()
  const [banner, setBanner] = useState<string | null>(null)
  const zones = useMemo(() => timeZones(), [])
  const currencies = useMemo(
    () =>
      COMMON_CURRENCIES.some((c) => c.value === organization.settings.currencyCode)
        ? COMMON_CURRENCIES
        : [...COMMON_CURRENCIES, { value: organization.settings.currencyCode, label: organization.settings.currencyCode }],
    [organization.settings.currencyCode],
  )
  const { control, handleSubmit, reset, setError, setValue, formState } = useForm<OrganizationSettingsForm>({
    resolver: zodResolver(organizationSettingsSchema),
    defaultValues: toForm(organization),
  })
  const brandColor = useWatch({ control, name: 'brandColor' })

  useEffect(() => reset(toForm(organization)), [organization, reset])

  const onSubmit = handleSubmit((values) => {
    setBanner(null)
    update.mutate(
      {
        timeZoneId: values.timeZoneId,
        currencyCode: values.currencyCode,
        locale: values.locale,
        fiscalYearStartMonth: Number(values.fiscalYearStartMonth),
        logoUrl: values.logoUrl.trim() === '' ? null : values.logoUrl.trim(),
        brandColor: values.brandColor.trim() === '' ? null : values.brandColor.trim(),
      },
      {
        onSuccess: () => toast.success('Settings saved.'),
        onError: (error) => {
          const unmatched = applyFieldErrors(error, FIELDS, setError)
          setBanner(unmatched[0] ?? (toApiError(error).isValidation ? null : toApiError(error).message))
        },
      },
    )
  })

  return (
    <form onSubmit={onSubmit} noValidate aria-label="Regional and branding settings">
      <Stack spacing={3}>
        {!canEdit ? <Alert severity="info">You can view these settings. Ask an administrator to change them.</Alert> : null}
        {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}

        <Typography variant="h5" component="h2">Regional</Typography>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 6 }}>
            <Controller
              control={control}
              name="timeZoneId"
              render={({ field, fieldState }) => (
                <Autocomplete
                  options={zones}
                  value={field.value || null}
                  onChange={(_event, value) => field.onChange(value ?? '')}
                  onBlur={field.onBlur}
                  disabled={!canEdit}
                  disableClearable={false}
                  renderInput={(params) => (
                    <TextField
                      {...params}
                      label="Time zone"
                      inputRef={field.ref}
                      error={Boolean(fieldState.error)}
                      helperText={fieldState.error?.message ?? 'Dates and times are stored in UTC and shown in this zone.'}
                    />
                  )}
                />
              )}
            />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormSelect control={control} name="currencyCode" label="Currency" options={currencies} disabled={!canEdit} helperText="All amounts for your organization use this currency." />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormSelect control={control} name="locale" label="Number and date format" options={COMMON_LOCALES} disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormSelect control={control} name="fiscalYearStartMonth" label="Fiscal year starts in" options={MONTHS} disabled={!canEdit} />
          </Grid>
        </Grid>

        <Typography variant="h5" component="h2">Branding</Typography>
        <Grid container spacing={2} sx={{ alignItems: 'flex-start' }}>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="logoUrl" label="Logo address" placeholder="https://" disabled={!canEdit} helperText="Shown on public giving pages." />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <Stack direction="row" spacing={1.5} sx={{ alignItems: 'flex-start' }}>
              <FormTextField control={control} name="brandColor" label="Brand colour" placeholder="#1F5EFF" disabled={!canEdit} />
              <Box
                component="input"
                type="color"
                aria-label="Pick a brand colour"
                value={/^#[0-9A-Fa-f]{6}$/.test(brandColor) ? brandColor : '#1f5eff'}
                onChange={(event) => setValue('brandColor', event.target.value.toUpperCase(), { shouldDirty: true, shouldValidate: true })}
                disabled={!canEdit}
                sx={{ width: 44, height: 40, p: 0.25, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'transparent', cursor: canEdit ? 'pointer' : 'default' }}
              />
            </Stack>
          </Grid>
        </Grid>

        {canEdit ? (
          <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end' }}>
            <Button onClick={() => reset(toForm(organization))} disabled={!formState.isDirty || update.isPending} color="inherit">
              Discard changes
            </Button>
            <Button type="submit" variant="contained" disabled={!formState.isDirty || update.isPending}>
              {update.isPending ? 'Saving…' : 'Save settings'}
            </Button>
          </Stack>
        ) : null}
      </Stack>
    </form>
  )
}
