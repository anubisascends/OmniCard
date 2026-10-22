namespace OmniCard.Shared.Kiosk;

/// <summary>Reads and writes <see cref="KioskSettings"/>.</summary>
public interface IKioskSettingsService
{
    KioskSettings Get();
    void Save(KioskSettings settings);
}
