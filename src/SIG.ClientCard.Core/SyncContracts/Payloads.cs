using System.Text.Json.Serialization;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;

namespace SIG.ClientCard.Core.SyncContracts;

// Wire-format row snapshots. Property names go through the snake_case policy in
// SyncJson.Options, so they match Postgres columns exactly.

public sealed class ClientPayload
{
    public Guid Id { get; set; }
    public Guid SalonId { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PhoneRaw { get; set; } = string.Empty;
    public string? Email { get; set; }
    public AcquisitionSource AcquisitionSource { get; set; }
    public string? AcquisitionDetail { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool MedicalFlag { get; set; }
    public string? GpDetails { get; set; }
    public DateOnly? PatchTestOn { get; set; }
    public PatchTestResult PatchTestResult { get; set; }
    public string? CardPhotoPath { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByDevice { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonInclude]
    public long SyncSeq { get; set; }

    public static ClientPayload From(Client c) => new()
    {
        Id = c.Id,
        SalonId = c.SalonId,
        LastName = c.LastName,
        FirstName = c.FirstName,
        Address = c.Address,
        Phone = c.Phone,
        PhoneRaw = c.PhoneRaw,
        Email = c.Email,
        AcquisitionSource = c.AcquisitionSource,
        AcquisitionDetail = c.AcquisitionDetail,
        DateOfBirth = c.DateOfBirth,
        MedicalFlag = c.MedicalFlag,
        GpDetails = c.GpDetails,
        PatchTestOn = c.PatchTestOn,
        PatchTestResult = c.PatchTestResult,
        CardPhotoPath = c.CardPhotoPath,
        UpdatedAt = c.UpdatedAt,
        UpdatedByDevice = c.UpdatedByDevice,
        DeletedAt = c.DeletedAt,
        SyncSeq = c.SyncSeq,
    };

    public Client ToEntity() => new()
    {
        Id = Id,
        SalonId = SalonId,
        LastName = LastName,
        FirstName = FirstName,
        Address = Address,
        Phone = Phone,
        PhoneRaw = PhoneRaw,
        Email = Email,
        AcquisitionSource = AcquisitionSource,
        AcquisitionDetail = AcquisitionDetail,
        DateOfBirth = DateOfBirth,
        MedicalFlag = MedicalFlag,
        GpDetails = GpDetails,
        PatchTestOn = PatchTestOn,
        PatchTestResult = PatchTestResult,
        CardPhotoPath = CardPhotoPath,
        UpdatedAt = UpdatedAt,
        UpdatedByDevice = UpdatedByDevice,
        DeletedAt = DeletedAt,
        SyncSeq = SyncSeq,
    };
}

public sealed class ClientNotePayload
{
    public Guid Id { get; set; }
    public Guid SalonId { get; set; }
    public Guid ClientId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByDevice { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long SyncSeq { get; set; }

    public static ClientNotePayload From(ClientNote n) => new()
    {
        Id = n.Id,
        SalonId = n.SalonId,
        ClientId = n.ClientId,
        CreatedAt = n.CreatedAt,
        AuthorUserId = n.AuthorUserId,
        Body = n.Body,
        UpdatedAt = n.UpdatedAt,
        UpdatedByDevice = n.UpdatedByDevice,
        DeletedAt = n.DeletedAt,
        SyncSeq = n.SyncSeq,
    };

    public ClientNote ToEntity() => new()
    {
        Id = Id,
        SalonId = SalonId,
        ClientId = ClientId,
        CreatedAt = CreatedAt,
        AuthorUserId = AuthorUserId,
        Body = Body,
        UpdatedAt = UpdatedAt,
        UpdatedByDevice = UpdatedByDevice,
        DeletedAt = DeletedAt,
        SyncSeq = SyncSeq,
    };
}

public sealed class ServiceRecordPayload
{
    public Guid Id { get; set; }
    public Guid SalonId { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly PerformedOn { get; set; }
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = "GBP";
    public Guid? ServiceCatalogId { get; set; }
    public string? PhotoPath { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByDevice { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long SyncSeq { get; set; }

    public static ServiceRecordPayload From(ServiceRecord s) => new()
    {
        Id = s.Id,
        SalonId = s.SalonId,
        ClientId = s.ClientId,
        PerformedOn = s.PerformedOn,
        ServiceDescription = s.ServiceDescription,
        Price = s.Price,
        CurrencyCode = s.CurrencyCode,
        ServiceCatalogId = s.ServiceCatalogId,
        PhotoPath = s.PhotoPath,
        UpdatedAt = s.UpdatedAt,
        UpdatedByDevice = s.UpdatedByDevice,
        DeletedAt = s.DeletedAt,
        SyncSeq = s.SyncSeq,
    };

    public ServiceRecord ToEntity() => new()
    {
        Id = Id,
        SalonId = SalonId,
        ClientId = ClientId,
        PerformedOn = PerformedOn,
        ServiceDescription = ServiceDescription,
        Price = Price,
        CurrencyCode = CurrencyCode,
        ServiceCatalogId = ServiceCatalogId,
        PhotoPath = PhotoPath,
        UpdatedAt = UpdatedAt,
        UpdatedByDevice = UpdatedByDevice,
        DeletedAt = DeletedAt,
        SyncSeq = SyncSeq,
    };
}

public sealed class ClientConsentPayload
{
    public Guid Id { get; set; }
    public Guid SalonId { get; set; }
    public Guid ClientId { get; set; }
    public ConsentPurpose Purpose { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public Guid? CapturedByUserId { get; set; }
    public string? SignatureBlobRef { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByDevice { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long SyncSeq { get; set; }

    public static ClientConsentPayload From(ClientConsent c) => new()
    {
        Id = c.Id,
        SalonId = c.SalonId,
        ClientId = c.ClientId,
        Purpose = c.Purpose,
        GrantedAt = c.GrantedAt,
        WithdrawnAt = c.WithdrawnAt,
        CapturedByUserId = c.CapturedByUserId,
        SignatureBlobRef = c.SignatureBlobRef,
        UpdatedAt = c.UpdatedAt,
        UpdatedByDevice = c.UpdatedByDevice,
        DeletedAt = c.DeletedAt,
        SyncSeq = c.SyncSeq,
    };

    public ClientConsent ToEntity() => new()
    {
        Id = Id,
        SalonId = SalonId,
        ClientId = ClientId,
        Purpose = Purpose,
        GrantedAt = GrantedAt,
        WithdrawnAt = WithdrawnAt,
        CapturedByUserId = CapturedByUserId,
        SignatureBlobRef = SignatureBlobRef,
        UpdatedAt = UpdatedAt,
        UpdatedByDevice = UpdatedByDevice,
        DeletedAt = DeletedAt,
        SyncSeq = SyncSeq,
    };
}

/// <summary>Result of a push: the server's current cursor and any rejected ops.</summary>
public sealed class SyncPushResult
{
    public long Cursor { get; set; }

    /// <summary>Op ids the server rejected as invalid (4xx-class). Never retried; parked in dead letter.</summary>
    public List<Guid> RejectedOpIds { get; set; } = [];
}

/// <summary>One page of changes pulled from the server.</summary>
public sealed class SyncPullBundle
{
    public List<ClientPayload> Clients { get; set; } = [];
    public List<ServiceRecordPayload> Services { get; set; } = [];
    public List<ClientNotePayload> Notes { get; set; } = [];
    public List<ClientConsentPayload> Consents { get; set; } = [];

    /// <summary>Redactions to apply locally: hard-delete these ids, keep no payload.</summary>
    public List<RedactionPayload> Redactions { get; set; } = [];
}

public sealed class RedactionPayload
{
    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTimeOffset RedactedAt { get; set; }
    public long SyncSeq { get; set; }
}
