using System;

namespace OceanyaClient.Components
{
    public sealed class LogMessageActionLink
    {
        public LogMessageActionLink(string text, Action onClick, string toolTip = "")
        {
            Text = string.IsNullOrWhiteSpace(text) ? throw new ArgumentNullException(nameof(text)) : text;
            OnClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
            ToolTip = toolTip ?? string.Empty;
        }

        public string Text { get; }

        public Action OnClick { get; }

        public string ToolTip { get; }
    }

    public sealed class LogMessageHandle
    {
        internal LogMessageHandle()
        {
            Id = Guid.NewGuid();
        }

        internal Guid Id { get; }
    }
}
