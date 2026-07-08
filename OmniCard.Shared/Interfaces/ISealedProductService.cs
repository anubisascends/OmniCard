using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ISealedProductService
{
    List<SealedProductTemplate> GetTemplates();
    SealedProductTemplate? FindTemplateByUpc(string upc);
    SealedProductTemplate CreateTemplate(SealedProductTemplate template);
    SealedProductTemplate CreateTemplateFromArchetype(SealedProductType type, string? setCode, string? setName, string? upc);
    void UpdateTemplate(SealedProductTemplate template);
    void DeleteTemplate(int templateId);
    List<SealedProductInstance> GetInstances();
    SealedProductInstance AddInstance(int templateId, decimal? purchasePrice);
    void UpdateInstancePrice(int instanceId, decimal? purchasePrice);
    void DeleteInstance(int instanceId);
    SealedProductInstance? GetInstanceWithContents(int instanceId);
    List<SealedProductInstance> CrackInstance(int instanceId);
    List<SealedProductInstance> CrackInstanceWithTemplates(int instanceId, Dictionary<int, int> contentTemplateOverrides);
}
