/**
 * Permission names, mirrored from the API's catalogue (FundFlow.Domain.Identity.Permissions).
 * The UI uses them to decide what to show; the API enforces them regardless.
 */
export const Permissions = {
  Donor: { Read: 'Donor.Read', Create: 'Donor.Create', Update: 'Donor.Update', Delete: 'Donor.Delete' },
  Campaign: { Read: 'Campaign.Read', Create: 'Campaign.Create', Update: 'Campaign.Update', Publish: 'Campaign.Publish' },
  Donation: { Read: 'Donation.Read', Create: 'Donation.Create', Refund: 'Donation.Refund' },
  Event: { Read: 'Event.Read', Create: 'Event.Create', Update: 'Event.Update', Publish: 'Event.Publish' },
  Auction: { Read: 'Auction.Read', Create: 'Auction.Create', Manage: 'Auction.Manage', Close: 'Auction.Close' },
  Sponsor: { Read: 'Sponsor.Read', Manage: 'Sponsor.Manage' },
  Volunteer: { Read: 'Volunteer.Read', Manage: 'Volunteer.Manage' },
  Communication: { Read: 'Communication.Read', Manage: 'Communication.Manage', Send: 'Communication.Send' },
  Report: { Read: 'Report.Read', Export: 'Report.Export' },
  Payment: { Manage: 'Payment.Manage' },
  Organization: { Update: 'Organization.Update' },
  User: { Read: 'User.Read', Manage: 'User.Manage' },
  Role: { Read: 'Role.Read', Manage: 'Role.Manage' },
  Audit: { Read: 'Audit.Read' },
  Platform: { Manage: 'Platform.Manage' },
} as const
