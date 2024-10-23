using System;
using System.Drawing; // For Icon
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms; // Alias for System.Windows.Forms
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using Tesseract;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Threading;

namespace DubSense
{
    public partial class MainWindow : Window
    {
        private static Mutex? _mutex = null;
        private DispatcherTimer captureTimer = new DispatcherTimer();
        private DateTime lastWebhookSent = DateTime.MinValue;
        private readonly TimeSpan webhookCooldown = TimeSpan.FromSeconds(15);
        private TesseractEngine ocrEngine = null!;
        private readonly object ocrLock = new object(); // Lock for thread safety
        private DispatcherTimer processCheckTimer = new DispatcherTimer();
        // NotifyIcon for system tray
        private Forms.NotifyIcon _notifyIcon = null!;


        const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private void SetWindowThemeAttribute()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            bool isDarkMode = true;
            int attribute = DWMWA_USE_IMMERSIVE_DARK_MODE;

            // For Windows versions before 1903 (build 18362)
            if (Environment.OSVersion.Version.Build < 18362)
            {
                attribute = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            }

            int useImmersiveDarkMode = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, attribute, ref useImmersiveDarkMode, Marshal.SizeOf(typeof(int)));
        }

        // Flag to determine if the app is closing
        private bool _isExit = false;

        public MainWindow()
        {
            InitializeComponent();
            // Initialize the mutex
            const string mutexName = "DubSenseAppMutex";
            _mutex = new Mutex(true, mutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is already running
                System.Windows.MessageBox.Show("Another instance of DubSense is already running.", "Instance Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            InitializeCaptureTimer();
            LoadSettings();
            InitializeNotifyIcon();
            InitializeProcessCheckTimer();

            // Handle window state changes
            this.StateChanged += MainWindow_StateChanged;

            // Handle window closing
            this.Closing += MainWindow_Closing;

            // Handle AutoMonitorCheckBox state changes
            AutoMonitorCheckBox.Checked += AutoMonitorCheckBox_CheckedChanged;
            AutoMonitorCheckBox.Unchecked += AutoMonitorCheckBox_CheckedChanged;

            // Load AutoMonitorCheckBox state
            AutoMonitorCheckBox.IsChecked = Properties.Settings.Default.AutoMonitor;

            // Load ViewCaptureCheckBox state
            ViewCaptureCheckBox.IsChecked = Properties.Settings.Default.ViewCapture;

            // Load Webhook URL
            WebhookUrlTextBox.Text = Properties.Settings.Default.WebhookUrl;

            // Add event handler for WebhookUrlTextBox text change
            WebhookUrlTextBox.TextChanged += WebhookUrlTextBox_TextChanged;

            // Check auto-start status
            CheckAutoStartStatus();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetWindowThemeAttribute();
        }

        private void WebhookUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Properties.Settings.Default.WebhookUrl = WebhookUrlTextBox.Text;
            Properties.Settings.Default.Save();
        }
        private void CheckAutoStartStatus()
        {
            try
            {
                string appName = "DubSense";
                using (RegistryKey? rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (rk != null && rk.GetValue(appName) != null)
                    {
                        AutoStartCheckBox.IsChecked = true; // Auto-start is enabled
                    }
                    else
                    {
                        AutoStartCheckBox.IsChecked = false; // Auto-start is disabled
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error checking auto-start status: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void MonitorToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
            MonitorToggleButton.Content = "Stop";
        }

        private void MonitorToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
            MonitorToggleButton.Content = "Start";
        }
        private void SetAutoStart(bool enable)
        {
            try
            {
                string appName = "DubSense";
                string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string appExePath = Path.ChangeExtension(appPath, ".exe");

                using (RegistryKey? rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk != null)
                    {
                        if (enable)
                        {
                            // Add the application to autostart
                            rk.SetValue(appName, appExePath);
                        }
                        else
                        {
                            // Remove the application from autostart
                            if (rk.GetValue(appName) != null)
                            {
                                rk.DeleteValue(appName, false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error setting auto-start: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Open the settings popup
            SettingsPopup.IsOpen = true;
        }

        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the settings popup
            SettingsPopup.IsOpen = false;
        }

        private void ViewCaptureCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ViewCapture = true;
            Properties.Settings.Default.Save();
        }

        private void ViewCaptureCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ViewCapture = false;
            Properties.Settings.Default.Save();
        }
        private void AutoStartCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetAutoStart(true); // Enable auto-start with Windows
        }

        private void AutoStartCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetAutoStart(false); // Disable auto-start with Windows
        }
        private void AutoMonitorCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isChecked = AutoMonitorCheckBox.IsChecked == true;
            MonitorToggleButton.IsEnabled = !isChecked;

            // Save the AutoMonitorCheckBox state
            Properties.Settings.Default.AutoMonitor = isChecked;
            Properties.Settings.Default.Save();

            // Update status text
            if (isChecked)
            {
                UpdateStatus("Waiting for CoD to start...");
            }
            else
            {
                UpdateStatus("Idle");
            }
        }
        private void InitializeProcessCheckTimer()
        {
            processCheckTimer.Interval = TimeSpan.FromSeconds(5); // Check every 5 seconds
            processCheckTimer.Tick += ProcessCheckTimer_Tick;
            processCheckTimer.Start();
        }

        private void ProcessCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (AutoMonitorCheckBox.IsChecked == true)
            {
                bool isCodRunning = Process.GetProcessesByName("cod23-cod").Length > 0 || Process.GetProcessesByName("cod").Length > 0;

                if (isCodRunning && !captureTimer.IsEnabled)
                {
                    StartMonitoring();
                }
                else if (!isCodRunning && captureTimer.IsEnabled)
                {
                    StopMonitoring();
                }
            }
        }
        private void UpdateStatus(string status)
        {
            StatusTextBlock.Text = status;
        }
        private void StartMonitoring()
        {
            SaveSettings();
            UpdateStatus("Monitoring");

            if (string.IsNullOrEmpty(WebhookUrlTextBox.Text.Trim()))
            {
                System.Windows.MessageBox.Show("Please enter a valid webhook URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string tessDataPath = System.IO.Path.Combine(baseDir, "tessdata");

                ocrEngine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                ocrEngine.SetVariable("tessedit_char_whitelist", "CTO");
                ocrEngine.DefaultPageSegMode = PageSegMode.SingleWord;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing OCR engine: {ex.Message}\n{ex.InnerException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            captureTimer.Start();
            WebhookUrlTextBox.IsEnabled = false;
        }

        private void StopMonitoring()
        {
            captureTimer.Stop();
            UpdateStatus("Idle");

            if (ocrEngine != null)
            {
                ocrEngine.Dispose();
                ocrEngine = null!;
            }

            WebhookUrlTextBox.IsEnabled = true;
        }

        // Event handler for Start Monitoring button
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        // Event handler for Stop Monitoring button
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }
        // Event handler for Stop Monitoring button
        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            await SendWebhookAsync();
        }
        private void InitializeCaptureTimer()
        {
            captureTimer.Interval = TimeSpan.FromMilliseconds(2500); // 2.5 seconds
            captureTimer.Tick += CaptureTimer_Tick;
            // Do not start the timer here; it will be started when the Start button is clicked
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.MouseClick += (s, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };

            // Set the custom icon
            try
            {
                // Construct the path to the icon file
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/dubsense.ico");

                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                    this.Icon = new BitmapImage(new Uri(iconPath)); // Set the window icon
                }
                else
                {
                    // Fallback to a default system icon if custom icon is not found
                    _notifyIcon.Icon = SystemIcons.Application;
                    this.Icon = Imaging.CreateBitmapSourceFromHIcon(
                        SystemIcons.Application.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    ); // Set the window icon
                    System.Windows.MessageBox.Show("Custom icon not found. Using default icon.", "Icon Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur while loading the icon
                _notifyIcon.Icon = SystemIcons.Application;
                this.Icon = Imaging.CreateBitmapSourceFromHIcon(
                    SystemIcons.Application.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions()
                ); // Set the window icon
                System.Windows.MessageBox.Show($"Failed to load custom icon. Using default icon.\nError: {ex.Message}", "Icon Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _notifyIcon.Visible = true; // Ensure the tray icon is always visible

            // Set tooltip
            _notifyIcon.Text = "DubSense";

            // Add a context menu to the tray icon
            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Restore", null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private async void CaptureTimer_Tick(object? sender, EventArgs e)
        {
            Bitmap capturedBitmap = CaptureScreenPortion();
            CapturedImage.Source = ConvertBitmapToImageSource(capturedBitmap);

            bool isCTODetected = await CheckForCTOAsync(capturedBitmap);

            if (isCTODetected && (DateTime.Now - lastWebhookSent) > webhookCooldown)
            {
                lastWebhookSent = DateTime.Now;
                await SendWebhookAsync();

                // Show notification
                _notifyIcon.ShowBalloonTip(1000, "DubSense", "Victory detected!", Forms.ToolTipIcon.None);
            }

            capturedBitmap.Dispose();
        }

        private Bitmap CaptureScreenPortion()
        {
            // Get screen dimensions
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // Calculate capture area
            double captureHeight = screenHeight * 0.22;
            double aspectRatio = 7.0 / 3.0;
            double captureWidth = captureHeight * aspectRatio;
            double offsetX = (screenWidth - captureWidth) / 2 - (screenHeight * 0.06);
            double offsetY = (screenHeight - captureHeight) / 2;

            // Define the capture rectangle
            System.Drawing.Rectangle captureRect = new System.Drawing.Rectangle(
                (int)offsetX,
                (int)offsetY,
                (int)captureWidth,
                (int)captureHeight);

            // Capture the screen portion
            Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(captureRect.Location, System.Drawing.Point.Empty, captureRect.Size);
            }

            // Convert to grayscale
            Bitmap grayBitmap = ConvertToGrayscale(bitmap);
            bitmap.Dispose();

            // Optionally resize the image (reduce size by half)
            Bitmap resizedBitmap = ResizeBitmap(grayBitmap, new System.Drawing.Size(grayBitmap.Width / 2, grayBitmap.Height / 2));
            grayBitmap.Dispose();

            return resizedBitmap;
        }

        private Bitmap ConvertToGrayscale(Bitmap source)
        {
            Bitmap grayBitmap = new Bitmap(source.Width, source.Height);

            using (Graphics g = Graphics.FromImage(grayBitmap))
            {
                System.Drawing.Imaging.ColorMatrix colorMatrix = new System.Drawing.Imaging.ColorMatrix(
                    new float[][]
                    {
                            new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                            new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                            new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {0, 0, 0, 0, 1}
                    });

                System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return grayBitmap;
        }

        private Bitmap ResizeBitmap(Bitmap source, System.Drawing.Size newSize)
        {
            Bitmap resizedBitmap = new Bitmap(newSize.Width, newSize.Height);

            using (Graphics g = Graphics.FromImage(resizedBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newSize.Width, newSize.Height);
            }

            return resizedBitmap;
        }

        private async Task<bool> CheckForCTOAsync(Bitmap bitmap)
        {
            return await Task.Run(() =>
            {
                lock (ocrLock)
                {
                    try
                    {
                        if (ocrEngine == null)
                            throw new InvalidOperationException("OCR engine is not initialized.");

                        using (var pix = BitmapToPix(bitmap))
                        {
                            // Process the image using the OCR engine
                            using (var page = ocrEngine.Process(pix))
                            {
                                // Get the recognized text
                                string text = page.GetText().Trim();

                                // Check if the text contains "CTO"
                                return text.Contains("CTO", StringComparison.Ordinal);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"OCR Error: {ex.Message}");
                        return false;
                    }
                }
            });
        }

        private Pix BitmapToPix(Bitmap bitmap)
        {
            // Save the bitmap to a MemoryStream
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                stream.Position = 0;

                // Load the Pix object from the MemoryStream
                return Pix.LoadFromMemory(stream.ToArray());
            }
        }

        private async Task SendWebhookAsync()
        {
            string webhookUrl = WebhookUrlTextBox.Text.Trim();

            if (string.IsNullOrEmpty(webhookUrl))
            {
                System.Windows.MessageBox.Show("Webhook URL is not set. Please enter a valid URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            using (HttpClient client = new HttpClient())
            {
                var content = new StringContent("{\"message\": \"Victory detected\"}", System.Text.Encoding.UTF8, "application/json");
                try
                {
                    await client.PostAsync(webhookUrl, content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Webhook Error: {ex.Message}");
                }
            }
        }

        private BitmapImage ConvertBitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;
            }
        }

        // Event handler for Start Monitoring button


        // Load settings when the application starts
        private void LoadSettings()
        {
            WebhookUrlTextBox.Text = Properties.Settings.Default.WebhookUrl;
            AutoMonitorCheckBox.IsChecked = Properties.Settings.Default.AutoMonitor;
            ViewCaptureCheckBox.IsChecked = Properties.Settings.Default.ViewCapture;

            // Update status text if AutoMonitorCheckBox is checked
            if (AutoMonitorCheckBox.IsChecked == true)
            {
                UpdateStatus("Waiting for CoD to start...");
            }
            else
            {
                UpdateStatus("Idle");
            }
        }

        // Save settings when the webhook URL changes or monitoring starts
        private void SaveSettings()
        {
            Properties.Settings.Default.WebhookUrl = WebhookUrlTextBox.Text.Trim();
            Properties.Settings.Default.AutoMonitor = AutoMonitorCheckBox.IsChecked == true;
            Properties.Settings.Default.Save();
        }

        // Handle window state changes to minimize to tray
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                Hide();
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(1000, "DubSense", "Application minimized to tray.", Forms.ToolTipIcon.None);
            }
        }

        // Show the main window and hide the tray icon
        private void ShowMainWindow()
        {
            Show();
            this.WindowState = WindowState.Normal;
            // Keep the tray icon visible even when the main window is shown
            _notifyIcon.Visible = true;
        }

        // Exit the application gracefully
        private void ExitApplication()
        {
            _isExit = true;
            _notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown(); // Fully qualify the Application class
        }

        // Handle window closing event
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit)
            {
                e.Cancel = true;
                this.WindowState = WindowState.Minimized;
            }
        }
    }
  
}