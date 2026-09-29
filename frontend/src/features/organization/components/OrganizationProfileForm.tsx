import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Grid from '@mui/material/Grid'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { FormTextField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { useUpdateOrganization } from '../hooks/useOrganization'
import { organizationProfileSchema, type OrganizationProfileForm } from '../schemas/organizationSchemas'
import type { Organization } from '../types/organization.types'

const FIELDS = ['name', 'legalName', 'taxId', 'website', 'contactEmail', 'phoneNumber', 'line1', 'line2', 'city', 'region', 'postalCode', 'country'] as const

const blankToNull = (value: string) => (value.trim() === '' ? null : value.trim())

function toForm(org: Organization): OrganizationProfileForm {
  return {
    name: org.name,
    legalName: org.legalName ?? '',
    taxId: org.taxId ?? '',
    website: org.website ?? '',
    contactEmail: org.contactEmail,
    phoneNumber: org.phoneNumber ?? '',
    line1: org.address.line1 ?? '',
    line2: org.address.line2 ?? '',
    city: org.address.city ?? '',
    region: org.address.region ?? '',
    postalCode: org.address.postalCode ?? '',
    country: org.address.country ?? '',
  }
}

export function OrganizationProfileFormPanel({ organization, canEdit }: { organization: Organization; canEdit: boolean }) {
  const update = useUpdateOrganization()
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit, reset, setError, formState } = useForm<OrganizationProfileForm>({
    resolver: zodResolver(organizationProfileSchema),
    defaultValues: toForm(organization),
  })

  // Follow the server copy after a save (and when another tab changed it).
  useEffect(() => reset(toForm(organization)), [organization, reset])

  const onSubmit = handleSubmit((values) => {
    setBanner(null)
    update.mutate(
      {
        name: values.name,
        legalName: blankToNull(values.legalName),
        taxId: blankToNull(values.taxId),
        website: blankToNull(values.website),
        contactEmail: values.contactEmail,
        phoneNumber: blankToNull(values.phoneNumber),
        address: {
          line1: blankToNull(values.line1),
          line2: blankToNull(values.line2),
          city: blankToNull(values.city),
          region: blankToNull(values.region),
          postalCode: blankToNull(values.postalCode),
          country: blankToNull(values.country),
        },
      },
      {
        onSuccess: () => toast.success('Organization profile saved.'),
        onError: (error) => {
          const unmatched = applyFieldErrors(error, FIELDS, setError)
          setBanner(unmatched[0] ?? (toApiError(error).isValidation ? null : toApiError(error).message))
        },
      },
    )
  })

  return (
    <form onSubmit={onSubmit} noValidate aria-label="Organization profile">
      <Stack spacing={3}>
        {!canEdit ? (
          <Alert severity="info">You can view the organization profile. Ask an administrator to change it.</Alert>
        ) : null}
        {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}

        <Typography variant="h5" component="h2">Identity</Typography>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="name" label="Organization name" required disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="legalName" label="Legal name" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="taxId" label="Tax ID / EIN" disabled={!canEdit} helperText="Printed on donation receipts." />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="website" label="Website" placeholder="https://" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="contactEmail" label="Contact email" type="email" required disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="phoneNumber" label="Phone" type="tel" disabled={!canEdit} />
          </Grid>
        </Grid>

        <Typography variant="h5" component="h2">Mailing address</Typography>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="line1" label="Address line 1" autoComplete="address-line1" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <FormTextField control={control} name="line2" label="Address line 2" autoComplete="address-line2" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
            <FormTextField control={control} name="city" label="City" autoComplete="address-level2" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
            <FormTextField control={control} name="region" label="State / region" autoComplete="address-level1" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
            <FormTextField control={control} name="postalCode" label="Postal code" autoComplete="postal-code" disabled={!canEdit} />
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
            <FormTextField control={control} name="country" label="Country" autoComplete="country-name" disabled={!canEdit} />
          </Grid>
        </Grid>

        {canEdit ? (
          <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end' }}>
            <Button onClick={() => reset(toForm(organization))} disabled={!formState.isDirty || update.isPending} color="inherit">
              Discard changes
            </Button>
            <Button type="submit" variant="contained" disabled={!formState.isDirty || update.isPending}>
              {update.isPending ? 'Saving…' : 'Save profile'}
            </Button>
          </Stack>
        ) : null}
      </Stack>
    </form>
  )
}
