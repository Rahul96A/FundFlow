namespace FundFlow.Domain.Identity;

public sealed record PermissionDefinition(string Name, string Module, string Description);

/// <summary>
/// The platform's permission catalogue. Permission names are the stable contract used by
/// <c>[HasPermission]</c> on API endpoints, by role definitions and by the frontend for UI gating.
/// Add new permissions here (and to <see cref="All"/>); reference data is synchronised on deployment.
/// </summary>
public static class Permissions
{
    public static class Donor
    {
        public const string Read = "Donor.Read";
        public const string Create = "Donor.Create";
        public const string Update = "Donor.Update";
        public const string Delete = "Donor.Delete";
    }

    public static class Campaign
    {
        public const string Read = "Campaign.Read";
        public const string Create = "Campaign.Create";
        public const string Update = "Campaign.Update";
        public const string Publish = "Campaign.Publish";
    }

    public static class Donation
    {
        public const string Read = "Donation.Read";
        public const string Create = "Donation.Create";
        public const string Refund = "Donation.Refund";
    }

    public static class Event
    {
        public const string Read = "Event.Read";
        public const string Create = "Event.Create";
        public const string Update = "Event.Update";
        public const string Publish = "Event.Publish";
    }

    public static class Auction
    {
        public const string Read = "Auction.Read";
        public const string Create = "Auction.Create";
        public const string Manage = "Auction.Manage";
        public const string Close = "Auction.Close";
    }

    public static class Sponsor
    {
        public const string Read = "Sponsor.Read";
        public const string Manage = "Sponsor.Manage";
    }

    public static class Volunteer
    {
        public const string Read = "Volunteer.Read";
        public const string Manage = "Volunteer.Manage";
    }

    public static class Communication
    {
        public const string Read = "Communication.Read";
        public const string Manage = "Communication.Manage";
        public const string Send = "Communication.Send";
    }

    public static class Report
    {
        public const string Read = "Report.Read";
        public const string Export = "Report.Export";
    }

    public static class Payment
    {
        public const string Manage = "Payment.Manage";
    }

    public static class Organization
    {
        public const string Update = "Organization.Update";
    }

    public static class User
    {
        public const string Read = "User.Read";
        public const string Manage = "User.Manage";
    }

    public static class Role
    {
        public const string Read = "Role.Read";
        public const string Manage = "Role.Manage";
    }

    public static class Audit
    {
        public const string Read = "Audit.Read";
    }

    /// <summary>Platform-operator permissions. Never granted to tenant roles.</summary>
    public static class Platform
    {
        public const string Manage = "Platform.Manage";
    }

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Donor.Read, "Donors", "View donors and their giving history"),
        new(Donor.Create, "Donors", "Create donors"),
        new(Donor.Update, "Donors", "Edit donor profiles, notes and relationships"),
        new(Donor.Delete, "Donors", "Delete or anonymise donors"),

        new(Campaign.Read, "Campaigns", "View campaigns and their metrics"),
        new(Campaign.Create, "Campaigns", "Create campaigns"),
        new(Campaign.Update, "Campaigns", "Edit campaigns, goals and pages"),
        new(Campaign.Publish, "Campaigns", "Publish, pause and complete campaigns"),

        new(Donation.Read, "Donations", "View donations and transactions"),
        new(Donation.Create, "Donations", "Record offline donations"),
        new(Donation.Refund, "Donations", "Refund or partially refund donations"),

        new(Event.Read, "Events", "View events, registrations and check-ins"),
        new(Event.Create, "Events", "Create events"),
        new(Event.Update, "Events", "Edit events, tickets and seating"),
        new(Event.Publish, "Events", "Publish, cancel and complete events"),

        new(Auction.Read, "Auctions", "View auctions, items and bids"),
        new(Auction.Create, "Auctions", "Create auctions and items"),
        new(Auction.Manage, "Auctions", "Manage auction items, bidders and settings"),
        new(Auction.Close, "Auctions", "Close auctions and settle winners"),

        new(Sponsor.Read, "Sponsors", "View sponsors and packages"),
        new(Sponsor.Manage, "Sponsors", "Manage sponsors, packages and benefits"),

        new(Volunteer.Read, "Volunteers", "View volunteers and shifts"),
        new(Volunteer.Manage, "Volunteers", "Manage volunteers, shifts and hours"),

        new(Communication.Read, "Communications", "View email campaigns and templates"),
        new(Communication.Manage, "Communications", "Create and edit email campaigns and templates"),
        new(Communication.Send, "Communications", "Send email campaigns"),

        new(Report.Read, "Reports", "View reports and analytics"),
        new(Report.Export, "Reports", "Export reports to CSV, Excel and PDF"),

        new(Payment.Manage, "Payments", "Configure payment providers and view payment settings"),

        new(Organization.Update, "Organization", "Edit organization profile and settings"),

        new(User.Read, "Users & Roles", "View users"),
        new(User.Manage, "Users & Roles", "Invite, edit, deactivate users and assign roles"),
        new(Role.Read, "Users & Roles", "View roles and permissions"),
        new(Role.Manage, "Users & Roles", "Create and edit custom roles"),

        new(Audit.Read, "Audit", "View the audit log"),

        new(Platform.Manage, "Platform", "Manage tenant organizations across the platform"),
    ];

    /// <summary>Every permission a tenant role may hold (everything except platform-operator permissions).</summary>
    public static IReadOnlyList<PermissionDefinition> TenantScoped { get; } =
        All.Where(p => p.Module != "Platform").ToArray();

    public static bool IsKnown(string name) => All.Any(p => p.Name == name);
}
