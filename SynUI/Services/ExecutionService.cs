using System;
using System.Windows;
using System.Windows.Media;
using SynUI.API;

namespace SynUI.Services
{
    public enum StatusType { Info, Success, Warning, Error }

    /// <summary>
    /// Handles script execution through SynapseZAPI and reports status via callbacks.
    /// </summary>
    public class ExecutionService
    {
        /// <summary>
        /// Fired with the status message and type after execution completes.
        /// </summary>
        public event Action<string, StatusType>? OnStatusUpdate;

        /// <summary>
        /// Executes a Lua script through SynapseZ and fires status update.
        /// PID = 0 targets all injected instances.
        /// </summary>
        public void Execute(string script, int pid = 0)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                OnStatusUpdate?.Invoke("No script to execute", StatusType.Warning);
                return;
            }

            OnStatusUpdate?.Invoke("Executing...", StatusType.Info);

            try
            {
                int result = SynapseZAPI.Execute(script, pid);

                switch (result)
                {
                    case 0:
                        OnStatusUpdate?.Invoke("Executed successfully", StatusType.Success);
                        break;
                    case 1:
                        OnStatusUpdate?.Invoke("Error: Bin folder not found", StatusType.Error);
                        break;
                    case 2:
                        OnStatusUpdate?.Invoke("Error: Scheduler folder not found", StatusType.Error);
                        break;
                    case 3:
                        string errMsg = SynapseZAPI.GetLatestErrorMessage();
                        OnStatusUpdate?.Invoke($"Error: {errMsg}", StatusType.Error);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"Error: {ex.Message}", StatusType.Error);
            }
        }
    }
}
