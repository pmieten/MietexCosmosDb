namespace Mietex.CosmosDb;

/// <summary>
/// Minimum contract the library needs from a persisted document. Host applications keep ownership of
/// their entity base classes and only add this interface.
/// </summary>
public interface ICosmosEntity
{
    /// <summary>Gets or sets the Cosmos DB document id.</summary>
    string Id { get; set; }

    /// <summary>Gets or sets the partition key value stored on the document.</summary>
    string PartitionKey { get; set; }

    /// <summary>Gets or sets the discriminator used to separate entity types in a shared container.</summary>
    string Discriminator { get; set; }

    /// <summary>Gets or sets the Cosmos DB <c>_etag</c> value.</summary>
    string? Etag { get; set; }

    /// <summary>Returns <see langword="true"/> when the document has no ETag yet.</summary>
    /// <returns><see langword="true"/> for a new document; otherwise <see langword="false"/>.</returns>
    bool IsNew();
}

/// <summary>Audit fields written when a document is created.</summary>
/// <typeparam name="TUser">Host-specific user identity type.</typeparam>
public interface ICreatedAuditEntity<TUser>
    where TUser : class
{
    /// <summary>Gets or sets the creation timestamp.</summary>
    DateTimeOffset CreatedDate { get; set; }

    /// <summary>Gets or sets the creating user.</summary>
    TUser? CreatedById { get; set; }
}

/// <summary>Audit fields written when a document is updated.</summary>
/// <typeparam name="TUser">Host-specific user identity type.</typeparam>
public interface IUpdatedAuditEntity<TUser>
    where TUser : class
{
    /// <summary>Gets or sets the last update timestamp.</summary>
    DateTimeOffset UpdatedDate { get; set; }

    /// <summary>Gets or sets the user that performed the last update.</summary>
    TUser? UpdatedById { get; set; }
}

/// <summary>Audit fields written when a document is soft-deleted.</summary>
/// <typeparam name="TUser">Host-specific user identity type.</typeparam>
public interface IDeletedAuditEntity<TUser>
    where TUser : class
{
    /// <summary>Gets or sets the deletion timestamp.</summary>
    DateTimeOffset? DeletedDate { get; set; }

    /// <summary>Gets or sets the user that deleted the document.</summary>
    TUser? DeletedById { get; set; }
}

/// <summary>Combined created and updated audit contract.</summary>
/// <typeparam name="TUser">Host-specific user identity type.</typeparam>
public interface IAuditEntity<TUser> : ICreatedAuditEntity<TUser>, IUpdatedAuditEntity<TUser>
    where TUser : class
{
}
