using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

class Program
{
    static void Main()
    {
        var type = typeof(CoreWebView2CookieManager);
        foreach (var method in type.GetMethods()) {
            if (method.Name.Contains("GetCookies")) {
                Console.WriteLine(method.ToString());
            }
        }
    }
}
