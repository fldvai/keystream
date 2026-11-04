using System.IO;
using System.Net.Http;
using System.Windows;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

public partial class UpdateProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly string _downloadUrl;
    private readonly string _targetPath;

    public Exception? DownloadError { get; private set; }
    public bool DownloadCompleted { get; private set; }

    public UpdateProgressWindow(string downloadUrl, string targetPath)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        Title = Localizer.Get("S1554"); // "Tentang Keystream" or "Keystream"
        StatusText.Text = Localizer.Get("S1844"); // "Mengunduh pembaruan Keystream..."
        _downloadUrl = downloadUrl;
        _targetPath = targetPath;

        Loaded += async (_, _) => await RunDownloadAsync();
    }

    private async Task RunDownloadAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Keystream/1.0");

            using var response = await http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(_cts.Token);
            await using var fileStream = new FileStream(_targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cts.Token);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    double pct = (double)totalRead / totalBytes * 100.0;
                    DownloadBar.Value = pct;
                    PercentText.Text = $"{(int)pct}%";
                    BytesText.Text = $"{totalRead / 1024.0 / 1024.0:F1} MB / {totalBytes / 1024.0 / 1024.0:F1} MB";
                }
                else
                {
                    DownloadBar.IsIndeterminate = true;
                    BytesText.Text = $"{totalRead / 1024.0 / 1024.0:F1} MB";
                }
            }

            DownloadCompleted = true;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(_targetPath)) File.Delete(_targetPath); } catch { }
            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            DownloadError = ex;
            try { if (File.Exists(_targetPath)) File.Delete(_targetPath); } catch { }
            DialogResult = false;
            Close();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }
}

