using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace OceanyaClient
{
    internal static class ClipboardUtilities
    {
        private const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);

        public static bool TrySetText(string text, int retries = 0, int delayMs = 0)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, false);
                    return true;
                }
                catch (COMException ex) when (ex.HResult == ClipboardCantOpenHResult)
                {
                    if (attempt >= retries)
                    {
                        return false;
                    }

                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool TryGetText(out string text, int retries = 0, int delayMs = 0)
        {
            text = string.Empty;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsText())
                    {
                        return false;
                    }

                    text = Clipboard.GetText();
                    return true;
                }
                catch (COMException ex) when (ex.HResult == ClipboardCantOpenHResult)
                {
                    if (attempt >= retries)
                    {
                        return false;
                    }

                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
