using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

class Program
{
    static void Main()
    {
        var prop = typeof(CoreWebView2Cookie).GetProperty("Expires");
        Console.WriteLine("PropertyType is: " + prop.PropertyType.FullName);
    }
}
