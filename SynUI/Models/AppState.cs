using System.Collections.Generic;

namespace SynUI.Models
{
    public class AppState
    {
        public List<ScriptTab> Tabs { get; set; } = new List<ScriptTab>();
        public string? ActiveTabId { get; set; }
    }
}
