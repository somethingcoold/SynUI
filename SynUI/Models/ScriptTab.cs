using System;

namespace SynUI.Models
{
    public class ScriptTab
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Untitled";
        public string Content { get; set; } = "";
        public bool IsAutoExec { get; set; } = false;
    }
}
