namespace NoDwarf.Simulation;

public static class SaveFiles
{
    public static void Write(string path,string json)
        =>Write(path,stream=>{using var writer=new StreamWriter(stream,leaveOpen:true);writer.Write(json);});
    public static void Write(string path,Action<Stream> encode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {encode(stream);stream.Flush(true);}
            if(File.Exists(path))File.Copy(path,path+".bak",true);
            File.Move(temp,path,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
