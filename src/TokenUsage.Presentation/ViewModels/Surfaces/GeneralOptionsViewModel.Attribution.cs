using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class GeneralOptionsViewModel
{
    private async Task<bool> LoadCapabilityAsync(AttributionCapability capability)
    {
        AttributionConsent consent = await _attributionConsent!.LoadAsync(capability).ConfigureAwait(true);
        if (consent.State == AttributionConsentState.PurgePending
            && !string.IsNullOrWhiteSpace(_usageDatabasePath))
        {
            await PurgeCapabilityAsync(capability).ConfigureAwait(true);
            consent = await _attributionConsent.LoadAsync(capability).ConfigureAwait(true);
        }

        return consent.State == AttributionConsentState.Enabled;
    }

    private async Task ApplyCapabilityAsync(AttributionCapability capability, bool enabled)
    {
        try
        {
            if (enabled)
            {
                AttributionConsent current = await _attributionConsent!.LoadAsync(capability)
                    .ConfigureAwait(false);
                if (current.State == AttributionConsentState.PurgePending)
                {
                    await PurgeCapabilityAsync(capability).ConfigureAwait(false);
                }

                await _attributionConsent.EnableAsync(capability).ConfigureAwait(false);
            }
            else
            {
                await _attributionConsent!.RevokeAsync(capability).ConfigureAwait(false);
                await PurgeCapabilityAsync(capability).ConfigureAwait(false);
            }

            RaiseCapabilityChanged(capability);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or TimeoutException
                                               or InvalidOperationException)
        {
            _isInitializing = true;
            try
            {
                AttributionConsent consent = await _attributionConsent!.LoadAsync(capability)
                    .ConfigureAwait(false);
                AssignCapability(capability, consent.State == AttributionConsentState.Enabled);
            }
            finally
            {
                _isInitializing = false;
            }
        }
    }

    private async Task PurgeCapabilityAsync(AttributionCapability capability)
    {
        if (!string.IsNullOrWhiteSpace(_usageDatabasePath) && File.Exists(_usageDatabasePath))
        {
            UsageRepository repository = await UsageRepository.OpenAsync(_usageDatabasePath).ConfigureAwait(false);
            if (capability.Value == AttributionCapability.CodexProject.Value)
            {
                await repository.PurgeProjectLinksAsync().ConfigureAwait(false);
                if (_attributionAliases is not null)
                {
                    await _attributionAliases.ClearAsync().ConfigureAwait(false);
                }
            }
            else if (capability.Value is "codex-mcp" or "codex-skills" or "codex-commands" or "codex-files")
            {
                await repository.PurgeOperationFactsAsync(capability).ConfigureAwait(false);
            }
            else
            {
                await repository.PurgeSessionLinksAsync(capability).ConfigureAwait(false);
            }
        }

        if (_clearAttributionDerivedStores is not null)
        {
            await _clearAttributionDerivedStores(capability, CancellationToken.None).ConfigureAwait(false);
        }

        await _attributionConsent!.CompletePurgeAsync(capability).ConfigureAwait(false);
    }

    private void AssignCapability(AttributionCapability capability, bool enabled)
    {
        if (capability.Value == AttributionCapability.CodexSession.Value)
        {
            IsCodexSessionAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CodexProject.Value)
        {
            IsCodexProjectAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CursorSession.Value)
        {
            IsCursorSessionAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CodexMcp.Value)
        {
            IsCodexMcpAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CodexSkills.Value)
        {
            IsCodexSkillsAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CodexCommands.Value)
        {
            IsCodexCommandsAttributionEnabled = enabled;
        }
        else if (capability.Value == AttributionCapability.CodexFiles.Value)
        {
            IsCodexFilesAttributionEnabled = enabled;
        }
    }

    private void RaiseCapabilityChanged(AttributionCapability capability)
    {
        if (capability.Value == AttributionCapability.CodexSession.Value)
        {
            CodexSessionAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CodexProject.Value)
        {
            CodexProjectAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CursorSession.Value)
        {
            CursorSessionAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CodexMcp.Value)
        {
            CodexMcpAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CodexSkills.Value)
        {
            CodexSkillsAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CodexCommands.Value)
        {
            CodexCommandsAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (capability.Value == AttributionCapability.CodexFiles.Value)
        {
            CodexFilesAttributionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
