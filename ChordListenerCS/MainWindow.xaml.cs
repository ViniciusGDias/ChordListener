using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HtmlAgilityPack;
using NAudio.Wave;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChordListenerCS
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture? _capture;
        private WaveFileWriter? _writer;
        private bool _isListening = false;
        private readonly HttpClient _httpClient = new HttpClient();
        private string _tempWavPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_capture.wav");

        public MainWindow()
        {
            InitializeComponent();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch(SearchInput.Text);
        }

        private async void SearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearch(SearchInput.Text);
            }
        }

        private async Task PerformSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            StatusText.Text = $"Searching: {query}...";
            StatusText.Foreground = Brushes.Cyan;
            
            try
            {
                var tabData = await ScrapeCifraClub(query);
                
                if (tabData != null)
                {
                    StatusText.Text = "Found!";
                    StatusText.Foreground = Brushes.SpringGreen;
                    
                    var parts = query.Split('-');
                    if (parts.Length > 1) {
                        TxtArtist.Text = parts[0].Trim();
                        TxtSongTitle.Text = parts[1].Trim();
                    } else {
                        TxtSongTitle.Text = query;
                        TxtArtist.Text = "Unknown Artist";
                    }

                    TxtTabContent.Text = tabData;
                    
                    // Show diagrams
                    ExtractAndShowChords(tabData);
                }
                else
                {
                    StatusText.Text = "Tab not found.";
                    StatusText.Foreground = Brushes.Red;
                    TxtTabContent.Text = "Could not find a tab for this song. Try being more specific (Artist - Song).";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error.";
                StatusText.Foreground = Brushes.Red;
                MessageBox.Show($"Error searching: {ex.Message}");
            }
        }

        private async Task<string> ScrapeCifraClub(string query)
        {
            try
            {
                // 1. DuckDuckGo Search (more lenient than Google)
                var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query + " cifra club")}";
                
                // Add headers to look like a real browser
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                var searchHtml = await _httpClient.GetStringAsync(searchUrl);
                
                var doc = new HtmlDocument();
                doc.LoadHtml(searchHtml);

                // DuckDuckGo result links
                var linkNode = doc.DocumentNode.SelectNodes("//a[@class='result__a']")
                    ?.FirstOrDefault(n => n.GetAttributeValue("href", "").Contains("cifraclub.com.br"));

                if (linkNode == null) return null;

                var link = linkNode.GetAttributeValue("href", "");
                
                // 2. Fetch Cifra Page
                var cifraHtml = await _httpClient.GetStringAsync(link);
                var cifraDoc = new HtmlDocument();
                cifraDoc.LoadHtml(cifraHtml);

                // CifraClub container usually has class 'cifra_cnt' or just inside a 'pre'
                var preNode = cifraDoc.DocumentNode.SelectSingleNode("//pre");
                if (preNode != null)
                {
                    return System.Net.WebUtility.HtmlDecode(preNode.InnerText);
                }
                
                // Fallback: Try to find div with class 'cifra'
                var divNode = cifraDoc.DocumentNode.SelectSingleNode("//div[@class='cifra']");
                if (divNode != null)
                {
                     return System.Net.WebUtility.HtmlDecode(divNode.InnerText);
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        // Duplicate fields removed
        private System.DateTime _lastVizUpdate = System.DateTime.MinValue;

        private void OnAudioCaptured(object? sender, WaveInEventArgs e)
        {
            // Only update UI every 100ms to allow button text countdown to refresh properly
            if ((System.DateTime.Now - _lastVizUpdate).TotalMilliseconds < 100) return;
            _lastVizUpdate = System.DateTime.Now;

            float max = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                var val = Math.Abs(sample / 32768f);
                if (val > max) max = val;
            }

            if (max > 0.01) 
            {
                Dispatcher.Invoke(() => {
                   StatusText.Text = $"Hearing music... (Vol: {(int)(max * 100)}%)";
                   StatusText.Foreground = Brushes.Yellow;
                });
            }
        }

        private void BtnListen_Click(object sender, RoutedEventArgs e)
        {
            if (!_isListening)
            {
                StartListening();
                BtnListen.Content = "⏳ Listening (15s)...";
                BtnListen.Background = Brushes.Orange;
                _isListening = true;
            }
            else
            {
                // Manual stop if needed, though we auto-stop
                StopListening();
            }
        }

        private System.IO.MemoryStream? _audioStream;

        private async void StartListening()
        {
            try
            {
                StatusText.Text = "Recording sample...";
                StatusText.Foreground = Brushes.Yellow;

                _capture = new WasapiLoopbackCapture();
                _audioStream = new System.IO.MemoryStream();

                _capture.DataAvailable += (s, a) =>
                {
                    // Buffer raw audio to memory
                    _audioStream.Write(a.Buffer, 0, a.BytesRecorded);
                    
                    // Simple viz
                    OnAudioCaptured(s, a);
                };

                _capture.RecordingStopped += async (s, a) =>
                {
                    _capture?.Dispose();
                    _capture = null;

                    // Convert and Save as 16-bit PCM (Shazam friendly)
                    if (_audioStream != null) {
                        SaveAsPcm16(_audioStream, _tempWavPath);
                        _audioStream.Dispose();
                    }

                    bool identified = false;

                    if (_isListening) 
                    {
                         identified = await IdentifySong(_tempWavPath);
                    }
                    
                    _isListening = false;
                    
                    Dispatcher.Invoke(() => {
                        BtnListen.Content = "🎙️ Auto-Listen";
                        BtnListen.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                        
                        if (!identified)
                        {
                            StatusText.Text = "Ready";
                            StatusText.Foreground = Brushes.Gray;
                        }
                    });
                };

                _capture.StartRecording();

                // Use DispatcherTimer for reliable UI countdown
                _recordingSecondsRemaining = 15;
                _recordingTimer = new System.Windows.Threading.DispatcherTimer();
                _recordingTimer.Interval = TimeSpan.FromSeconds(1);
                _recordingTimer.Tick += (s, args) =>
                {
                    _recordingSecondsRemaining--;
                    BtnListen.Content = $"⏳ Listening ({_recordingSecondsRemaining}s)...";
                    
                    if (_recordingSecondsRemaining <= 0)
                    {
                        _recordingTimer.Stop();
                        if (_isListening) StopListening();
                    }
                };
                _recordingTimer.Start();
                
                // Set initial text immediately
                BtnListen.Content = "⏳ Listening (15s)...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Audio Error: {ex.Message}");
                StopListening();
            }
        }

        // --- Chord Library ---
        private readonly System.Collections.Generic.Dictionary<string, string> _chordDiagrams = new()
        {
            // Basic Major
            {"C",  "C Major\n x32010\n ||||O|\n ||O|||\n |O||||"},
            {"D",  "D Major\n xx0232\n ||||||\n |||O|O\n ||||O|"},
            {"E",  "E Major\n 022100\n |||O||\n |OO|||\n ||||||"},
            {"F",  "F Major\n 133211\n OOOOOO (1)\n |||O||\n |OO|||"},
            {"G",  "G Major\n 320003\n ||||||\n |O||||\n O||||O"},
            {"A",  "A Major\n x02220\n ||||||\n ||OOO|\n ||||||"},
            {"B",  "B Major\n x24442\n |O...O (2)\n ||||||\n ||OOO|"},
            
            // Basic Minor
            {"Cm",  "C Minor\n x35543\n |O...O (3)\n ||||O|\n ||OO||"},
            {"Dm",  "D Minor\n xx0231\n |||||O\n |||O||\n ||||O|"},
            {"Em",  "E Minor\n 022000\n ||||||\n |OO|||\n ||||||"},
            {"Fm",  "F Minor\n 133111\n OOOOOO (1)\n ||||||\n |OO|||"},
            {"Gm",  "G Minor\n 355333\n OOOOOO (3)\n ||||||\n |OO|||"},
            {"Am",  "A Minor\n x02210\n ||||O|\n ||OO||\n ||||||"},
            {"Bm",  "B Minor\n x24432\n |O...O (2)\n ||||O|\n ||OO||"},

            // Sharps / Flats map to closest
            {"C#m", "C# Minor\n x46654\n |O...O (4)\n ||||O|\n ||OO||"},
            {"F#m", "F# Minor\n 244222\n OOOOOO (2)\n ||||||\n |OO|||"},
            {"C#",  "C# Major\n x46664\n |O...O (4)\n ||||||\n ||OOO|"},
        };

        private void ExtractAndShowChords(string fullText)
        {
            // Simple regex to find chords roughly (Words that look like chords)
            // Matches: [Space] [Letter A-G] [Optional #/b] [Optional m/maj/dim/7] [Space]
            var candidates = new System.Collections.Generic.HashSet<string>();
            var regex = new Regex(@"\b[A-G][#b]?(m|min|maj|dim|aug|sus|7|9)*\b");
            
            foreach (Match match in regex.Matches(fullText))
            {
                candidates.Add(match.Value);
            }

            // Build display string
            var display = new System.Text.StringBuilder();
            
            foreach(var chord in candidates) 
            {
                // Normalize for dictionary check (simplistic)
                var key = chord; 
                // Try exact match
                if (_chordDiagrams.ContainsKey(key)) {
                    display.AppendLine(_chordDiagrams[key]);
                    display.AppendLine(new string('-', 15));
                }
                // Try stripping extension (e.g. A7 -> A) if simpler not found
                else if (key.Length > 1 && _chordDiagrams.ContainsKey(key.Substring(0, key.Length-1))) {
                    display.AppendLine($"{key} (Base)\n" + _chordDiagrams[key.Substring(0, key.Length-1)].Split('\n')[2]); // Just header + trick
                     display.AppendLine(new string('-', 15));
                }
            }

            if (display.Length == 0) 
            {
                TxtChordsReference.Text = "No standard chords detected to visualize.";
            } else {
                TxtChordsReference.Text = display.ToString();
            }
        }

        private void StopListening()
        {
            _recordingTimer?.Stop();
            _capture?.StopRecording();
            // Dispose happens in event handler
        }

        private System.Windows.Threading.DispatcherTimer? _recordingTimer;
        private int _recordingSecondsRemaining;

        private void SaveAsPcm16(System.IO.MemoryStream rawAudioKey, string outputFile)
        {
            rawAudioKey.Position = 0;
            // Original format (usually 32-bit float IEEE)
            using (var reader = new RawSourceWaveStream(rawAudioKey, new WasapiLoopbackCapture().WaveFormat))
            {
                // Target format: 16-bit PCM, 44.1kHz
                var targetFormat = new WaveFormat(44100, 16, 2); 
                using (var converter = new MediaFoundationResampler(reader, targetFormat))
                {
                    converter.ResamplerQuality = 60; // Fair quality
                    WaveFileWriter.CreateWaveFile(outputFile, converter);
                }
            }
        }

        private async Task<bool> IdentifySong(string filePath)
        {
            StatusText.Text = "Identifying song...";
            StatusText.Foreground = Brushes.Cyan;

            var result = await RunPythonRecognizer(filePath);
            
            if (result.StartsWith("AI_CHORDS:"))
            {
                 var chords = result.Replace("AI_CHORDS:", "").Trim();
                 StatusText.Text = "AI Ear Active 👂";
                 StatusText.Foreground = Brushes.Magenta;
                 
                 TxtSongTitle.Text = "AI Transcription";
                 TxtArtist.Text = "Real-time Analysis";
                 
                 TxtTabContent.Text = $"I couldn't identify the song, but I heard these chords:\n\n{chords}\n\n(Note: This is an algorithmic estimation)";
                 
                 // Show diagrams for these chords too
                 ExtractAndShowChords(chords);
                 
                 return true;
            }

            if (string.IsNullOrWhiteSpace(result) || result.Contains("Error") || result.Contains("Not Found"))
            {
                 StatusText.Text = "Not Found / Failed";
                 StatusText.Foreground = Brushes.Red;
                 
                 // Show EXACTLY what came back
                 MessageBox.Show($"Debug Result: '{result}'", "Identification Result");

                 return false;
            }
            else
            {
                // Success (Song ID)
                // Result should be "Artist - Title"
                StatusText.Text = $"Detected: {result}";
                StatusText.Foreground = Brushes.SpringGreen;
                
                await PerformSearch(result.Trim());
                return true;
            }
        }

        private Task<string> RunPythonRecognizer(string wavPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recognizer.py");
                    
                    // Copy script to output dir if it's not there (development convenience)
                    // In a real scenario, we'd ensure it's there during build.
                    if (!System.IO.File.Exists(scriptPath)) 
                    {
                        // Try looking in source dir? For now assume it's in CWD or build output.
                        // Let's assume CWD has it if run via dotnet run
                        if (System.IO.File.Exists("recognizer.py"))
                             scriptPath = System.IO.Path.GetFullPath("recognizer.py");
                    }

                    var start = new System.Diagnostics.ProcessStartInfo();
                    start.FileName = "py"; // Use Python Launcher
                    start.Arguments = $"-3.10 \"{scriptPath}\" \"{wavPath}\""; // Force Python 3.10
                    start.UseShellExecute = false;
                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                    start.CreateNoWindow = true; // No black window

                    using (var process = System.Diagnostics.Process.Start(start))
                    {
                        if (process == null) return "Error: Failed to start python.";

                        using (var reader = process.StandardOutput)
                        using (var errReader = process.StandardError)
                        {
                            var result = reader.ReadToEnd();
                            var err = errReader.ReadToEnd();
                            
                            process.WaitForExit();
                            
                            if (!string.IsNullOrWhiteSpace(err) && string.IsNullOrWhiteSpace(result))
                            {
                                return "Error: " + err;
                            }
                            
                            if (!string.IsNullOrWhiteSpace(result)) 
                                return result.Trim();
                                
                            return "Error: " + err;
                        }
                    }
                }
                catch (Exception ex)
                {
                    return "Error: " + ex.Message;
                }
            });
        }

        private System.Windows.Threading.DispatcherTimer? _scrollTimer;

        private void ChkAutoScroll_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (ChkAutoScroll.IsChecked == true)
            {
                StartScrollTimer();
            }
            else
            {
                _scrollTimer?.Stop();
            }
        }

        private void SldScrollSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_scrollTimer != null && _scrollTimer.IsEnabled)
            {
                // Restart with new speed
                _scrollTimer.Stop();
                StartScrollTimer();
            }
        }

        private void StartScrollTimer()
        {
            if (_scrollTimer == null)
            {
                _scrollTimer = new System.Windows.Threading.DispatcherTimer();
                _scrollTimer.Tick += (s, args) =>
                {
                    TabScrollViewer.ScrollToVerticalOffset(TabScrollViewer.VerticalOffset + 1);
                };
            }
            
            // Calculate interval based on slider: 
            // Min (1) -> Slow (e.g. 200ms)
            // Max (20) -> Fast (e.g. 10ms)
            // Formula: Interval = 200 - (Value * 9) approx
            var val = SldScrollSpeed.Value;
            var ms = Math.Max(10, 210 - (val * 10)); 
            
            _scrollTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _scrollTimer.Start();
        }
    }
}