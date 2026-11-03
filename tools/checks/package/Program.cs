using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

try
{
    if(args.Length!=1)throw new ArgumentException("Pass the exported package directory.");
    var directory=Path.GetFullPath(args[0]);
    var files=Directory.GetFiles(directory,"*",SearchOption.AllDirectories);
    if(files.Any(f=>Path.GetFileName(f).Contains("Checks",StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("Export contains a check assembly or artifact.");
    foreach(string assembly in new[]{"NoDwarf.dll","NoDwarf.Core.dll"})
    {
        var file=files.Single(f=>Path.GetFileName(f)==assembly);
        using var stream=File.OpenRead(file);using var pe=new PEReader(stream);
        var metadata=pe.GetMetadataReader();
        foreach(var handle in metadata.AssemblyReferences)
        {
            string name=metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if(name.Contains("Checks")||name=="GodotSharpEditor")throw new InvalidDataException($"{assembly} references development assembly {name}");
        }
        foreach(var handle in metadata.TypeDefinitions)
        {
            var type=metadata.GetTypeDefinition(handle);string name=metadata.GetString(type.Name);
            if(name is "RealtimeStressScenario" or "ColonyLongRun" or "TerrainMeshReference" or "TpsReview" or "FlatPawnDemo" or "DirectionalDwarfDemo" or "HairPaletteReview"||name.EndsWith("Checks"))
                throw new InvalidDataException($"Development type exported: {name}");
            foreach(var method in type.GetMethods())
            {
                var methodName=metadata.GetString(metadata.GetMethodDefinition(method).Name);
                if(methodName is "RunDevelopmentChecks" or "CheckReference" or "CheckCharacterEditor" or "BeginFrameDiagnostic" or "CaptureColonyPreview" or "RunStartupPreview")
                    throw new InvalidDataException($"Development method exported: {name}.{methodName}");
            }
        }
        Console.WriteLine("PACKAGE_ASSEMBLY_OK: "+assembly);
    }
    return 0;
}
catch(Exception e){Console.Error.WriteLine("PACKAGE_CHECK_FAILED: "+e.Message);return 1;}
