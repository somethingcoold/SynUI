using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using SynUI.Editor;
using SynUI.Models;

namespace SynUI.Services
{
    /// <summary>
    /// Manages all code editor interactions: auto-completion, LSP integration,
    /// and text change handling. Decoupled from MainWindow.
    /// </summary>
    public class EditorManager
    {
        private CompletionWindow? _completionWindow;
        private readonly List<LuauCompletionData> _allCompletions = new();
        private int _lspDocVersion = 1;
        private string? _activeDocUri;

        /// <summary>
        /// Sets the LSP document URI for the currently active tab.
        /// </summary>
        public void SetActiveDocument(string tabId)
        {
            _activeDocUri = $"file:///{tabId}.lua";
        }

        /// <summary>
        /// Opens a document in the LSP for the given tab.
        /// </summary>
        public void OpenLspDocument(string tabId, string content)
        {
            _ = LspManager.Instance.OpenDocumentAsync($"file:///{tabId}.lua", content);
        }

        /// <summary>
        /// Called when the editor text changes. Syncs to LSP and filters completions.
        /// </summary>
        public void HandleTextChanged(TextArea textArea, string fullText)
        {
            if (_activeDocUri != null)
            {
                _lspDocVersion++;
                _ = LspManager.Instance.UpdateDocumentAsync(_activeDocUri, fullText, _lspDocVersion);
            }

            // Filter completions in real-time
            if (_completionWindow != null)
            {
                string prefix = GetCurrentWordPrefix(textArea);
                if (string.IsNullOrEmpty(prefix))
                {
                    _completionWindow.Close();
                    _completionWindow = null;
                }
                else
                {
                    FilterCompletionData(prefix);
                }
            }
        }

        /// <summary>
        /// Called when a character is entered. Triggers completion on letters or dot.
        /// </summary>
        public async void HandleTextEntered(TextArea textArea, string enteredText, Func<string, Brush?> findResource)
        {
            if (enteredText.Length == 0) return;
            char ch = enteredText[0];

            if (!char.IsLetter(ch) && ch != '.') return;
            if (_completionWindow != null) return;

            // Get completions from LSP
            int line = textArea.Caret.Line - 1; // 0-indexed
            int col = textArea.Caret.Column - 1;
            var lspItems = await LspManager.Instance.GetCompletionsAsync(
                _activeDocUri ?? "", line, col);

            if (!lspItems.Any() && ch != '.')
            {
                // Fallback to local keywords if LSP isn't ready
                lspItems = LuauKeywords.GetAll()
                    .Select(k => new LspCompletionItem
                    {
                        Label = k.Text,
                        Kind = (int)k.Type
                    }).ToList();
            }

            if (!lspItems.Any()) return;

            _completionWindow = new CompletionWindow(textArea);

            // Style the completion window
            StyleCompletionWindow(_completionWindow, findResource);

            // Map LSP items to UI completions
            _allCompletions.Clear();
            foreach (var item in lspItems)
            {
                CompletionType mappedType = item.Kind switch
                {
                    3 or 2 => CompletionType.LocalFunction, // Function/Method
                    6 => CompletionType.LocalVariable,       // Variable
                    14 or 1 => CompletionType.Keyword,       // Keyword/Text
                    _ => CompletionType.Keyword
                };

                _allCompletions.Add(new LuauCompletionData(
                    item.Label, mappedType, item.Detail ?? "", item.InsertText));
            }

            // Initial filter
            string prefix = GetCurrentWordPrefix(textArea);
            FilterCompletionData(prefix);

            if (_completionWindow != null && _completionWindow.CompletionList.CompletionData.Count > 0)
            {
                _completionWindow.Show();
                _completionWindow.Closed += delegate { _completionWindow = null; };
            }
            else
            {
                _completionWindow?.Close();
                _completionWindow = null;
            }
        }

        /// <summary>
        /// Called when text is about to be entered. Handles completion dismissal on non-word chars.
        /// </summary>
        public void HandleTextEntering(TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                char ch = e.Text[0];

                // Dismiss completion on brackets/parens — don't insert
                if (ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
                    ch == '{' || ch == '}' || ch == '"' || ch == '\'')
                {
                    _completionWindow.Close();
                    _completionWindow = null;
                    return;
                }

                if (!char.IsLetterOrDigit(ch) && ch != '_')
                {
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        private void StyleCompletionWindow(CompletionWindow window, Func<string, Brush?> findResource)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            window.BorderThickness = new Thickness(0);

            var listBoxStyle = new Style(typeof(ListBox));
            listBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            listBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, findResource("BorderBrush")));
            listBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, findResource("BgElevatedBrush")));
            listBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, findResource("TextPrimaryBrush")));

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 8, 2)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            var trigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, findResource("BgHoverBrush")));
            itemStyle.Triggers.Add(trigger);

            window.Resources.Add(typeof(ListBoxItem), itemStyle);
            window.CompletionList.ListBox.Style = listBoxStyle;
        }

        private string GetCurrentWordPrefix(TextArea textArea)
        {
            int offset = textArea.Caret.Offset;
            string wordPrefix = "";
            while (offset > 0)
            {
                char c = textArea.Document.GetCharAt(offset - 1);
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    wordPrefix = c + wordPrefix;
                    offset--;
                }
                else break;
            }
            return wordPrefix;
        }

        private void FilterCompletionData(string prefix)
        {
            if (_completionWindow == null) return;

            var data = _completionWindow.CompletionList.CompletionData;
            data.Clear();

            var matches = _allCompletions
                .Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Priority)
                .ThenBy(c => c.Text);

            foreach (var match in matches)
                data.Add(match);

            if (!data.Any())
            {
                _completionWindow.Close();
                _completionWindow = null;
            }
            else
            {
                _completionWindow.CompletionList.SelectItem(prefix);
            }
        }
    }
}
