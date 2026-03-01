using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace OceanyaClient
{
    internal static class ClipboardUtilities
    {
        private const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);

        public static bool TrySetText(string text, int retries = 8, int delayMs = 25)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (COMException ex) when (ex.HResult == ClipboardCantOpenHResult)
                {
                    if (attempt >= retries)
                    {
                        return false;
                    }

                    Thread.Sleep(delayMs);
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
