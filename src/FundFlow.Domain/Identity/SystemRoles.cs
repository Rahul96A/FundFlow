namespace FundFlow.Domain.Identity;

public static class SystemRoles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string OrganizationAdmin = "ORGANIZATION_ADMIN";
    public const string FundraisingManager = "FUNDRAISING_MANAGER";
    public const string EventManager = "EVENT_MANAGER";
    public const string DonorManager = "DONOR_MANAGER";
    public const string FinanceManager = "FINANCE_MANAGER";
    public const string MarketingManager = "MARKETING_MANAGER";
    public const string VolunteerManager = "VOLUNTEER_MANAGER";
    public const string ReportViewer = "REPORT_VIEWER";
    public const string Staff = "STAFF";
    public const string Volunteer = "VOLUNTEER";

    /// <summary>Roles whose holders may grant permissions they do not personally hold.</summary>
    public static bool IsAdministrator(string roleName) =>
        roleName is SuperAdmin or OrganizationAdmin;
}

public sealed record RoleTemplate(string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>
/// Blueprints for the system roles. Each organization receives its own copy of the tenant roles when it is created,
/// so a role row always belongs to exactly one tenant (or to the platform for SUPER_ADMIN).
/// System roles are read-only in the UI; organizations that need something different create a custom role.
/// </summary>
public static class RoleTemplates
{
    public static RoleTemplate SuperAdmin { get; } = new(
        SystemRoles.SuperAdmin,
        "Platform operator. Manages tenant organizations; has no access to any tenant's business data.",
        [Permissions.Platform.Manage, Permissions.Audit.Read]);

    public static IReadOnlyList<RoleTemplate> Tenant { get; } =
    [
        new(SystemRoles.OrganizationAdmin,
            "Full control over the organization, including users, roles and settings.",
            Permissions.TenantScoped.Select(p => p.Name).ToArray()),

        new(SystemRoles.FundraisingManager,
            "Runs campaigns and donations; works with donors and reports.",
            [
                Permissions.Campaign.Read, Permissions.Campaign.Create, Permissions.Campaign.Update, Permissions.Campaign.Publish,
                Permissions.Donation.Read, Permissions.Donation.Create,
                Permissions.Donor.Read, Permissions.Donor.Create, Permissions.Donor.Update,
                Permissions.Event.Read, Permissions.Auction.Read, Permissions.Sponsor.Read,
                Permissions.Communication.Read, Permissions.Report.Read, Permissions.Report.Export,
            ]),

        new(SystemRoles.EventManager,
            "Plans events, ticketing, seating and auctions.",
            [
                Permissions.Event.Read, Permissions.Event.Create, Permissions.Event.Update, Permissions.Event.Publish,
                Permissions.Auction.Read, Permissions.Auction.Create, Permissions.Auction.Manage, Permissions.Auction.Close,
                Permissions.Sponsor.Read, Permissions.Sponsor.Manage,
                Permissions.Volunteer.Read, Permissions.Donor.Read, Permissions.Report.Read,
            ]),

        new(SystemRoles.DonorManager,
            "Maintains the donor CRM: profiles, segments and relationships.",
            [
                Permissions.Donor.Read, Permissions.Donor.Create, Permissions.Donor.Update, Permissions.Donor.Delete,
                Permissions.Donation.Read, Permissions.Campaign.Read, Permissions.Communication.Read, Permissions.Report.Read,
            ]),

        new(SystemRoles.FinanceManager,
            "Oversees payments, refunds, receipts and financial reporting.",
            [
                Permissions.Donation.Read, Permissions.Donation.Create, Permissions.Donation.Refund,
                Permissions.Payment.Manage, Permissions.Report.Read, Permissions.Report.Export,
                Permissions.Donor.Read, Permissions.Campaign.Read, Permissions.Audit.Read,
            ]),

        new(SystemRoles.MarketingManager,
            "Owns email communications and campaign messaging.",
            [
                Permissions.Communication.Read, Permissions.Communication.Manage, Permissions.Communication.Send,
                Permissions.Campaign.Read, Permissions.Campaign.Update,
                Permissions.Donor.Read, Permissions.Report.Read,
            ]),

        new(SystemRoles.VolunteerManager,
            "Recruits and schedules volunteers.",
            [
                Permissions.Volunteer.Read, Permissions.Volunteer.Manage,
                Permissions.Event.Read, Permissions.Donor.Read,
            ]),

        new(SystemRoles.ReportViewer,
            "Read-only access to reports and the data behind them.",
            [
                Permissions.Report.Read, Permissions.Campaign.Read, Permissions.Donation.Read, Permissions.Donor.Read,
                Permissions.Event.Read, Permissions.Auction.Read, Permissions.Sponsor.Read, Permissions.Volunteer.Read,
            ]),

        new(SystemRoles.Staff,
            "General staff: read access to fundraising records.",
            [
                Permissions.Donor.Read, Permissions.Campaign.Read, Permissions.Event.Read,
                Permissions.Auction.Read, Permissions.Sponsor.Read, Permissions.Volunteer.Read,
            ]),

        new(SystemRoles.Volunteer,
            "Volunteer access: sees events and their own volunteer shifts.",
            [Permissions.Event.Read, Permissions.Volunteer.Read]),
    ];
}
