using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// A spawned engine owns a named pipe and a Windows OCR instance, so two replay tests must never
/// run at once. Disable parallelization for this collection so it also stays isolated from other
/// test collections in the assembly.
/// </summary>
[CollectionDefinition("ReplayParity", DisableParallelization = true)]
public class ReplayParityCollection;
