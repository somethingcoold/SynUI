using System.Collections.Generic;

namespace SynUI.Editor
{
    public static class LuauKeywords
    {
        public static readonly string[] Keywords = new[]
        {
            "and", "break", "continue", "do", "else", "elseif", "end", "false", "for",
            "function", "if", "in", "local", "nil", "not", "or", "repeat", "return",
            "then", "true", "until", "while", "export", "type", "typeof"
        };

        public static readonly string[] Globals = new[]
        {
            "game", "workspace", "script", "math", "string", "table", "coroutine", "debug", 
            "os", "task", "bit32", "utf8", "buffer",
            "print", "warn", "error", "pcall", "xpcall", "require",
            "setmetatable", "getmetatable", "rawget", "rawset", "rawequal", "rawlen",
            "tostring", "tonumber", "pairs", "ipairs", "next", "select", "unpack", "assert",
            "shared", "_G", "_VERSION"
        };

        public static readonly string[] RobloxTypes = new[]
        {
            "Instance", "Vector2", "Vector3", "CFrame", "Color3", "BrickColor",
            "UDim", "UDim2", "Region3", "Ray", "Rect", "TweenInfo", "NumberRange",
            "NumberSequence", "ColorSequence", "PhysicalProperties", "Enum"
        };
        
        public static IEnumerable<LuauCompletionData> GetAll()
        {
            foreach (var kw in Keywords) yield return new LuauCompletionData(kw, CompletionType.Keyword, "Keyword");
            foreach (var gl in Globals) yield return new LuauCompletionData(gl, CompletionType.Global, "Global Function/Library");
            foreach (var rt in RobloxTypes) yield return new LuauCompletionData(rt, CompletionType.Type, "Data Type");
        }
    }
}
