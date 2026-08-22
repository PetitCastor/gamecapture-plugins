namespace SignaturePlugin;

/// <summary>The table entry and cluster count selected for an observed mining signature.</summary>
public readonly record struct SignatureMatch(
    string Name, string Kind, double TableSignature, int Count, double Delta);
