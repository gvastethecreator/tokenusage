namespace TokenUsage.Providers.Catalog;

public enum ManualCredentialKind
{
    None = 0,
    ApiKey,
    ApiKeyAndOptionalKeyId,
    ApiKeyAndOptionalOrganization,
    ApiKeyAndOrganization,
    ApiKeyAndEndpoint,
}

public static class ManualCredentialKindExtensions
{
    public static bool HasSecondaryField(this ManualCredentialKind kind) => kind is
        ManualCredentialKind.ApiKeyAndOptionalKeyId
        or ManualCredentialKind.ApiKeyAndOptionalOrganization
        or ManualCredentialKind.ApiKeyAndOrganization
        or ManualCredentialKind.ApiKeyAndEndpoint;

    public static bool RequiresSecondaryField(this ManualCredentialKind kind) => kind is
        ManualCredentialKind.ApiKeyAndOrganization
        or ManualCredentialKind.ApiKeyAndEndpoint;
}
