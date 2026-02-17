using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace OceanyaClient
{
    public partial class DoomWindow : Window
    {
        private const string DoomUrl = "https://js-dos.com/games/doom.exe.html";

        public DoomWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                FallbackOverlay.Visibility = Visibility.Collapsed;
                await DoomBrowser.EnsureCoreWebView2Async();
                DoomBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                DoomBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                DoomBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                DoomBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                DoomBrowser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                DoomBrowser.Source = new Uri(DoomUrl);
            }
            catch
            {
                FallbackOverlay.Visibility = Visibility.Visible;
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            FallbackOverlay.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
            if (!e.IsSuccess)
            {
                return;
            }

            await ApplyImmersiveLayoutAsync();
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DoomBrowser.CoreWebView2 != null)
                {
                    FallbackOverlay.Visibility = Visibility.Collapsed;
                    DoomBrowser.Reload();
                    return;
                }

                DoomBrowser.Source = new Uri(DoomUrl);
            }
            catch
            {
                FallbackOverlay.Visibility = Visibility.Visible;
            }
        }

        private async Task ApplyImmersiveLayoutAsync()
        {
            if (DoomBrowser.CoreWebView2 == null)
            {
                return;
            }

            const string script = """
(() => {
  const styleId = "oceanya-doom-immersive-style";
  if (!document.getElementById(styleId)) {
    const style = document.createElement("style");
    style.id = styleId;
    style.textContent = `
      html, body {
        margin: 0 !important;
        padding: 0 !important;
        width: 100% !important;
        height: 100% !important;
        overflow: hidden !important;
        background: #000 !important;
      }
      body * {
        box-sizing: border-box !important;
      }
      header, footer, nav, aside, .navbar, .header, .footer, .controls, .share, .social, .ads {
        display: none !important;
        visibility: hidden !important;
      }
    `;
    document.head.appendChild(style);
  }

  const pickLargest = (elements) => {
    if (elements.length === 0) {
      return null;
    }

    elements.sort((a, b) => {
      const rectA = a.getBoundingClientRect();
      const rectB = b.getBoundingClientRect();
      const areaA = Math.max(rectA.width, a.clientWidth || a.width || 0) * Math.max(rectA.height, a.clientHeight || a.height || 0);
      const areaB = Math.max(rectB.width, b.clientWidth || b.width || 0) * Math.max(rectB.height, b.clientHeight || b.height || 0);
      return areaB - areaA;
    });

    return elements[0];
  };

  const fitGameHost = () => {
    const canvases = Array.from(document.querySelectorAll("canvas"));
    const iframes = Array.from(document.querySelectorAll("iframe"));
    const hostCandidates = canvases.concat(iframes);
    const host = pickLargest(hostCandidates);
    if (!host) {
      return false;
    }

    let root = host;
    while (root.parentElement && root.parentElement !== document.body) {
      root = root.parentElement;
    }

    root.style.position = "fixed";
    root.style.inset = "0";
    root.style.margin = "0";
    root.style.padding = "0";
    root.style.width = "100vw";
    root.style.height = "100vh";
    root.style.display = "flex";
    root.style.alignItems = "center";
    root.style.justifyContent = "center";
    root.style.background = "#000";
    root.style.zIndex = "2147483646";
    root.style.overflow = "hidden";

    host.style.position = "absolute";
    host.style.inset = "0";
    host.style.margin = "0";
    host.style.padding = "0";
    host.style.border = "0";
    host.style.width = "100%";
    host.style.height = "100%";
    host.style.minWidth = "100%";
    host.style.minHeight = "100%";
    host.style.maxWidth = "100%";
    host.style.maxHeight = "100%";
    host.style.objectFit = "fill";
    host.style.background = "#000";
    host.style.transform = "none";
    host.style.imageRendering = "pixelated";

    if (host.tagName && host.tagName.toLowerCase() === "iframe") {
      host.setAttribute("allowfullscreen", "true");
      host.setAttribute("scrolling", "no");
    }

    Array.from(document.body.children).forEach((child) => {
      if (child !== root) {
        child.style.display = "none";
      }
    });

    document.body.style.overflow = "hidden";
    document.documentElement.style.overflow = "hidden";
    return true;
  };

  if (!fitGameHost()) {
    let attempts = 0;
    const timer = setInterval(() => {
      attempts++;
      if (fitGameHost() || attempts > 120) {
        clearInterval(timer);
      }
    }, 100);
  }
})();
""";

            try
            {
                await DoomBrowser.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                FallbackOverlay.Visibility = Visibility.Visible;
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }
}
