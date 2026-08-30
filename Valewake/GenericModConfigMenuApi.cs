using System;
using StardewModdingAPI;

namespace Valewake;

/// <summary>
/// Minimal subset of the Generic Mod Config Menu (GMCM) API surface that Valewake
/// uses. We declare the oldest stable signatures (no fieldId / titleScreenOnly) so
/// the signature-based binding matches both old and new GMCM versions.
/// </summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save);

    void AddTextOption(
        IManifest mod,
        Func<string> getValue,
        Action<string> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string[]? allowedValues = null,
        Func<string, string>? formatAllowedValue = null
    );

    void AddDropdownOption(
        IManifest mod,
        Func<int> getValue,
        Action<int> setValue,
        Func<string[]> values,
        Func<string> name,
        Func<string>? tooltip = null
    );

    void AddBoolOption(
        IManifest mod,
        Func<bool> getValue,
        Action<bool> setValue,
        Func<string> name,
        Func<string>? tooltip = null
    );

    void AddNumberOption(
        IManifest mod,
        Func<int> getValue,
        Action<int> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        int? min = null,
        int? max = null,
        int? interval = null,
        Func<int, string>? formatValue = null
    );
}
