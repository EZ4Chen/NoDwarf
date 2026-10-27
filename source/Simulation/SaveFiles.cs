namespace NoDwarf.Simulation;

public static class SaveFiles
{
    public static string ReadPath(string current,string legacy)=>File.Exists(current)?current:legacy;
    public static void Write(string path,string json,string? previous=null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+".tmp";
        try
        {
            File.WriteAllText(temp,json);
            string? backup=File.Exists(path)?path:previous;
            if(backup is not null&&File.Exists(backup))File.Copy(backup,path+".bak",true);
            File.Move(temp,path,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
