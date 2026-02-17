using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Interactions;
using System;
using System.Threading;
using SeleniumExtras.WaitHelpers;
using System.Windows;

namespace AOBot_Testing.Agents
{
    public class W2GController : IDisposable
    {
        private readonly IWebDriver driver;
        private readonly WebDriverWait wait;
        private readonly IJavaScriptExecutor js;
        private bool isLoggedIn = false;

        public W2GController(bool headless = true)
        {
            var options = new ChromeOptions();
            if (headless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--window-size=1920,1200");
                options.AddArgument("--no-sandbox");
            }

            // Desktop-specific arguments
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--incognito");

            // Set a more specific desktop user agent
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");

            // Prevent Selenium/Chrome from being detected as automated
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--lang=en-US");

            // Add preferences to avoid detection
            var prefs = new Dictionary<string, object>();
            prefs.Add("profile.default_content_setting_values.notifications", 2);
            options.AddUserProfilePreference("credentials_enable_service", false);
            options.AddUserProfilePreference("profile.password_manager_enabled", false);

            driver = new ChromeDriver(options);

            // Set window size explicitly after driver initialization
            driver.Manage().Window.Size = new System.Drawing.Size(1920, 1080);

            // Use JavaScript to override viewport dimensions
            js = (IJavaScriptExecutor)driver;
            js.ExecuteScript("window.innerWidth = 1920; window.innerHeight = 1080;");
            js.ExecuteScript("Object.defineProperty(navigator, 'maxTouchPoints', {get: function() {return 0;}})");

            wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            // Set implicit wait for element finding
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
        }



        // Log into Watch2Gether using your credentials
        public bool Login(string username, string password)
        {
            try
            {
                // Navigate to the login page
                driver.Navigate().GoToUrl("https://w2g.tv/en/account/");

                // Wait for the page to load
                wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));

                // Accept cookies if prompt appears
                /*try
                {
                    var acceptCookiesButton = driver.FindElement(By.XPath("//button[contains(text(), 'Accept') or contains(text(), 'accept all')]"));
                    acceptCookiesButton.Click();
                    Thread.Sleep(1000); // Wait for cookie notice to disappear
                }
                catch (NoSuchElementException)
                {
                    // Cookie prompt might not appear, continue
                }*/

