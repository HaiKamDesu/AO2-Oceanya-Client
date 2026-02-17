using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AOBot_Testing.Agents;
using Common;

/// <summary>
/// 
/// </summary>
/// <param name="MaxChatHistory">Number of stored messages, if -1, it will store all messages</param>
public class ChatLogManager(int MaxChatHistory)
{
    private readonly List<string> _chatHistory = new List<string>();
    private int MaxChatHistory = MaxChatHistory; 

    /// <summary>
    /// Adds a new message to the chatlog and determines if a response is needed.
    /// </summary>
    public void AddMessage(string chatLogType, string characterName, string showName, string message)
    {
        string formattedMessage = FormatMessage(chatLogType, characterName, showName, message);

        _chatHistory.Add(message);
        if(MaxChatHistory != -1 && _chatHistory.Count > MaxChatHistory)
        {
            _chatHistory.RemoveAt(0); // Remove oldest message when limit is exceeded
        }

        CustomConsole.Info(formattedMessage);
    }

    /// <summary>
    /// Formats a message properly before storing it.
    /// </summary>
    private string FormatMessage(string chatLogType, string characterName, string showName, string message)
    {
        string timestamp = DateTime.UtcNow.ToString("ddd MMM d HH:mm:ss yyyy");
        if (chatLogType == "OOC")
        {
            return $"[OOC][{timestamp}] {showName}: {message}";
        }

        return $"[{timestamp}] {showName} ({characterName}): {message}";
    }

    /// <summary>
    /// Retrieves the last N messages as formatted context.
    /// </summary>
    public string GetFormattedChatHistory()
    {
        return string.Join("\n", _chatHistory);
    }
}
