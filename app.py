import asyncio
import os
import flet as ft
import requests
from bs4 import BeautifulSoup
import threading

# --- Logic Mixins ---
try:
    import soundcard as sc
    import soundfile as sf
    AUDIO_AVAILABLE = True
except ImportError:
    AUDIO_AVAILABLE = False
    print("SoundCard/SoundFile not found.")

try:
    from shazamio import Shazam
    RECOGNITION_AVAILABLE = True
except ImportError:
    RECOGNITION_AVAILABLE = False
    print("ShazamIO not found.")

# Audio configurations
RATE = 44100
RECORD_SECONDS = 5
WAVE_OUTPUT_FILENAME = "captured_sample_flet.wav"

async def record_audio_segment():
    if not AUDIO_AVAILABLE:
        return None
    try:
        # Run synchronous audio recording in a separate thread to prevent UI freezing
        loop = asyncio.get_event_loop()
        def _record():
            mic = sc.default_speaker()
            data = mic.record(samplerate=RATE, numframes=RATE * RECORD_SECONDS)
            sf.write(WAVE_OUTPUT_FILENAME, data, RATE)
            return WAVE_OUTPUT_FILENAME
        
        return await loop.run_in_executor(None, _record)
    except Exception as e:
        print(f"Error recording: {e}")
        return None

def search_cifra_club_sync(query):
    print(f"Searching for: {query}")
    search_url = f"https://www.google.com/search?q={query} cifra club"
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
        
        print(f"Found link: {link}")
        cifra_resp = requests.get(link, headers=headers, timeout=10)
        cifra_soup = BeautifulSoup(cifra_resp.text, 'html.parser')
        
        pre_content = cifra_soup.find('pre')
        if pre_content:
            return {
                "url": link,
                "content": pre_content.get_text()
            }
    except Exception as e:
        print(f"Error scraping: {e}")
    return None

async def main(page: ft.Page):
    page.title = "Chord Listener"
    page.theme_mode = ft.ThemeMode.DARK
    page.scroll = "auto"
    
    # State variables
    is_listening = False
    
    # --- UI Components ---
    
    status_text = ft.Text("Ready", color="grey")
    
    txt_search = ft.TextField(
        hint_text="Artist - Song",
        expand=True,
        on_submit=lambda e: perform_search_async(txt_search.value)
    )
    
    txt_content = ft.Text(
        value="Waiting for song...",
        font_family="Consolas, monospace",
        size=14,
        selectable=True
    )
    
    # Cipher Container - Improved Layout
    container_cipher = ft.Container(
        content=ft.Column(
            [txt_content], 
            scroll=ft.ScrollMode.AUTO, 
            expand=True
        ),
        bgcolor="#111111",
        padding=15,
        border_radius=8,
        border=ft.border.all(1, "#333333"),
        expand=True, # Allow it to fill remaining space
        margin=ft.margin.only(top=10)
    )

    async def perform_search_async(query):
        if not query: return
        
        status_text.value = f"Searching: {query}..."
        status_text.color = "blue"
        page.update()
        
        loop = asyncio.get_event_loop()
        # Run blocking request in thread
        result = await loop.run_in_executor(None, lambda: search_cifra_club_sync(query))
        
        if result:
            status_text.value = "Found!"
            status_text.color = "green"
            txt_content.value = result['content']
        else:
            status_text.value = "Not found."
            status_text.color = "red"
            txt_content.value = "No tab found."
        
        page.update()

    async def btn_search_click(e):
        await perform_search_async(txt_search.value)

    async def listener_loop():
        nonlocal is_listening
        shazam = Shazam() if RECOGNITION_AVAILABLE else None
        
        last_id = None
        
        while is_listening:
            if not shazam:
                status_text.value = "Library missing: Manual search only."
                status_text.color = "red"
                # Stop listening
                is_listening = False
                btn_listen.text = "Start Auto-Listen"
                btn_listen.icon = "mic"
                btn_listen.disabled = True
                page.update()
                break

            status_text.value = "Listening..."
            status_text.color = "green"
            page.update()

            file_path = await record_audio_segment()
            
            if file_path:
                status_text.value = "Identifying..."
                page.update()
                try:
                    out = await shazam.recognize(file_path)
                    track = out.get('track', {})
                    if track:
                        key = track.get('key')
                        if key != last_id:
                            last_id = key
                            title = track.get('title')
                            subtitle = track.get('subtitle')
                            full_name = f"{subtitle} - {title}"
                            
                            status_text.value = f"Detected: {full_name}"
                            page.update()
                            await perform_search_async(full_name)
                except Exception as e:
                    print(f"Shazam Error: {e}")
            
            await asyncio.sleep(1)

    async def btn_listen_click(e):
        nonlocal is_listening
        is_listening = not is_listening
        e.control.text = "Stop Listening" if is_listening else "Start Auto-Listen"
        e.control.icon = "mic_off" if is_listening else "mic"
        page.update()
        
        if is_listening:
            page.run_task(listener_loop)

    btn_listen = ft.ElevatedButton(
        "Start Auto-Listen",
        icon="mic",
        on_click=btn_listen_click,
        color="white",
        bgcolor="green"
    )

    # --- Add to page ---
    page.add(
        ft.Row([
            ft.Icon("music_note", size=30),
            ft.Text("Chord Listener", size=24, weight="bold"),
            ft.Container(expand=True),
            status_text
        ]),
        ft.Divider(),
        ft.Row([
            txt_search,
            ft.IconButton("search", on_click=btn_search_click),
            btn_listen
        ]),
        container_cipher
    )

if __name__ == "__main__":
    ft.app(target=main)