                // Locate and fill in the username field
                var usernameField = wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("input[type='email']")));
                usernameField.Clear();
                usernameField.SendKeys(username);

                // Locate and fill in the password field
                var passwordField = wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("input[type='password']")));
                passwordField.Clear();
                passwordField.SendKeys(password);

                // Submit the login form
                var loginButton = wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("button[type='submit']")));
                loginButton.Click();

                // Wait for login to complete - check for user menu or avatar
                var userAvatarSelector =
                                        "/html/body/nav/div[1]/div/div[3]/div/div/div[1]/img";
                wait.Until(d => d.FindElements(By.XPath(userAvatarSelector)).Count > 0);

                isLoggedIn = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
                return false;
            }
        }

        // Create a new room and return the room URL
        public string? CreateRoom(string roomName = "")
        {
            try
            {
                // Go to the homepage
                driver.Navigate().GoToUrl("https://w2g.tv/");

                var createRoomButton = wait.Until(ExpectedConditions.ElementToBeClickable(
                        By.XPath(isLoggedIn ? "/html/body/main/div/div[1]/button[1]" : "/html/body/main/div/div/div[1]/button")));
                
                    
                createRoomButton.Click();

                // Look for the "Copy" button and click it
                var roomInvUrl = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"invite-modal\"]/div[2]/div[1]/div[2]/div/div[2]/button")));
                roomInvUrl.Click();

                Thread.Sleep(500); // brief wait for the copy action
                
                // Copy it from the clipboard
                string clipboardText = RunOnSTAThread(() =>
                {
                    return Clipboard.GetText();
                }) ?? string.Empty;

                // Close the popup
                var closeButton = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"invite-modal\"]/div[2]/div[4]/div")));
                closeButton.Click();

                // Wait for the room to load by checking for player container
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@id=\"player_container\"]")));

                // If room name is provided, set it
                if (!string.IsNullOrWhiteSpace(roomName))
                {
                    if (isLoggedIn)
                    {
                        try
                        {
                            //click on the "copy" popup
                            var closePopupButton = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("/html/body/div[11]/div/div/div[1]/div[4]/button")));
                            closePopupButton.Click();
                        }
                        catch
                        {
                            // Room settings might not appear immediately, continue
                        }

                        var settingsButton = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("/html/body/div[7]/div[1]/div/div[1]/div[1]/a[3]/div")));
                        settingsButton.Click();

                        var roomNameInput = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"w2g-save-room-input\"]")));
                        roomNameInput.Click();
                        roomNameInput.SendKeys(roomName);

                        // Click save button
                        var saveButton = driver.FindElement(By.XPath("//*[@id=\"w2g-save-room-form\"]/div/button"));
                        saveButton.Click();
                    }
                    else
                    {
                        //if not logged in, you cannot make a saved room.
                    }
                    
                }

                // Return the current URL which should be the room URL
                return clipboardText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create room: {ex.Message}");
                return null;
            }
        }

        private T? RunOnSTAThread<T>(Func<T> action)
        {
            T? result = default;
            var thread = new Thread(() =>
            {
                result = action();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        // Join a specific room by navigating to its URL
        public bool JoinRoom(string roomUrl, string nickname = "")
        {
            try
            {
                driver.Navigate().GoToUrl(roomUrl);

                // When you join, there might be the "invite friends" room popup
                try
                {
                    var closeButton = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"invite-modal\"]/div[2]/div[4]/div")));
                    closeButton.Click();
                }
                catch
                {
                    // might not appear, continue
                }

                // Wait until the room loads by checking for player container
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@id=\"player_container\"]")));

                // Enter nickname if prompted
                if (!string.IsNullOrEmpty(nickname))
                {
                    try
                    {
                        var userBtn = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"user-jdxcznjno73j1zpz-basic\"]/div/div[2]")));
                        userBtn.Click();

                        var nicknameInput = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"nickname-form-nickname\"]")));
                        nicknameInput.Clear();
                        nicknameInput.SendKeys(nickname);

                        var confirmButton = driver.FindElement(By.XPath("//*[@id=\"nickname-form\"]/div/button"));
                        confirmButton.Click();
                    }
                    catch
                    {
                        // Nickname prompt might not appear, continue
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to join room: {ex.Message}");
                return false;
            }
        }

        // Add a video link to the current room
        public bool AddVideo(string videoUrl)
        {
            try
            {
                // When you join, there might be the "invite friends" room popup
                try
                {
                    var closeButton = wait.Until(ExpectedConditions.ElementIsVisible(
                            By.XPath("//*[@id=\"invite-modal\"]/div[2]/div[4]/div")));
                    closeButton.Click();
                }
                catch
                {
                    // might not appear, continue
                }

                // Wait for the search input field to be visible
                var videoUrlInput = wait.Until(ExpectedConditions.ElementIsVisible(
                    By.XPath("//*[@id=\"search-bar-input\"]")));
                videoUrlInput.Clear();
                videoUrlInput.SendKeys(videoUrl);
                Thread.Sleep(500); // Small delay to ensure the input is registered
                videoUrlInput.SendKeys(Keys.Enter);

                // Wait for search results and click on the first result
                try
                {
                    var firstResult = wait.Until(ExpectedConditions.ElementToBeClickable(
                        By.XPath("//*[@id=\"w2g-search-results\"]/div[1]/div/div[1]/div")));
                    firstResult.Click();
                }
                catch
                {
                    // For direct URL input, there might not be search results
                    try
                    {
                        var addButton = wait.Until(ExpectedConditions.ElementToBeClickable(
                            By.XPath("//button[contains(text(), 'Add') or contains(@class, 'add-button')]")));
                        addButton.Click();
                    }
                    catch
                    {
                        // Some videos might be added directly without an add button
                    }
                }

                Thread.Sleep(1000);

                // Wait for the video to appear in the player
                wait.Until(ExpectedConditions.ElementExists(
                    By.XPath("//video | //iframe[contains(@src, 'youtube') or contains(@src, 'vimeo')]")));

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to add video: {ex.Message}");
                return false;
            }
        }


        // Get the currently playing video URL if available
        public string GetCurrentVideoUrl()
        {
            try
            {
                object result = js.ExecuteScript(
                    "const video = document.querySelector('video'); " +
                    "if(video && video.src) return video.src; " +
                    "const ytIframe = document.querySelector('iframe[src*=\"youtube\"]'); " +
                    "if(ytIframe) return ytIframe.src; " +
                    "return '';"
                );

                return result?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        // Send a chat message in the room
        public bool SendChatMessage(string message)
        {
            try
            {
                var openChatBtn = wait.Until(ExpectedConditions.ElementIsVisible(By.XPath("//*[@id=\"chat-float\"]/div[2]")));
                openChatBtn.Click();

                // Find the chat input field
                var chatInput = wait.Until(ExpectedConditions.ElementIsVisible(
                    By.XPath("//*[@id=\"w2g-chat-input\"]")));
                chatInput.Clear();
                chatInput.SendKeys(message);
                chatInput.SendKeys(Keys.Enter);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send chat message: {ex.Message}");
                return false;
            }
        }


        #region Frame
        public enum VideoState
        {
            Playing,
            Paused,
            Finished
        }

        // Send the play command
        public bool Play()
        {
            try
            {

                // Try to find a traditional play button
                // Switch to the iframe
                YoutubeVideoLoader_FrameSwitch();

                // If we found the pause icon, the video is playing
                var state = VideoState.Finished;

                // Find the icon element that has any of the target classes.
                var videoIcon = driver.FindElement(By.XPath("//*[@id=\"player_controls\"]/div[1]/i"));

                // Get the complete list of classes on that element.
                string iconClasses = videoIcon.GetAttribute("class");

                // Determine the video state based on the classes.
                if (iconClasses.Contains("icon-pause"))
                    state = VideoState.Playing;
                else if (iconClasses.Contains("icon-play"))
                    state = VideoState.Paused;
                else if (iconClasses.Contains("icon-arrows-cw"))
                    state = VideoState.Finished;

                switch (state)
                {
                    case VideoState.Playing:
                        //do nothing, it already is playing
                        break;
                    case VideoState.Paused:
                    case VideoState.Finished:
                    default:
                        // Then try to find the element
                        var playButton = wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//*[@id=\"player_controls\"]/div[1]")));
                        playButton.Click();
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to play video: {ex.Message}");
                return false;
            }
        }

        // Send the pause command
        public bool Pause()
        {
            try
            {
                // Try to find a traditional play button
                // Switch to the iframe
                YoutubeVideoLoader_FrameSwitch();

                // If we found the pause icon, the video is playing
                var state = VideoState.Finished;

                // Find the icon element that has any of the target classes.
                var videoIcon = driver.FindElement(By.XPath("//*[@id=\"player_controls\"]/div[1]/i"));

                // Get the complete list of classes on that element.
                string iconClasses = videoIcon.GetAttribute("class");

                // Determine the video state based on the classes.
                if (iconClasses.Contains("icon-pause"))
                    state = VideoState.Playing;
                else if (iconClasses.Contains("icon-play"))
                    state = VideoState.Paused;
                else if (iconClasses.Contains("icon-arrows-cw"))
                    state = VideoState.Finished;


                switch (state)
                {
                    case VideoState.Paused:
                    case VideoState.Finished:
                    default:
                        //do nothing, it already is paused
                        break;

                    case VideoState.Playing:
                        // Then try to find the element
                        var playButton = wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//*[@id=\"player_controls\"]/div[1]")));
                        playButton.Click();
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to pause video: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set the video to a specific time (in seconds)
        /// </summary>
        /// <param name="seconds">Time in seconds to seek to</param>
        /// <returns>True if operation was successful</returns>
        public bool SetVideoTime(double seconds)
        {
            try
            {
                // Switch to the iframe first
                YoutubeVideoLoader_FrameSwitch();

                // Wait for the progress bar to be clickable (this ensures it's ready for interaction)
                var shortWait = new WebDriverWait(driver, TimeSpan.FromSeconds(2));
                var progressBar = shortWait.Until(ExpectedConditions.ElementToBeClickable(By.Id("time_slider")));

                if (progressBar != null && progressBar.Displayed)
                {
                    int width = progressBar.Size.Width;
                    double percentage = Math.Min(1.0, Math.Max(0.0, seconds / GetVideoDuration()));
                    double xOffset = width * percentage;
                    int pos = (int)(xOffset - (width / 2));

                    // Chain the actions to move directly to the desired offset and click.
                    Actions actions = new Actions(driver);
                    YoutubeVideoLoader_FrameSwitch();
                    var clickableProgressBar = shortWait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//*[@id=\"time_slider\"]")));
                    actions.MoveToElement(clickableProgressBar, pos, 0)
                           .Click()
                           .Perform();

                    Console.WriteLine($"Clicked at position {xOffset}px ({percentage:P}) of progress bar");
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set video time: {ex.Message}");
                return false;
            }
            finally
            {

            }
        }
        // Get the current video time in seconds
        public double GetCurrentVideoTime()
        {
            try
            {
                // Switch to the iframe
                YoutubeVideoLoader_FrameSwitch();


                // Then try to find the element
                var timeSpan = wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//*[@id=\"time_display\"]/span[1]")));
                string result = timeSpan.Text;

                if (!string.IsNullOrEmpty(result))
                {
                    var timeParts = result.Split(':').Select(int.Parse).ToArray();
                    double totalSeconds = 0;

                    if (timeParts.Length == 3)
                    {
                        totalSeconds = timeParts[0] * 3600 + timeParts[1] * 60 + timeParts[2];
                    }
                    else if (timeParts.Length == 2)
                    {
                        totalSeconds = timeParts[0] * 60 + timeParts[1];
                    }

                    return totalSeconds;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        // Get the total video duration in seconds
        public double GetVideoDuration()
        {
            try
            {
                // Switch to the iframe
                YoutubeVideoLoader_FrameSwitch();
                

                // Then try to find the element
                var timeSpan = wait.Until(ExpectedConditions.ElementToBeClickable(By.XPath("//*[@id=\"time_display\"]/span[3]")));
                string result = timeSpan.Text;

                if (!string.IsNullOrEmpty(result))
                {
                    var timeParts = result.Split(':').Select(int.Parse).ToArray();
                    double totalSeconds = 0;

                    if (timeParts.Length == 3)
                    {
                        totalSeconds = timeParts[0] * 3600 + timeParts[1] * 60 + timeParts[2];
                    }
                    else if (timeParts.Length == 2)
                    {
                        totalSeconds = timeParts[0] * 60 + timeParts[1];
                    }

                    return totalSeconds;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        // Get the current playing status (true if playing, false if paused)
        public bool IsPlaying()
        {
            try
            {
                // Try to find a traditional play button
                // Switch to the iframe
                                    YoutubeVideoLoader_FrameSwitch();

                // If we found the pause icon, the video is playing
                var state = VideoState.Finished;

                // Find the icon element that has any of the target classes.
                var videoIcon = driver.FindElement(By.XPath("//*[@id=\"player_controls\"]/div[1]/i"));

                // Get the complete list of classes on that element.
                string iconClasses = videoIcon.GetAttribute("class");

                // Determine the video state based on the classes.
                if (iconClasses.Contains("icon-pause"))
                    state = VideoState.Playing;
                else if (iconClasses.Contains("icon-play"))
                    state = VideoState.Paused;
                else if (iconClasses.Contains("icon-arrows-cw"))
                    state = VideoState.Finished;

                return state == VideoState.Playing;
            }
            catch
            {
                return false;
            }
        }
        public void YoutubeVideoLoader_FrameSwitch()
        {


            // Check if we're already inside any frame.
            var frameElement = ((IJavaScriptExecutor)driver).ExecuteScript("return window.frameElement;");
            if (frameElement != null)
            {
                // Get the ID of the current frame element.
                var currentFrameId = ((IJavaScriptExecutor)driver)
                                       .ExecuteScript("return arguments[0].id;", frameElement) as string;
                if (currentFrameId != "w2g-npa-frame")
                {
                    // If we're in a different frame, switch back to default first then to our cached target frame.
                    driver.SwitchTo().DefaultContent();
                    driver.SwitchTo().Frame("w2g-npa-frame");
                }
                // Else: already in the target frame, so no action is needed.
            }
            else
            {
                // Not in any frame, so just switch to our cached target frame.
                driver.SwitchTo().Frame("w2g-npa-frame");
            }
        }
        #endregion


        // Clean up the driver
        public void Dispose()
        {
            driver?.Quit();
        }
    }
}
