import asyncio
import os
import shutil
from rich.console import Console
from rich.panel import Panel
from rich.layout import Layout
from rich.live import Live
from rich.text import Text
from rich.spinner import Spinner
from rich.align import Align
from rich.syntax import Syntax
import requests
from bs4 import BeautifulSoup

# Check dependencies
console = Console()

try:
    import soundcard as sc
    import soundfile as sf
    AUDIO_AVAILABLE = True
except ImportError:
    AUDIO_AVAILABLE = False

try:
    from shazamio import Shazam
    RECOGNITION_AVAILABLE = True
except ImportError:
    RECOGNITION_AVAILABLE = False

# Configuration
RATE = 44100
RECORD_SECONDS = 5
WAVE_OUTPUT_FILENAME = "captured_sample_cli.wav"

def clear_screen():
    os.system('cls' if os.name == 'nt' else 'clear')

async def record_audio_segment():
    """Records system loopback audio."""
    if not AUDIO_AVAILABLE:
        return None
    try:
        loop = asyncio.get_event_loop()
        def _record():
            mic = sc.default_speaker()
            # Record 5 seconds
            data = mic.record(samplerate=RATE, numframes=RATE * RECORD_SECONDS)
            sf.write(WAVE_OUTPUT_FILENAME, data, RATE)
            return WAVE_OUTPUT_FILENAME
        
        return await loop.run_in_executor(None, _record)
    except Exception as e:
        # console.log(f"[red]Audio Error:[/red] {e}")
        return None

def search_cifra_club_sync(artist, title):
    """Searches and scrapes Cifra Club."""
    query = f"{artist} {title} cifra club"
    search_url = f"https://www.google.com/search?q={query}"
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
    }
    
    try:
        resp = requests.get(search_url, headers=headers, timeout=10)
        soup = BeautifulSoup(resp.text, 'html.parser')
        
        link = None
        for a in soup.find_all('a', href=True):
            if 'cifraclub.com.br' in a['href']:
                link = a['href']
                if '/url?q=' in link:
                    link = link.split('/url?q=')[1].split('&')[0]
                break
        
        if not link:
            return None
        
        cifra_resp = requests.get(link, headers=headers, timeout=10)
        cifra_soup = BeautifulSoup(cifra_resp.text, 'html.parser')
        
        pre_content = cifra_soup.find('pre')
        if pre_content:
            return {
                "url": link,
                "content": pre_content.get_text()
            }
    except Exception:
        pass
    return None

async def main():
    clear_screen()
    
    # Header
    console.print(Panel.fit(
        "[bold cyan]🎸 Chord Listener CLI 🎸[/bold cyan]\n[grey]Listening to your PC music...[/grey]",
        border_style="cyan"
    ))
    
    if not RECOGNITION_AVAILABLE:
        console.print("[bold red]Error:[/bold red] 'shazamio' library not installed. Cannot recognize music.")
        return

    if not AUDIO_AVAILABLE:
        console.print("[bold red]Error:[/bold red] 'soundcard'/'soundfile' not installed. Cannot hear PC.")
        return

    shazam = Shazam()
    last_song_key = None
    
    # Status Layout
    status_text = Text("Waiting for music...", style="yellow")
    content_area = Panel("No song detected yet.", title="Tablatura / Cifra", expand=True)
    
    layout = Layout()
    layout.split_column(
        Layout(name="status", size=3),
        Layout(name="content")
    )
    layout["status"].update(Panel(status_text, border_style="yellow"))
    layout["content"].update(content_area)

    with Live(layout, refresh_per_second=4, screen=False) as live:
        while True:
            # 1. Update Status: Listening
            status_text = Text("🎧  Listening...", style="green blink")
            layout["status"].update(Panel(status_text, border_style="green"))
            
            # 2. Record
            file_path = await record_audio_segment()
            
            if file_path:
                status_text = Text("🔍  Identifying...", style="blue")
                layout["status"].update(Panel(status_text, border_style="blue"))
                
                try:
                    # 3. Recognize
                    out = await shazam.recognize(file_path)
                    track = out.get('track', {})
                    
                    if track:
                        key = track.get('key')
                        title = track.get('title')
                        subtitle = track.get('subtitle')
                        
                        full_name = f"{subtitle} - {title}"
                        
                        if key != last_song_key:
                            last_song_key = key
                            
                            status_text = Text(f"🎵  Found: {full_name}", style="bold magenta")
                            layout["status"].update(Panel(status_text, border_style="magenta"))
                            
                            # 4. Search Cifra
                            loop = asyncio.get_event_loop()
                            cifra_data = await loop.run_in_executor(None, lambda: search_cifra_club_sync(subtitle, title))
                            
                            if cifra_data:
                                tab_content = cifra_data['content']
                                # Basic format highlighting
                                syntax = Syntax(tab_content, "text", theme="monokai", word_wrap=True)
                                content_area = Panel(syntax, title=f"{full_name} (Source: CifraClub)", border_style="green", expand=True)
                            else:
                                content_area = Panel(f"[red]Tab not found for {full_name}[/red]", title="Error", border_style="red")
                            
                            layout["content"].update(content_area)
                        else:
                            # Same song, just pulse status
                            status_text = Text(f"🎵  Playing: {full_name}", style="magenta")
                            layout["status"].update(Panel(status_text, border_style="magenta"))
                            
                except Exception as e:
                    # console.print(f"Debug: {e}")
                    pass
            
            await asyncio.sleep(1)

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nGoodbye!")
