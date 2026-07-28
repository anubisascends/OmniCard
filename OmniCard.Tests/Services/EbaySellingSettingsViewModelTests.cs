using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Settings;

namespace OmniCard.Tests.Services;

public class EbaySellingSettingsViewModelTests
{
    private sealed class MemSettings : IEbaySellingSettingsService
    {
        public EbaySellingSettings Current = new();
        public EbaySellingSettings Get() => Current;
        public void Save(EbaySellingSettings s) => Current = s;
        public bool IsSetupComplete() => Current.LocationProvisioned && !string.IsNullOrEmpty(Current.FulfillmentPolicyId) && !string.IsNullOrEmpty(Current.ReturnPolicyId);
    }
    private sealed class FakeSetup : IEbaySellerSetupService
    {
        public EbaySetupResult Result = new();
        public Task<EbaySetupResult> RunSetupAsync(IProgress<string>? p = null) => Task.FromResult(Result);
    }

    [Fact]
    public void Load_PopulatesFieldsFromSettings()
    {
        var settings = new MemSettings();
        settings.Current.City = "Portland";
        var vm = new EbaySellingSettingsViewModel(settings, new FakeSetup());
        vm.Load();
        Assert.Equal("Portland", vm.Settings.City);
    }

    [Fact]
    public async Task RunSetup_AppendsStepStatusesToStatusLog()
    {
        var setup = new FakeSetup();
        setup.Result.Steps.Add(new EbaySetupStep("Inventory location", EbaySetupStepStatus.Ok, null));
        setup.Result.Steps.Add(new EbaySetupStep("Payment policy", EbaySetupStepStatus.Failed, "not eligible"));
        setup.Result.Success = true;

        var vm = new EbaySellingSettingsViewModel(new MemSettings(), setup);
        vm.Load();
        await vm.RunSetupCommand.ExecuteAsync(null);

        Assert.Contains(vm.StatusLog, l => l.Contains("Inventory location") && l.Contains("OK"));
        Assert.Contains(vm.StatusLog, l => l.Contains("Payment policy") && l.Contains("not eligible"));
    }
}
