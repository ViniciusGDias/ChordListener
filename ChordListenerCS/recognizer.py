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

# --- Improved Chord Recognition with Viterbi Decoding ---

def generate_templates():
    """
    Generate 12-dimensional chord templates for Major and Minor triads.
    Returns:
        templates (np.ndarray): Shape (24, 12). Rows are chords, columns are chroma bins.
        labels (list): List of chord names correspoddning to rows.
    """
    templates = []
    labels = []
    
    # 12 Semitones
    semitones = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
    
    # Major (Root, Major 3rd, Perfect 5th) -> Intervals: 0, 4, 7
    # Minor (Root, Minor 3rd, Perfect 5th) -> Intervals: 0, 3, 7
    
    for root_idx, name in enumerate(semitones):
        # Major
        vec_maj = np.zeros(12)
        vec_maj[root_idx] = 1.0
        vec_maj[(root_idx + 4) % 12] = 1.0 # Give slightly less weight to 3rd/5th if we wanted, but binary is fine for CENS
        vec_maj[(root_idx + 7) % 12] = 1.0
        templates.append(vec_maj)
        labels.append(name)
        
        # Minor
        vec_min = np.zeros(12)
        vec_min[root_idx] = 1.0
        vec_min[(root_idx + 3) % 12] = 1.0
        vec_min[(root_idx + 7) % 12] = 1.0
        templates.append(vec_min)
        labels.append(name + 'm')
        
    return np.array(templates), labels

def viterbi_decoding(chroma, templates):
    """
    Find most likely chord sequence using Viterbi algorithm.
    chroma: (12, N)
    templates: (24, 12)
    """
    n_frames = chroma.shape[1]
    n_chords = templates.shape[0]
    
    # 1. Emission Probabilities (Cosine Similarity)
    # Norm templates
    templates_norm = np.linalg.norm(templates, axis=1, keepdims=True)
    templates_unit = templates / (templates_norm + 1e-6)
    
    # Norm chroma
    chroma_norm = np.linalg.norm(chroma, axis=0, keepdims=True)
    chroma_unit = chroma / (chroma_norm + 1e-6)
    
    # Similarity matrix (n_chords, n_frames)
    # Value is between 0 and 1
    emission = np.dot(templates_unit, chroma_unit)
    
    # 2. Transition Matrix
    # High probability to stay in same chord, lower to switch
    # This acts as a smoothing factor naturally
    transition_prob = 0.95
    trans_mat = np.ones((n_chords, n_chords)) * ((1 - transition_prob) / (n_chords - 1))
    np.fill_diagonal(trans_mat, transition_prob)
    
    # Log probabilities to avoid underflow
    log_trans = np.log(trans_mat)
    log_emit = np.log(emission + 1e-6)
    
    # 3. Viterbi Path finding
    path = np.zeros((n_chords, n_frames), dtype=int)
    log_delta = np.zeros((n_chords, n_frames))
    
    # Init
    log_delta[:, 0] = log_emit[:, 0]
    
    for t in range(1, n_frames):
        # For each current state (chord), find best previous state
        # Broadcast add: (n_chords, 1) + (n_chords, n_chords) -> (n_chords, n_chords)
        # We want max over previous states (columns of trans_mat)
        # delta[t-1] is shape (n_chords,)
        
        # scores[i, j] = prob of going from state i to j
        scores = log_delta[:, t-1][:, None] + log_trans
        
        # max over previous states (axis 0)
        best_prev = np.argmax(scores, axis=0)
        
        path[:, t] = best_prev
        
        # Current max probability + emission
        log_delta[:, t] = scores[best_prev, np.arange(n_chords)] + log_emit[:, t]
        
    # Backtrack
    best_path = []
    last_state = np.argmax(log_delta[:, -1])
    best_path.append(last_state)
    
    for t in range(n_frames - 1, 0, -1):
        last_state = path[last_state, t]
        best_path.append(last_state)
        
    return best_path[::-1]

def estimate_chords(file_path):
    try:
        # Load audio (downsample to 22050 for speed)
        y, sr = librosa.load(file_path, sr=22050, duration=30)
        
        # Use Harmonic component
        y_harmonic, _ = librosa.effects.hpss(y)
        
        # Compute Chroma CENS (Chroma Energy Normalized Statistics)
        # CENS is robust to dynamics and timbre, good for chord ID
        # hop_length=512 gives ~43 frames/sec
        chroma = librosa.feature.chroma_cens(y=y_harmonic, sr=sr, hop_length=512, fmin=librosa.note_to_hz('C2'))
        
        # Generate Templates (Major + Minor)
        templates, labels = generate_templates()
        
        # Decode optimal path
        chord_indices = viterbi_decoding(chroma, templates)
        
        # Convert indices to names and collapse
        detected_sequence = []
        if len(chord_indices) > 0:
            current_idx = chord_indices[0]
            detected_sequence.append(labels[current_idx])
            
            for idx in chord_indices[1:]:
                if idx != current_idx:
                    detected_sequence.append(labels[idx])
                    current_idx = idx
        
        # Post-Processing: cleanup fast oscillating errors if Viterbi didn't catch them
        # (Viterbi usually handles this via transition penalties, but let's be safe)
        
        # Limit output
        return " -> ".join(detected_sequence[:16])
        
    except Exception as e:
        return f"Chord Error: {str(e)}"

async def main():
    if len(sys.argv) < 2:
        print("Error: No file provided")
        return

    file_path = sys.argv[1]
    
    # 1. Try Shazam First (unless disabled)
    if "--no-shazam" not in sys.argv:
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
