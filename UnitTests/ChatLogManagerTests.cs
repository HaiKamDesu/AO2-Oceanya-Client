using AOBot_Testing;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    public class ChatLogManagerTests
    {
        [Test]
        public void Test_ChatLogManager_StoredMessages()
        {
            // Create a ChatLogManager with a maximum of 5 messages
            var chatLogManager = new ChatLogManager(5);
            
            // Add 7 messages (2 more than the maximum)
            for (int i = 1; i <= 7; i++)
            {
                chatLogManager.AddMessage("IC", "Phoenix", "Phoenix Wright", $"Test message {i}");
            }
            
            // Get the formatted chat history
            string history = chatLogManager.GetFormattedChatHistory();
            
            // The history should contain only the 5 most recent messages
            Assert.That(history, Does.Not.Contain("Test message 1"), "Oldest message should be removed");
            Assert.That(history, Does.Not.Contain("Test message 2"), "Second oldest message should be removed");
            Assert.That(history, Does.Contain("Test message 3"), "Message 3 should be present");
            Assert.That(history, Does.Contain("Test message 7"), "Latest message should be present");
        }
        
        [Test]
        public void Test_ChatLogManager_UnlimitedStorage()
        {
            // Create a ChatLogManager with unlimited message storage (-1)
            var chatLogManager = new ChatLogManager(-1);
            
            // Add 20 messages
            for (int i = 1; i <= 20; i++)
            {
                chatLogManager.AddMessage("IC", "Phoenix", "Phoenix Wright", $"Test message {i}");
            }
            
            // Get the formatted chat history
            string history = chatLogManager.GetFormattedChatHistory();
            
            // All messages should be present (test first, middle, and last)
            Assert.That(history, Does.Contain("Test message 1"), "First message should be present");
            Assert.That(history, Does.Contain("Test message 10"), "Middle message should be present");
            Assert.That(history, Does.Contain("Test message 20"), "Last message should be present");
        }
        
        [Test]
        public void Test_ChatLogManager_FormatMessages()
        {
            // Create a ChatLogManager
            var chatLogManager = new ChatLogManager(10);
            
            // Add IC message
            chatLogManager.AddMessage("IC", "Phoenix", "Phoenix Wright", "This is an IC message");
            
            // Add OOC message
            chatLogManager.AddMessage("OOC", "", "User123", "This is an OOC message");
            
            // Get the formatted chat history
            string history = chatLogManager.GetFormattedChatHistory();
            
            // Check that messages are stored correctly
            Assert.That(history, Does.Contain("This is an IC message"), "IC message should be present in history");
            Assert.That(history, Does.Contain("This is an OOC message"), "OOC message should be present in history");
        }
        
        [Test]
        public void Test_ChatLogManager_EmptyHistory()
        {
            // Create a ChatLogManager
            var chatLogManager = new ChatLogManager(10);
            
            // Get empty history
            string history = chatLogManager.GetFormattedChatHistory();
            
            // Should return empty string or whitespace
            Assert.That(history.Trim(), Is.Empty, "Empty chat history should return empty string");
        }
        
        [Test]
        public void Test_Integration_WithAOClient()
        {
            // Create a ChatLogManager
            var chatLogManager = new ChatLogManager(10);
            
            // Create a mock AOClient (we won't actually connect it)
            var client = new AOClient("ws://localhost:8080");
            
            // Hook up the message received event
            client.OnMessageReceived += (chatLogType, characterName, showName, message, iniPuppetID) =>
            {
                chatLogManager.AddMessage(chatLogType, characterName, showName, message);
            };
            
            // Simulate receiving a few messages
            var icMsg = new ICMessage
            {
                Character = "Phoenix",
                ShowName = "Phoenix Wright",
                Message = "Court is now in session!"
            };
            
            // Manually invoke the callback (simulating message reception)
            client.OnMessageReceived?.Invoke("IC", icMsg.Character, icMsg.ShowName, icMsg.Message, 1);
            client.OnMessageReceived?.Invoke("OOC", "", "Judge", "All rise!", -1);
            
            // Get the formatted chat history
            string history = chatLogManager.GetFormattedChatHistory();
            
            // Verify messages are in the history
            Assert.That(history, Does.Contain("Court is now in session!"), "IC message should be in history");
            Assert.That(history, Does.Contain("All rise!"), "OOC message should be in history");
        }
    }
}
