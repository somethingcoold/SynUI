using System;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

class Program
{
    static void Main()
    {
        try
        {
            using var reader = new XmlTextReader("Lua.xshd");
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            Console.WriteLine("SUCCESS! Parsing worked perfectly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR PARSING LUA.XSHD:");
            Console.WriteLine(ex.ToString());
        }
    }
}
