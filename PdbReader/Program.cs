using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

var pdbPath = args.Length > 0 ? args[0] : "/tmp/PaymentProcessor.Worker.pdb";

using var fs = File.OpenRead(pdbPath);
using var provider = MetadataReaderProvider.FromPortablePdbStream(fs);
var reader = provider.GetMetadataReader();

// === Documents ===
Console.WriteLine("=== Source files embedded in PDB ===");
foreach (var docHandle in reader.Documents)
{
    var doc = reader.GetDocument(docHandle);
    var name = reader.GetString(doc.Name);
    if (!string.IsNullOrEmpty(name))
        Console.WriteLine(name);
}

// === Method Debug Info (sequence points = IL offset → source line mapping) ===
Console.WriteLine("\n=== Method Debug Info (Sequence Points) ===");
foreach (var methodHandle in reader.MethodDebugInformation)
{
    var method = reader.GetMethodDebugInformation(methodHandle);
    if (method.Document.IsNil) continue;

    var doc = reader.GetDocument(method.Document);
    var docName = reader.GetString(doc.Name);
    var shortDoc = Path.GetFileName(docName);

    var points = method.GetSequencePoints().ToList();
    if (points.Count == 0) continue;

    int rowNumber = MetadataTokens.GetRowNumber(methodHandle);
    var methodToken = MetadataTokens.MethodDefinitionHandle(rowNumber);
    Console.WriteLine($"\n  Method token: 0x{MetadataTokens.GetToken(methodToken):X8} [{shortDoc}]");

    foreach (var sp in points)
    {
        if (sp.IsHidden)
            Console.WriteLine($"    IL_0x{sp.Offset:X4}  <hidden>");
        else
            Console.WriteLine($"    IL_0x{sp.Offset:X4}  {shortDoc}:{sp.StartLine}:{sp.StartColumn} → {sp.EndLine}:{sp.EndColumn}");
    }
}

// === Local Variable Scopes ===
Console.WriteLine("\n=== Local Variable Scopes ===");
foreach (var scopeHandle in reader.LocalScopes)
{
    var scope = reader.GetLocalScope(scopeHandle);
    var locals = scope.GetLocalVariables().ToList();
    if (locals.Count == 0) continue;

    int rowNumber = MetadataTokens.GetRowNumber(scopeHandle);
    Console.WriteLine($"\n  Scope (IL 0x{scope.StartOffset:X4}–0x{scope.EndOffset:X4}) [MethodDef row {rowNumber}]");
    foreach (var localHandle in locals)
    {
        var local = reader.GetLocalVariable(localHandle);
        var name = reader.GetString(local.Name);
        Console.WriteLine($"    [{local.Index}] {name}");
    }
}

// === Import Scopes (using directives per method) ===
Console.WriteLine("\n=== Import Scopes (namespaces) ===");
var seenImports = new HashSet<string>();
foreach (var scopeHandle in reader.ImportScopes)
{
    var scope = reader.GetImportScope(scopeHandle);
    foreach (var import in scope.GetImports())
    {
        string ns = import.Kind switch
        {
            ImportDefinitionKind.ImportNamespace or
            ImportDefinitionKind.ImportAssemblyNamespace =>
                Encoding.UTF8.GetString(reader.GetBlobBytes(import.TargetNamespace)),
            _ => ""
        };
        string alias = import.Alias.IsNil ? "" : Encoding.UTF8.GetString(reader.GetBlobBytes(import.Alias));

        string entry = import.Kind switch
        {
            ImportDefinitionKind.ImportNamespace => $"using {ns}",
            ImportDefinitionKind.ImportAssemblyNamespace => $"using {ns} (from assembly)",
            ImportDefinitionKind.AliasNamespace => $"using {alias} = {ns}",
            _ => import.Kind.ToString()
        };

        if (seenImports.Add(entry))
            Console.WriteLine($"  {entry}");
    }
}

// === Custom Debug Info (state machine / async methods) ===
Console.WriteLine("\n=== Custom Debug Information ===");
foreach (var cdiHandle in reader.CustomDebugInformation)
{
    var cdi = reader.GetCustomDebugInformation(cdiHandle);
    var guid = reader.GetGuid(cdi.Kind);
    Console.WriteLine($"  Kind: {guid}  Parent: {cdi.Parent.Kind} #{MetadataTokens.GetRowNumber(cdi.Parent)}");
}
