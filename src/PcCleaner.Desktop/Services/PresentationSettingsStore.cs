using CoreSettingsStore = PcCleaner.Core.Settings.ISettingsStore;
using PresentationSettingsStore_ = PcCleaner.Presentation.Services.ISettingsStore;

namespace PcCleaner.Desktop.Services;

/// <summary>
/// Lets the Presentation layer's settings consumers (developer mode, scan volumes, cloud drives, dev roots) share the
/// same JSON settings file as the Linux theme service. Core's <see cref="CoreSettingsStore"/> never throws and reports
/// write failures with a bool; Presentation's string get/set contract just wants the value or null.
/// </summary>
public sealed class PresentationSettingsStore(CoreSettingsStore inner) : PresentationSettingsStore_
{
    public string? Get(string key) => inner.GetString(key);

    public void Set(string key, string value) => inner.SetString(key, value);
}
