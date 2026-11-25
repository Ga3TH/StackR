namespace StackR.Core.Manifests;

public enum ComponentKind { Apache, Php, MySql, Mailpit, Node, Composer }

public enum PackageType { Zip, File }

public sealed record ComponentRelease(
    ComponentKind Component,
    string Version,
    string Channel,
    Uri Url,
    string Sha256,
    long SizeBytes,
    PackageType Package,
    string[] Requires,
    string? ArchiveRoot);
