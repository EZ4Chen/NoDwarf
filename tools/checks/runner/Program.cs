using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

try {return await Run(args);}catch(Exception e){Console.Error.WriteLine("CHECK_FAILED: "+e.Message);return 1;}

static async Task<int> Run(string[] args)
{
    var options=new Dictionary<string,string>();bool skipBuild=false,list=false;
    for(int i=0;i<args.Length;i++)
    {
        if(args[i]=="--skip-build"){skipBuild=true;continue;}
        if(args[i]=="--list"){list=true;continue;}
        if(args[i] is not ("--root" or "--godot" or "--game" or "--group" or "--only")||i+1==args.Length)
            throw new ArgumentException("Use --root PATH [--group quick|regression|performance|export] [--only ID,...] [--godot PATH|--game PATH] [--skip-build] [--list]");
        options.Add(args[i],args[++i]);
    }
    string root=Path.GetFullPath(options.GetValueOrDefault("--root")??throw new ArgumentException("--root is required"));
    var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(root,"tools/checks/checks.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    if(manifest.Version!=1||manifest.Checks.Length==0||manifest.Checks.Select(c=>c.Id).Distinct().Count()!=manifest.Checks.Length)throw new InvalidDataException("Invalid check manifest");
    foreach(var c in manifest.Checks)
        if(!Regex.IsMatch(c.Id,"^[a-z0-9-]+$")||c.Markers.Length==0||c.Groups.Length==0||c.TimeoutSeconds<1||c.Kind is not ("dotnet" or "godot")||c.Args.Contains("--live"))throw new InvalidDataException("Invalid/offline-only check: "+c.Id);
    string group=options.GetValueOrDefault("--group","regression");
    var ids=options.GetValueOrDefault("--only")?.Split(',');
    if(ids is not null&&ids.Any(id=>!manifest.Checks.Any(c=>c.Id==id)))throw new ArgumentException("Unknown check ID");
    var selected=manifest.Checks.Where(c=>ids is not null?ids.Contains(c.Id):c.Groups.Contains(group)).ToArray();
    if(selected.Length==0)throw new ArgumentException("No checks selected");
    if(list){foreach(var c in selected)Console.WriteLine($"{c.Id}: {c.Kind} [{string.Join(',',c.Groups)}]");return 0;}
    string project=Path.Combine(root,"source"),logs=Path.Combine(project,".local/checks");Directory.CreateDirectory(logs);
    string dotnet=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet";
    string? game=options.GetValueOrDefault("--game"),godot=options.GetValueOrDefault("--godot");
    if(selected.Any(c=>c.Kind=="godot")&&game is null&&godot is null)throw new ArgumentException("Godot checks require --godot or --game");
    if(game is not null&&selected.Any(c=>c.Kind!="godot"))throw new ArgumentException("Export checks require a Godot-only group");
    async Task Execute(string id,string exe,IEnumerable<string> arguments,string cwd,int timeout,string[] markers,bool ownsErrors=false)
    {
        Console.WriteLine("START "+id);
        var start=new ProcessStartInfo(exe){WorkingDirectory=cwd,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(var a in arguments)start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new IOException("Could not launch "+id);
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try{await process.WaitForExitAsync(deadline.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(logs,id+".log"),(await stdout)+(await stderr));throw new TimeoutException(id+" timed out");}
        string output=(await stdout)+(await stderr),log=Path.Combine(logs,id+".log");await File.WriteAllTextAsync(log,output);
        if(process.ExitCode!=0||markers.Any(m=>!output.Contains(m))||(!ownsErrors&&Regex.IsMatch(output,@"ERROR:|\b\w*Exception\b")))
            throw new InvalidOperationException($"{id} exited {process.ExitCode}, required markers/errors failed; see {log}");
        Console.WriteLine("PASS "+id);
        foreach(var line in output.Split('\n').Where(l=>l.Contains("_MEASURE:")||l.Contains("_STATS:")))Console.WriteLine(line.Trim());
    }
    if(!skipBuild&&game is null&&selected.Any(c=>c.Kind=="godot"))
    {
        await Execute("build-game",dotnet,["build",Path.Combine(project,"NoDwarf.csproj"),"--nologo"],project,300,[]);
        await Execute("import",godot!,["--headless","--path",project,"--editor","--import","--quit","--log-file",Path.Combine(logs,"import-engine.log")],project,300,[]);
    }
    // Every console check starts outside the checkout. This tests its real runtime resources.
    string external=Path.Combine(Path.GetTempPath(),"nodwarf-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(external);
    try
    {
        foreach(var check in selected)
        {
            if(check.Kind=="dotnet")
            {
                string file=Path.GetFullPath(Path.Combine(root,check.Project));
                if(!skipBuild)await Execute("build-"+check.Id,dotnet,["build",file,"--nologo"],project,300,[]);
                string dll=Path.Combine(Path.GetDirectoryName(file)!,"bin/Debug/net8.0",Path.GetFileNameWithoutExtension(file)+".dll");
                await Execute(check.Id,dotnet,[dll],external,check.TimeoutSeconds,check.Markers,check.OwnsErrorValidation);
            }
            else
            {
                var command=new List<string>{"--headless"};
                if(game is null){command.Add("--path");command.Add(project);}
                command.AddRange(["--log-file",Path.Combine(logs,check.Id+"-engine.log"),"--"]);command.AddRange(check.Args);
                await Execute(check.Id,game??godot!,command,game is null?project:external,check.TimeoutSeconds,check.Markers,check.OwnsErrorValidation);
            }
        }
    }
    finally{Directory.Delete(external,true);}
    Console.WriteLine($"CHECK_SUITE_OK: {selected.Length} checks; logs {logs}");return 0;
}

sealed record Manifest(int Version,Check[] Checks);
sealed record Check
{
    public string Id {get;init;}="";
    public string Kind {get;init;}="";
    public string Project {get;init;}="";
    public string[] Args {get;init;}=[];
    public string[] Markers {get;init;}=[];
    public string[] Groups {get;init;}=[];
    public int TimeoutSeconds {get;init;}=180;
    public bool OwnsErrorValidation {get;init;}
}
