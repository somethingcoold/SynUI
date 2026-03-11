namespace SynUI.Models
{
    /// <summary>
    /// Represents a single completion item received from the Luau LSP.
    /// </summary>
    public class LspCompletionItem
    {
        public string Label { get; set; } = "";
        public string? Detail { get; set; }
        public int Kind { get; set; }
        public string InsertText { get; set; } = "";
    }
}
