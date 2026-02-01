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
using System.Collections.ObjectModel;

namespace ChordListenerCS
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture? _capture;
        private WaveFileWriter? _writer;
        private bool _isListening = false;
        private bool _isContinuousChecking = false;
        private readonly HttpClient _httpClient = new HttpClient();
        private string _tempWavPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_capture.wav");

        public MainWindow()
        {
            InitializeComponent();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            LstHistory.ItemsSource = _history;
        }

        public class HistoryItem
        {
            public string DisplayName { get; set; } = "";
            public string Content { get; set; } = ""; // Tab or Chords
            public string Artist { get; set; } = "";
            public string Title { get; set; } = "";
            public bool IsAI { get; set; }
            
            // For Tooltip
            public string FullText => $"{DisplayName}\n{(IsAI ? "AI Detected" : "CifraClub")}";
        }

        private ObservableCollection<HistoryItem> _history = new ObservableCollection<HistoryItem>();

        private void LstHistory_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LstHistory.SelectedItem is HistoryItem item)
            {
                TxtArtist.Text = item.Artist;
                TxtSongTitle.Text = item.Title;
                TxtTabContent.Text = item.Content;
                
                // Show chords
                ExtractAndShowChords(item.Content, false);
                
                StatusText.Text = "History Loaded";
                StatusText.Foreground = Brushes.White;
            }
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
                    
                    // Add to History
                    AddToHistory($"{TxtArtist.Text} - {TxtSongTitle.Text}", tabData, TxtArtist.Text, TxtSongTitle.Text, false);

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
            if (_isContinuousChecking) {
                StopContinuousAiListening();
                return;
            }

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

        private void BtnForceAI_Click(object sender, RoutedEventArgs e)
        {
             if (_isContinuousChecking) {
                StopContinuousAiListening();
            }
            else {
                if (_isListening) StopListening();
                StartContinuousAiListening();
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
                    if (_audioStream != null)
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
                        // Only reset UI if we didn't switch to Continuous Mode
                        if (!_isContinuousChecking)
                        {
                            BtnListen.Content = "🎙️ Auto-Listen";
                            BtnListen.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                            
                            if (!identified)
                            {
                                StatusText.Text = "Ready";
                                StatusText.Foreground = Brushes.Gray;
                            }
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

        // Dictionary: Chord Name -> Pattern (e.g. x32010 for C)
        private readonly System.Collections.Generic.Dictionary<string, string> _chordDiagrams = new()
        {
            // Major
            {"C",  "x32010"}, {"D",  "xx0232"}, {"E",  "022100"},
            {"F",  "133211"}, {"G",  "320003"}, {"A",  "x02220"}, {"B",  "x24442"},
            
            // Minor
            {"Cm", "x35543"}, {"Dm", "xx0231"}, {"Em", "022000"},
            {"Fm", "133111"}, {"Gm", "355333"}, {"Am", "x02210"}, {"Bm", "x24432"},

            // Sharp/Flat Majors & Minors
            {"C#", "x46664"}, {"D#", "x68886"}, {"F#", "244322"}, {"G#", "466544"}, {"A#", "x13331"},
            {"C#m","x46654"}, {"D#m","x68876"}, {"F#m","244222"}, {"G#m","466444"}, {"A#m","x13321"},
            
            // 7ths (Dominant)
            {"C7", "x32310"}, {"D7", "xx0212"}, {"E7", "020100"}, {"G7", "320001"}, {"A7", "x02020"}, {"B7", "x21202"},
            
            // Major 7 (7M / maj7)
            {"C7M", "x32000"}, {"Cmaj7", "x32000"}, 
            {"D7M", "xx0222"}, {"Dmaj7", "xx0222"},
            {"E7M", "021100"}, {"Emaj7", "021100"},
            {"F7M", "xx3210"}, {"Fmaj7", "xx3210"},
            {"G7M", "320002"}, {"Gmaj7", "320002"},
            {"A7M", "x02120"}, {"Amaj7", "x02120"},

            // Minor 7 (m7)
            {"Cm7", "x35343"}, {"Dm7", "xx0211"}, {"Em7", "022030"},
            {"Fm7", "131111"}, {"Gm7", "353333"}, {"Am7", "x02010"}, {"Bm7", "x20202"},
            
            // Sus4 (4)
            {"C4", "x33010"},  {"Csus4", "x33010"},
            {"D4", "xx0233"},  {"Dsus4", "xx0233"},
            {"E4", "022200"},  {"Esus4", "022200"},
            {"F4", "133311"},  {"Fsus4", "133311"},
            {"G4", "3x0013"},  {"Gsus4", "3x0013"},
            {"A4", "x02230"},  {"Asus4", "x02230"},
            {"B4", "x24452"},  {"Bsus4", "x24452"},

            // 9 (Add9 or Dominant 9)
            {"C9", "x32030"},  {"Cadd9", "x32030"},
            {"D9", "xx0230"},  {"Dadd9", "xx0230"},
            {"E9", "022102"}, 
            {"G9", "3x0203"},  {"Gadd9", "320003"}, // Variation
            {"A9", "x02200"},  {"Aadd9", "x02200"},
        };
        
        public ObservableCollection<ChordControl> DisplayedChords { get; } = new ObservableCollection<ChordControl>();

        private void ExtractAndShowChords(string fullText, bool append = false)
        {
            if (!append) DisplayedChords.Clear();
            else {
                // If appending, clear if too many (keep last 5-6 chords to be relevant)
                if (DisplayedChords.Count > 10) {
                     // Remove chunks from start 
                     // ObservableCollection doesn't support RemoveRange, so loop
                     while(DisplayedChords.Count > 5) DisplayedChords.RemoveAt(0);
                }
            }

            // Expanded Regex: Matches Chords with extensions (4, 9, 13, sus, dim) and Slash Chords (/F#)
            // Examples: G4, C/G, Am7(9), Bb
            var candidates = new System.Collections.Generic.HashSet<string>();
            var regex = new Regex(@"\b[A-G][#b]?(\w|\d|\+|°)*(\/[A-G][#b]?)?\b");
            
            foreach (Match match in regex.Matches(fullText))
            {
                // Filter out non-chords like "Intro" or "Chorus" if they accidentally match
                // Our regex is broad, so we check if key starts with valid root
                if (Regex.IsMatch(match.Value, @"^[A-G]")) 
                    candidates.Add(match.Value);
            }
            
            // Sort chords naturally (e.g. C, G, Am, F)
            foreach(var chord in candidates.OrderBy(c => c)) 
            {
                string? pattern = null;
                string displayKey = chord;
                string searchKey = chord;

                // 1. Exact Match
                if (_chordDiagrams.ContainsKey(searchKey)) {
                    pattern = _chordDiagrams[searchKey];
                }
                // 2. Handle simple "4" as "sus4" if not found (e.g. G4 -> try Gsus4)
                else if (searchKey.EndsWith("4")) {
                     var altKey = searchKey.Replace("4", "sus4");
                     if (_chordDiagrams.ContainsKey(altKey)) pattern = _chordDiagrams[altKey];
                     else {
                         // Try base chord?
                         altKey = searchKey.TrimEnd('4');
                         if (_chordDiagrams.ContainsKey(altKey)) {
                             pattern = _chordDiagrams[altKey];
                             displayKey += " (Base)";
                         }
                     }
                }
                // 3. Handle Slash Chords (e.g. D/F#) -> Show D
                else if (searchKey.Contains("/")) {
                    var parts = searchKey.Split('/');
                    var baseChord = parts[0];
                    if (_chordDiagrams.ContainsKey(baseChord)) {
                        pattern = _chordDiagrams[baseChord];
                        // displayKey remains full name (D/F#) but shows D diagram
                    }
                    else if (_chordDiagrams.TryGetValue(baseChord + "m", out var mPattern)) { // safety
                         pattern = mPattern; // unlikely
                    }
                }
                
                // 4. Fallback: Strip sophisticated extensions (7M, 9, 11, etc) to find base triad
                if (pattern == null) {
                    // Try removing numbers and fancy suffixes
                    var baseChord = Regex.Match(searchKey, @"^[A-G][#b]?m?").Value;
                    if (!string.IsNullOrEmpty(baseChord) && _chordDiagrams.ContainsKey(baseChord)) {
                         pattern = _chordDiagrams[baseChord];
                         // Indicate it's an approximation
                         // displayKey += "*"; 
                    }
                }

                if (pattern != null)
                {
                    var ctrl = new ChordControl();
                    ctrl.SetChord(displayKey, pattern);
                    DisplayedChords.Add(ctrl);
                }
            }
        }

        private void StopListening()
        {
            _recordingTimer?.Stop();
            // In normal mode, we stop recording. In continuous, we keep it running but handle differently?
            // Actually, continuous mode manages its own capture life-cycle or piggybacks.
            // Let's standard stop.
            _capture?.StopRecording();
        }

        private System.Windows.Threading.DispatcherTimer? _recordingTimer;
        private System.Windows.Threading.DispatcherTimer? _continuousTimer;
        private int _recordingSecondsRemaining;

        private void StartContinuousAiListening()
        {
            if (_isContinuousChecking) return;
            _isContinuousChecking = true;

            // UI
            BtnListen.Content = "🛑 Stop AI Stream";
            BtnListen.Background = Brushes.Crimson;
            StatusText.Text = "📡 Continuous AI Listening...";
            StatusText.Foreground = Brushes.Magenta;
            TxtTabContent.Text = "--- Live Chord Stream ---\n(Listening for changes...)\n";
            DisplayedChords.Clear();

            // Ensure we are recording
            if (_capture == null)
            {
                _capture = new WasapiLoopbackCapture();
                _audioStream = new System.IO.MemoryStream();
                
                 _capture.DataAvailable += (s, a) =>
                {
                    if (_audioStream != null) _audioStream.Write(a.Buffer, 0, a.BytesRecorded);
                    OnAudioCaptured(s, a);
                };
                _capture.StartRecording();
            }
            else
            {
                // Already recording, just reset stream for fresh chunk
                 if (_audioStream != null) { 
                     _audioStream.SetLength(0); 
                 } else {
                     _audioStream = new System.IO.MemoryStream();
                 }
            }

            // Start Chunk Timer (e.g. every 5 seconds)
            _continuousTimer = new System.Windows.Threading.DispatcherTimer();
            _continuousTimer.Interval = TimeSpan.FromSeconds(5);
            _continuousTimer.Tick += async (s, args) => await ProcessContinuousChunk();
            _continuousTimer.Start();
        }

        private void StopContinuousAiListening()
        {
            _isContinuousChecking = false;
            _continuousTimer?.Stop();
            StopListening(); // Stops WASAPI

            BtnListen.Content = "🎙️ Auto-Listen";
            BtnListen.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            StatusText.Text = "Stream Stopped";
            StatusText.Foreground = Brushes.Gray;
        }

        private async Task ProcessContinuousChunk()
        {
            if (_capture == null || _audioStream == null) return;

            // 1. Snapshot current audio
            var chunkPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_live.wav");
            long position = _audioStream.Position;
            
            // If empty, skip
            if (position < 1000) return;

            // Copy to buffer to save
            byte[] data = _audioStream.ToArray();
            
            // Clear main stream for next chunk
            _audioStream.SetLength(0);

            // Save async
            await Task.Run(() => {
                using (var mem = new System.IO.MemoryStream(data))
                {
                    SaveAsPcm16(mem, chunkPath);
                }
            });

            // 2. Run Python (No Shazam, Pure Chords)
            var result = await RunPythonRecognizer(chunkPath, true); // true = --no-shazam

            // 3. Update UI
            if (result.StartsWith("AI_CHORDS:"))
            {
                var chords = result.Replace("AI_CHORDS:", "").Trim();
                if (string.IsNullOrWhiteSpace(chords)) return;

                // Append text
                TxtTabContent.Text += $"\n[{DateTime.Now:HH:mm:ss}] {chords}";
                TabScrollViewer.ScrollToBottom();

                // Append Visuals (Optional: Clear old ones? Or keep adding? Let's keep adding but limit to 8)
                // We'll use a specific version of ExtractAndShowChords that appends
                ExtractAndShowChords(chords, append: true);
            }
        }

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
                 
                 // If this was a one-off check and failed, we enter Continuous Mode
                 if (!_isContinuousChecking) 
                 {
                     StatusText.Text = "Song not found. Switching to AI Stream...";
                     StartContinuousAiListening();
                     return true; // prevent standard reset
                 }

                 // If we were already in continuous mode (shouldn't happen here usually), just update
                 return true;
            }

            if (string.IsNullOrWhiteSpace(result) || result.Contains("Error") || result.Contains("Not Found"))
            {
                 // Also fallback to continuous if error/notfound
                 if (!_isContinuousChecking) 
                 {
                     StatusText.Text = "Not Found. Switching to AI Stream...";
                     StartContinuousAiListening();
                     return true; 
                 }

                 StatusText.Text = "Not Found / Failed";
                 StatusText.Foreground = Brushes.Red;
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

        private Task<string> RunPythonRecognizer(string wavPath, bool noShazam = false)
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
                    var args = $"-3.10 \"{scriptPath}\" \"{wavPath}\"";
                    if (noShazam) args += " --no-shazam";
                    
                    start.Arguments = args;
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
        
        private void AddToHistory(string name, string content, string artist, string title, bool isAI)
        {
            // Avoid duplicates at the top
            if (_history.Count > 0 && _history[0].Content == content) return;
            
            _history.Insert(0, new HistoryItem 
            { 
                DisplayName = name, 
                Content = content, 
                Artist = artist,
                Title = title,
                IsAI = isAI 
            });
            
            // Limit history
            if (_history.Count > 10) _history.RemoveAt(_history.Count - 1);
        }
    }
}