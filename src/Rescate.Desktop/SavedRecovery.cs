using System.IO;

namespace Rescate.Desktop;

internal sealed record SavedRecovery(string Folder,bool HasFiles)
{
    public string Label => (HasFiles?"Archivos recuperados":"Copia de la USB · lista para analizar")+" · "+Directory.GetLastWriteTime(Folder).ToString("dd/MM/yyyy HH:mm")+"\n"+Folder;
    public static List<SavedRecovery> Find(string root)
    {
        var found=new List<SavedRecovery>();var queue=new Queue<(string,int)>();queue.Enqueue((root,0));int visited=0;
        while(queue.TryDequeue(out var next))
        {
            if(++visited>2000)throw new IOException("Hay demasiadas carpetas. Elige la carpeta de una memoria concreta.");
            if((File.GetAttributes(next.Item1)&FileAttributes.ReparsePoint)!=0)continue;
            if(File.Exists(Path.Combine(next.Item1,"informe.json"))){found.Add(new(next.Item1,true));continue;}
            if(File.Exists(Path.Combine(next.Item1,"usb.img")))found.Add(new(next.Item1,false));
            if(next.Item2>=3)continue;
            foreach(var dir in Directory.EnumerateDirectories(next.Item1))
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0)queue.Enqueue((dir,next.Item2+1));
        }
        // A case with exported files should reopen those, not scan its image again.
        return found.Where(x=>x.HasFiles || !found.Any(y=>y.HasFiles && y.Folder.StartsWith(x.Folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x=>Directory.GetLastWriteTime(x.Folder)).ToList();
    }
}
