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
    public static bool RequiresSecondaryField(this ManualCredentialKind kind) => kind is
        ManualCredentialKind.ApiKeyAndOrganization
        or ManualCredentialKind.ApiKeyAndEndpoint;
}
