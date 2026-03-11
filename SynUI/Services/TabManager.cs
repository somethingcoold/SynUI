using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SynUI.Models;

namespace SynUI.Services
{
    /// <summary>
    /// Manages the script tab list, active tab tracking, and state persistence.
    /// Decoupled from the UI — communicates changes via callbacks.
    /// </summary>
    public class TabManager
    {
        private List<ScriptTab> _tabs = new();
        private string? _activeTabId;

        /// <summary>Fired when tabs change and the UI needs to rebuild its tab list.</summary>
        public event Action? TabsChanged;

        /// <summary>Fired when the active tab switches, providing the new tab's content.</summary>
        public event Action<ScriptTab>? ActiveTabChanged;

        public IReadOnlyList<ScriptTab> Tabs => _tabs.AsReadOnly();
        public string? ActiveTabId => _activeTabId;

        /// <summary>
        /// Loads state from disk and populates the tab list.
        /// </summary>
        public void LoadState()
        {
            var state = ScriptManager.Load();
            _tabs = state.Tabs;
            _activeTabId = state.ActiveTabId ?? _tabs.FirstOrDefault()?.Id;

            TabsChanged?.Invoke();
            if (_activeTabId != null)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == _activeTabId);
                if (tab != null) ActiveTabChanged?.Invoke(tab);
            }
        }

        /// <summary>
        /// Saves current state to disk. Call <paramref name="syncEditor"/> to capture
        /// the editor's current text before saving.
        /// </summary>
        public void SaveState(Func<string?>? getEditorContent = null)
        {
            SyncEditorToActiveTab(getEditorContent);
            ScriptManager.Save(new AppState { Tabs = _tabs, ActiveTabId = _activeTabId });
        }

        /// <summary>
        /// Syncs the current editor text to the active tab's Content property.
        /// </summary>
        public void SyncEditorToActiveTab(Func<string?>? getEditorContent = null)
        {
            if (getEditorContent == null) return;
            var activeTab = _tabs.FirstOrDefault(t => t.Id == _activeTabId);
            if (activeTab != null)
            {
                string? content = getEditorContent();
                if (content != null) activeTab.Content = content;
            }
        }

        public void SwitchToTab(string? tabId)
        {
            if (tabId == null) return;
            _activeTabId = tabId;
            var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab != null) ActiveTabChanged?.Invoke(tab);
        }

        public void AddTab(bool isAutoExec = false)
        {
            int nextNum = _tabs.Count + 1;
            string prefix = isAutoExec ? "AutoExec" : "Script";
            string suffix = isAutoExec ? ".lua" : "";

            while (_tabs.Any(t => t.Name == $"{prefix} {nextNum}{suffix}"))
                nextNum++;

            var newTab = new ScriptTab
            {
                Name = $"{prefix} {nextNum}{suffix}",
                Content = isAutoExec ? "-- AutoExec Script\n" : "-- Write your Lua script here\n",
                IsAutoExec = isAutoExec
            };

            _tabs.Add(newTab);
            SwitchToTab(newTab.Id);
            TabsChanged?.Invoke();
            SaveState();
        }

        /// <summary>
        /// Returns the new tab's ID for further actions like renaming.
        /// </summary>
        public string? GetLatestTabId() => _tabs.LastOrDefault()?.Id;

        public void CloseTab(string tabId)
        {
            if (_tabs.Count <= 1) return;

            int idx = _tabs.FindIndex(t => t.Id == tabId);
            if (idx < 0) return;

            var tabToDelete = _tabs[idx];
            _tabs.RemoveAt(idx);

            // Physically delete the file from disk so it doesn't come back
            ScriptManager.DeleteScriptFile(tabToDelete.Name, tabToDelete.IsAutoExec);

            if (_activeTabId == tabId)
            {
                int newIdx = Math.Min(idx, _tabs.Count - 1);
                SwitchToTab(_tabs[newIdx].Id);
            }

            TabsChanged?.Invoke();
            SaveState();
        }

        public void DuplicateTab(string tabId, Func<string?>? getEditorContent = null)
        {
            var original = _tabs.FirstOrDefault(t => t.Id == tabId);
            if (original == null) return;

            SyncEditorToActiveTab(getEditorContent);

            var duplicate = new ScriptTab
            {
                Name = original.Name + " (copy)",
                Content = original.Content,
                IsAutoExec = original.IsAutoExec
            };

            int idx = _tabs.FindIndex(t => t.Id == tabId);
            _tabs.Insert(idx + 1, duplicate);

            SwitchToTab(duplicate.Id);
            TabsChanged?.Invoke();
            SaveState();
        }

        public void ToggleAutoExec(string tabId)
        {
            var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab == null) return;

            if (tab.IsAutoExec)
            {
                tab.IsAutoExec = false;
                ScriptManager.RemoveAutoExecFile(tab.Name);
            }
            else
            {
                tab.IsAutoExec = true;
            }

            SaveState();
            TabsChanged?.Invoke();
        }

        public void RenameTab(string tabId, string newName)
        {
            var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab == null || string.IsNullOrWhiteSpace(newName)) return;

            tab.Name = newName.Trim();
            TabsChanged?.Invoke();
            SaveState();
        }

        public ScriptTab? GetTab(string tabId)
        {
            return _tabs.FirstOrDefault(t => t.Id == tabId);
        }

        public int TabCount => _tabs.Count;
    }
}
