using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class Template : Common.Entity
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public TemplateKind Kind { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
}
