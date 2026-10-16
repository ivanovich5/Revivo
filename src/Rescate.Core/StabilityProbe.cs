using System.Security.Cryptography;

namespace Rescate.Core;

public sealed record StabilitySample(long Offset,int Length,string ReferenceSha256,string FirstSha256,string SecondSha256,bool MatchesReference,bool RepeatMatches);
public static class StabilityProbe
{
    public static async Task<List<StabilitySample>> Run(IReadOnlySource source,Stream reference,string evidenceFolder,CancellationToken token,int sampleCount=32)
    {
        if(source.Length!=reference.Length || source.Length<ImageAcquirer.BlockSize || !reference.CanSeek)throw new IOException("La referencia no tiene el tamaño esperado.");
        if(sampleCount<2 || sampleCount>256)throw new IOException("Número de muestras no válido.");
        evidenceFolder=Safety.LocalPath(evidenceFolder);Safety.RequireInternalDisk(evidenceFolder);Directory.CreateDirectory(evidenceFolder);
        int size=ImageAcquirer.BlockSize;long last=source.Length-size;
        var offsets=Enumerable.Range(0,sampleCount).Select(i=>last*i/(sampleCount-1)/4096*4096)
            .Concat(new[]{1432576L/4096*4096,9104896L/4096*4096,16777216L,13570768896L/4096*4096}.Where(x=>x<=last)).Distinct().Order().ToArray();
        var initial=new Dictionary<long,(string Reference,string First)>();var buffer=new byte[size];var original=new byte[size];
        foreach(var offset in offsets)
        {
            reference.Position=offset;await reference.ReadExactlyAsync(original,token);
            if(await source.Read(offset,buffer,token)!=size)throw new IOException("Lectura parcial; prueba detenida.");
            var hash=Convert.ToHexString(SHA256.HashData(buffer));
            using(var copy=new FileStream(Path.Combine(evidenceFolder,$"first-{offset}.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None))await copy.WriteAsync(buffer,token);
            initial[offset]=(Convert.ToHexString(SHA256.HashData(original)),hash);
        }
        var samples=new List<StabilitySample>();
        foreach(var offset in offsets.Reverse())
        {
            if(await source.Read(offset,buffer,token)!=size)throw new IOException("Lectura parcial; prueba detenida.");
            var hash=Convert.ToHexString(SHA256.HashData(buffer));var first=initial[offset];
            if(hash!=first.First)
            {using var copy=new FileStream(Path.Combine(evidenceFolder,$"second-{offset}.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None);await copy.WriteAsync(buffer,token);}
            samples.Add(new(offset,size,first.Reference,first.First,hash,first.Reference==first.First && first.Reference==hash,first.First==hash));
        }
        return samples.OrderBy(s=>s.Offset).ToList();
    }
}
