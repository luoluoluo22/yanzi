using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;
public static class YanziAction {
 public static Task<string> RunAsync(YanziActionContext context) {
  var input=(context.InputText??"").Trim();
  var settings=Path.Combine(context.ExtensionDataDirectory,"gateway.json");
  if(input.StartsWith("/配置 ", StringComparison.Ordinal)){
   var uri=Validate(input.Substring(4));
   Directory.CreateDirectory(context.ExtensionDataDirectory);
   File.WriteAllText(settings,JsonSerializer.Serialize(new { gateway=uri }));
   return Task.FromResult("远控网关已配置。再次点击小程序即可连接。");
  }
  if(input=="/清除"){if(File.Exists(settings))File.Delete(settings);return Task.FromResult("已清除本机网关配置。");}
  string gateway="";
  if(File.Exists(settings)){using var d=JsonDocument.Parse(File.ReadAllText(settings));gateway=Validate(d.RootElement.GetProperty("gateway").GetString()??"");}
  if(input=="/状态")return Task.FromResult(gateway.Length>0?"远控网关："+gateway:"尚未配置网关。");
  if(gateway.Length==0)return Task.FromResult("远控网关尚未设置，请输入 /配置 https://你的远控域名");
  var start=new ProcessStartInfo(gateway){UseShellExecute=true};
  Process.Start(start);
  return Task.FromResult("已打开远控网关，请选择已授权的设备。");
 }
 private static string Validate(string value){
  if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.AbsolutePath!="/")throw new ArgumentException("只接受 HTTPS 网关根地址。");
  return uri.GetLeftPart(UriPartial.Authority)+"/";
 }
}