using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace OpenQuickHost;
public sealed partial class LocalAgentApiServer {
    private async Task<bool> TryCompanionFiles(HttpListenerRequest request,HttpListenerResponse response,string path) {
        const string prefix="/v1/companion/files/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal)) return false;
        var file=YanziFileWorkflow.Ticket(path[prefix.Length..]);
        if(request.HttpMethod=="GET") {
            if(!File.Exists(file)){await WriteJsonAsync(response,404,new{error="file_ticket_not_found"});return true;}
            long offset=0;
            if(request.QueryString["offset"] is { } start && (!long.TryParse(start,out offset)||offset<0||offset>new FileInfo(file).Length)){await WriteJsonAsync(response,416,new{error="invalid_offset"});return true;}
            response.ContentType="application/octet-stream";response.ContentLength64=new FileInfo(file).Length-offset;
            await using var input=File.OpenRead(file);input.Position=offset;await input.CopyToAsync(response.OutputStream);response.Close();return true;
        }
        if(request.HttpMethod!="PUT"){await WriteJsonAsync(response,405,new{error="method_not_allowed"});return true;}
        if(request.ContentLength64<=0 || request.ContentLength64>YanziFileWorkflow.Limit) {await WriteJsonAsync(response,413,new{error="file_size_limit"});return true;}
        var expected=request.Headers["X-Content-Sha256"];
        var temp=file+"."+Guid.NewGuid().ToString("N")+".part";
        try {
            long total=0;await using(var output=File.Create(temp)) {
                var buffer=new byte[65536];int count;while((count=await request.InputStream.ReadAsync(buffer))>0){total+=count;if(total>YanziFileWorkflow.Limit)throw new IOException("file_size_limit");await output.WriteAsync(buffer.AsMemory(0,count));}
            }
            var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temp))).ToLowerInvariant();
            if(total!=request.ContentLength64 || hash!=expected) {await WriteJsonAsync(response,400,new{error="checksum_mismatch"});return true;}
            if(File.Exists(file)){if(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant()!=hash){await WriteJsonAsync(response,409,new{error="ticket_conflict"});return true;}}
            else File.Move(temp,file);
            await WriteJsonAsync(response,200,new{ok=true,sha256=hash,size=total});return true;
        } finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
