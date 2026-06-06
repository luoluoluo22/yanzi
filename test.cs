using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

class Program
{
    static void Main()
    {
        var method = typeof(CoreWebView2CookieManager).GetMethod("GetCookiesAsync");
        if (method != null) {
            Console.WriteLine(method.ToString());
        }
    }
}
