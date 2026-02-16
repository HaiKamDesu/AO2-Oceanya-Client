using Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    public class GlobalsTests
    {
        [Test]
        public void Test_ReplaceTextForSymbols()
        {
            // Test replacing special characters
            Assert.That(Globals.ReplaceTextForSymbols("<percent>"), Is.EqualTo("%"), "Should replace <percent> with %");
            Assert.That(Globals.ReplaceTextForSymbols("<dollar>"), Is.EqualTo("$"), "Should replace <dollar> with $");
            Assert.That(Globals.ReplaceTextForSymbols("<num>"), Is.EqualTo("#"), "Should replace <num> with #");
            Assert.That(Globals.ReplaceTextForSymbols("<and>"), Is.EqualTo("&"), "Should replace <and> with &");
            
            // Test multiple replacements in a single string
            Assert.That(
                Globals.ReplaceTextForSymbols("Price: <dollar>10.99 (<percent>5 off)"),
                Is.EqualTo("Price: $10.99 (%5 off)"),
                "Should replace multiple placeholders in a string"
            );
            
            // Test string with no replacements
            Assert.That(
                Globals.ReplaceTextForSymbols("This is a normal string"),
                Is.EqualTo("This is a normal string"),
                "Should return original string when no replacements are needed"
            );
        }
        
        [Test]
        public void Test_ReplaceSymbolsForText()
        {
            // Test replacing special characters in reverse
            Assert.That(Globals.ReplaceSymbolsForText("%"), Is.EqualTo("<percent>"), "Should replace % with <percent>");
            Assert.That(Globals.ReplaceSymbolsForText("$"), Is.EqualTo("<dollar>"), "Should replace $ with <dollar>");
            Assert.That(Globals.ReplaceSymbolsForText("#"), Is.EqualTo("<num>"), "Should replace # with <num>");
            Assert.That(Globals.ReplaceSymbolsForText("&"), Is.EqualTo("<and>"), "Should replace & with <and>");
            
            // Test multiple replacements in a single string
            Assert.That(
                Globals.ReplaceSymbolsForText("Price: $10.99 (%5 off)"),
                Is.EqualTo("Price: <dollar>10.99 (<percent>5 off)"),
                "Should replace multiple symbols in a string"
            );
            
            // Test string with no replacements
            Assert.That(
                Globals.ReplaceSymbolsForText("This is a normal string"),
                Is.EqualTo("This is a normal string"),
                "Should return original string when no replacements are needed"
            );
        }
        
        [Test]
        public void Test_ReplaceSymbols_Bidirectional()
        {
            // Test that text -> symbols -> text results in original string
            string original = "Test message with # $ % & symbols";
            string encoded = Globals.ReplaceSymbolsForText(original);
            string decoded = Globals.ReplaceTextForSymbols(encoded);
            
            Assert.That(decoded, Is.EqualTo(original), "Bidirectional replacement should result in original string");
        }
        
        [Test]
        public void Test_AllowedImageExtensions()
        {
            // Verify that the allowed extensions list contains expected formats
            Assert.That(Globals.AllowedImageExtensions, Does.Contain("png"), "PNG should be allowed");
            Assert.That(Globals.AllowedImageExtensions, Does.Contain("jpg"), "JPG should be allowed");
            Assert.That(Globals.AllowedImageExtensions, Does.Contain("gif"), "GIF should be allowed");
            
            // Check overall count to make sure all formats are present
            Assert.That(Globals.AllowedImageExtensions.Count, Is.GreaterThanOrEqualTo(5), "Should have at least 5 allowed image formats");
        }
    }
    
    [TestFixture]
    public class SaveFileTests
    {
        private string _testFilePath;
        
        [SetUp]
        public void SetUp()
        {
            // Create a temporary file path for testing
            _testFilePath = Path.Combine(Path.GetTempPath(), $"savefiletest_{Guid.NewGuid()}.json");
        }
        
        [TearDown]
        public void TearDown()
        {
            // Clean up any test files
            if (File.Exists(_testFilePath))
            {
                try
                {
                    File.Delete(_testFilePath);
                }
                catch
                {
                    // Ignore errors in cleanup
                }
            }
        }
        
        [Test]
        public void Test_SaveFile_Initialization()
        {
            // Access the SaveFile static instance
            var data = OceanyaClient.SaveFile.Data;
            
            // Verify default values are initialized
            Assert.That(data, Is.Not.Null, "SaveFile.Data should not be null");
            Assert.That(data.LogMaxMessages, Is.GreaterThanOrEqualTo(0), "LogMaxMessages should have a default value");
        }
        
        [Test]
        public void Test_SaveFile_CustomConsoleIntegration()
        {
            // Set a test value that would be saved
            OceanyaClient.SaveFile.Data.LogMaxMessages = 100;
            
            // Capture console output
            var prevOutput = CustomConsole.OnWriteLine;
            var consoleOutput = new StringBuilder();
            CustomConsole.OnWriteLine = (s) => consoleOutput.AppendLine(s);
            
            // Generate some debug output
            CustomConsole.Debug("Test debug message");
            
            // Reset console output
            CustomConsole.OnWriteLine = prevOutput;
            
            // Verify debug message was processed
            #if DEBUG
            Assert.That(consoleOutput.ToString(), Does.Contain("Test debug message"), "Debug message should be processed");
            #else
            Assert.Pass("Debug message test skipped in Release mode");
            #endif
        }
    }
    
    [TestFixture]
    public class CustomConsoleTests
    {
        private StringBuilder _testOutput;
        private Action<string> _originalOnWriteLine;
        
        [SetUp]
        public void SetUp()
        {
            _testOutput = new StringBuilder();
            
            // Store original output methods
            _originalOnWriteLine = CustomConsole.OnWriteLine;
            
            // Override with test methods
            CustomConsole.OnWriteLine = (s) => _testOutput.AppendLine(s);
        }
        
        [TearDown]
        public void TearDown()
        {
            // Restore original output methods
            CustomConsole.OnWriteLine = _originalOnWriteLine;
        }
        
        [Test]
        public void Test_CustomConsole_InfoOutput()
        {
            CustomConsole.Info("Test info message");
            
            Assert.That(_testOutput.ToString(), Does.Contain("ℹ️ Test info message"), 
                "Info message should be formatted correctly");
        }
        
        [Test]
        public void Test_CustomConsole_ErrorOutput()
        {
            // Test with just message
            CustomConsole.Error("Test error message");
            
            Assert.That(_testOutput.ToString(), Does.Contain("❌ Test error message"), 
                "Error message should be formatted correctly");
            
            // Clear output
            _testOutput.Clear();
            
            // Test with exception
            var exception = new Exception("Test exception");
            CustomConsole.Error("Test error with exception", exception);
            
            string output = _testOutput.ToString();
            Assert.That(output, Does.Contain("❌ Test error with exception"), 
                "Error with exception should include message");
            Assert.That(output, Does.Contain("Exception: Exception"), 
                "Error with exception should include exception type");
            Assert.That(output, Does.Contain("Message: Test exception"), 
                "Error with exception should include exception message");
        }
        
        [Test]
        public void Test_CustomConsole_DebugOutput()
        {
            CustomConsole.Debug("Test debug message");
            
            // This may not work in Release mode since Debug is conditional
            #if DEBUG
            Assert.That(_testOutput.ToString(), Does.Contain("🔍 Test debug message"), 
                "Debug message should be formatted correctly");
            #else
            Assert.Pass("Debug message test skipped in Release mode");
            #endif
        }
        
        [Test]
        public void Test_CustomConsole_WarningOutput()
        {
            CustomConsole.Warning("Test warning message");
            
            Assert.That(_testOutput.ToString(), Does.Contain("⚠️ Test warning message"), 
                "Warning message should be formatted correctly");
        }
        
        [Test]
        public void Test_CustomConsole_LogOutput()
        {
            CustomConsole.Log("Test log message", CustomConsole.LogLevel.Info);
            
            Assert.That(_testOutput.ToString(), Does.Contain("ℹ️ Test log message"), 
                "Log with Info level should be formatted correctly");
        }
    }
}
