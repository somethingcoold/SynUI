using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace SynUI.Editor
{
    public enum CompletionType
    {
        Keyword,
        Global,
        Type,
        LocalVariable,
        LocalFunction
    }

    public class LuauCompletionData : ICompletionData
    {
        public CompletionType Type { get; }
        public string Text { get; }
        public string InsertText { get; }
        public object Description { get; }

        public LuauCompletionData(string text, CompletionType type, string description = "", string insertText = "")
        {
            Text = text;
            Type = type;
            Description = description;
            InsertText = string.IsNullOrEmpty(insertText) ? text : insertText;
        }

        public ImageSource? Image => null;

        public double Priority => Type switch
        {
            CompletionType.LocalVariable => 2.0,
            CompletionType.LocalFunction => 1.9,
            CompletionType.Global => 1.5,
            CompletionType.Type => 1.4,
            _ => 1.0
        };

        public object Content
        {
            get
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                
                // Icon backing
                var iconBorder = new Border
                {
                    Width = 16, Height = 16,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var iconText = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                };

                switch (Type)
                {
                    case CompletionType.Keyword:
                        iconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 199, 146, 234)); // Light purple
                        iconText.Foreground = new SolidColorBrush(Color.FromRgb(199, 146, 234));
                        iconText.Text = "k";
                        break;
                    case CompletionType.Global:
                        iconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 137, 221, 255)); // Light blue
                        iconText.Foreground = new SolidColorBrush(Color.FromRgb(137, 221, 255));
                        iconText.Text = "ƒ";
                        iconText.FontSize = 11;
                        iconText.Margin = new Thickness(0, -2, 0, 0);
                        break;
                    case CompletionType.Type:
                        iconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 203, 107)); // Light yellow
                        iconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107));
                        iconText.Text = "T";
                        break;
                    case CompletionType.LocalVariable:
                        iconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 247, 140, 108)); // Orange
                        iconText.Foreground = new SolidColorBrush(Color.FromRgb(247, 140, 108));
                        iconText.Text = "v";
                        break;
                    case CompletionType.LocalFunction:
                        iconBorder.Background = new SolidColorBrush(Color.FromArgb(40, 168, 219, 138)); // Green
                        iconText.Foreground = new SolidColorBrush(Color.FromRgb(168, 219, 138));
                        iconText.Text = "m";
                        break;
                }

                iconBorder.Child = iconText;
                panel.Children.Add(iconBorder);

                var textBlock = new TextBlock
                {
                    Text = Text,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas")
                };
                panel.Children.Add(textBlock);

                return panel;
            }
        }

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            // Calculate how much text is already typed
            int offset = completionSegment.Offset;
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

            // Just insert the text cleanly — no auto-parentheses
            textArea.Document.Replace(offset, completionSegment.EndOffset - offset, InsertText);
        }
    }
}
