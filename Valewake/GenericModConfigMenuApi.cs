using System;
using StardewModdingAPI;

namespace Valewake;

/// <summary>
/// Minimal subset of the Generic Mod Config Menu (GMCM) API surface that Valewake
/// uses. Signatures mirror GMCM's IGenericModConfigMenuApi exactly (SMAPI's Pintail
/// binder matches by exact parameter list). GMCM has no AddDropdownOption: dropdowns
/// are expressed as AddTextOption with an allowedValues array.
/// </summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

    void AddTextOption(
        IManifest mod,
        Func<string> getValue,
        Action<string> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string[]? allowedValues = null,
        Func<string, string>? formatAllowedValue = null,
        string? fieldId = null
    );

    void AddBoolOption(
        IManifest mod,
        Func<bool> getValue,
        Action<bool> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        string? fieldId = null
    );
}
