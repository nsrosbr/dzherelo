namespace DticLoader;

public sealed class SubjectField
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string FolderName => $"{Number}. {Name}";
    public override string ToString() => FolderName;
}

public sealed class SubjectGroup
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string FieldNumber { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FullCode => $"{FieldNumber}/{Code}";
    public string DottedCode => Code.Replace('/', '.');
    public string FolderName => $"{FieldNumber}.{DottedCode} {Name}";
    public override string ToString() => FolderName;
}

public sealed class CatalogCache
{
    public DateTimeOffset Updated { get; set; }
    public List<SubjectField> Fields { get; set; } = new();
    public List<SubjectGroup> Groups { get; set; } = new();
}