import asyncio
import sys
import warnings
import numpy as np

# Suppress warnings
warnings.filterwarnings("ignore")

try:
    from shazamio import Shazam
    import librosa
except ImportError:
    print("Error: Missing libraries. Please install shazamio, librosa, numpy")
    sys.exit(1)

# Major and Minor Chord Templates (Simplified)
CHORD_TEMPLATES = {
    'C':  [1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0],
    'Cm': [1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0],
    'C#': [0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0],
    'C#m':[0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
    'D':  [0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0],
    'Dm': [0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0],
    'D#': [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0],
    'D#m':[0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0],
    'E':  [0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1],
    'Em': [0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1],
    'F':  [1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0],
    'Fm': [1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0],
    'F#': [0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0],
    'F#m':[0, 1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0],
    'G':  [0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1],
    'Gm': [0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 1, 0],
    'G#': [1, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0],
    'G#m':[0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 1],
    'A':  [0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0],  # Fixed A template
    'Am': [1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0],  # Fixed Am template
    'A#': [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0],  # Fixed A#
    'A#m':[0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
    'B':  [0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
    'Bm': [0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1]
}
# Corrections for standard templates (Roots: C C# D D# E F F# G G# A A# B)
# Re-defining carefully to be safe:
# C: C(0), E(4), G(7)
# Cm: C(0), Eb(3), G(7)
CHORD_DEFS = {
    'C':   [0, 4, 7], 'Cm':  [0, 3, 7],
    'C#':  [1, 5, 8], 'C#m': [1, 4, 8],
    'D':   [2, 6, 9], 'Dm':  [2, 5, 9],
    'D#':  [3, 7, 10],'D#m': [3, 6, 10], 
    'E':   [4, 8, 11],'Em':  [4, 7, 11],
    'F':   [5, 9, 0], 'Fm':  [5, 8, 0],
    'F#':  [6, 10, 1],'F#m': [6, 9, 1],
    'G':   [7, 11, 2],'Gm':  [7, 10, 2],
    'G#':  [8, 0, 3], 'G#m': [8, 11, 3],
    'A':   [9, 1, 4], 'Am':  [9, 0, 4],
    'A#':  [10, 2, 5],'A#m': [10, 1, 5],
    'B':   [11, 3, 6],'Bm':  [11, 2, 6],
}

def estimate_chords(file_path):
    try:
        y, sr = librosa.load(file_path, duration=30)
        # Harmonic component only to suppress percussion
        y_harmonic, _ = librosa.effects.hpss(y)
        
        # Compute Chroma
        chroma = librosa.feature.chroma_cqt(y=y_harmonic, sr=sr)
        
        # Average chroma over time chunks (e.g. every 0.5s)
        # For simplicity, let's just find the global key or sequence
        detected_chords = []
        
        # Identify chord for each time frame
        frames = chroma.shape[1]
        for i in range(frames):
            frame_chroma = chroma[:, i]
            
            best_score = -1
            best_chord = "?"
            
            for chord_name, notes in CHORD_DEFS.items():
                # Simple score: sum of chroma values at template indices
                score = sum(frame_chroma[n] for n in notes)
                if score > best_score:
                    best_score = score
                    best_chord = chord_name
            
            detected_chords.append(best_chord)
            
        # Compress sequence (remove duplicates)
        final_sequence = []
        if detected_chords:
            current = detected_chords[0]
            final_sequence.append(current)
            for ch in detected_chords[1:]:
                # Only add if distinct and sustainable (simple smoothing)
                if ch != current:
                    final_sequence.append(ch)
                    current = ch
                    
        # Simplify: Pick top 5 most frequent or just the unique sequence roughly
        # Let's return the sequence as a string
        # Limit to first 8 changes to keep it readable
        return " -> ".join(final_sequence[:12])
        
    except Exception as e:
        return f"Chord Error: {str(e)}"

async def main():
    if len(sys.argv) < 2:
        print("Error: No file provided")
        return

    file_path = sys.argv[1]
    
    # 1. Try Shazam First
    shazam = Shazam()
    try:
        out = await shazam.recognize(file_path)
        track = out.get('track', {})
        
        if track:
            title = track.get('title')
            subtitle = track.get('subtitle')
            print(f"{subtitle} - {title}")
            return
    except:
        pass # Fallback to chords
        
    # 2. If Shazam Failed (or we want chords), Detect Chords
    # We print a specific marker so C# feels it
    chords = estimate_chords(file_path)
    print(f"AI_CHORDS:{chords}")

if __name__ == "__main__":
    asyncio.run(main())
