import { useState, useEffect, useRef } from 'react'
import './App.css'

function App() {
  const [status, setStatus] = useState('connecting');
  const [songData, setSongData] = useState(null);
  const [autoScroll, setAutoScroll] = useState(false);
  const scrollRef = useRef(null);
  const socketRef = useRef(null);

  const handleSearch = (e) => {
    e.preventDefault();
    const query = e.target.elements.search.value;
    if (query && socketRef.current) {
        socketRef.current.send(JSON.stringify({ action: 'search', query }));
    }
  };

  useEffect(() => {
    const connect = () => {
      const ws = new WebSocket('ws://localhost:8000/ws');
      socketRef.current = ws;

      ws.onopen = () => {
        setStatus('listening');
        console.log('Connected to backend');
      };

      ws.onmessage = (event) => {
        const data = JSON.parse(event.data);
        console.log('Received:', data);
        if (data.status === 'found') {
          setSongData(data);
        } else if (data.status === 'error') {
            setStatus('manual-only');
        }
      };

      ws.onclose = () => {
        setStatus('disconnected');
        setTimeout(connect, 3000); 
      };

      ws.onerror = (err) => {
        console.error('Socket error:', err);
        ws.close();
      };
      
      return ws;
    };

    const ws = connect();
    return () => ws.close();
  }, []);

  useEffect(() => {
    let interval;
    if (autoScroll && songData?.tab) {
      interval = setInterval(() => {
        if (scrollRef.current) {
          scrollRef.current.scrollTop += 1;
        }
      }, 50);
    }
    return () => clearInterval(interval);
  }, [autoScroll, songData]);

  return (
    <div className="container">
      <header className="header">
        <h1>🎸 Chord Listener</h1>
        <div className={`status-badge ${status}`}>
          {status === 'listening' ? 'Listening...' : status === 'manual-only' ? 'Manual Mode' : status}
        </div>
      </header>

      <main className="main-content">
        <div className="search-bar">
            <form onSubmit={handleSearch}>
                <input type="text" name="search" placeholder="Search song manually (Artist - Title)..." />
                <button type="submit">Search</button>
            </form>
        </div>

        {!songData ? (
          <div className="placeholder">
            {status === 'manual-only' ? (
                <p>Automatic recognition unavailable. Please search manually above.</p>
            ) : (
                <>
                    <div className="pulse-circle"></div>
                    <p>Play some music on your PC...</p>
                </>
            )}
          </div>
        ) : (
          <div className="song-display">
            <div className="song-info">
              {songData.cover ? <img src={songData.cover} alt="Cover" className="cover-art" /> : <div className="cover-placeholder">NO IMAGE</div>}
              <div className="details">
                <h2>{songData.title}</h2>
                <h3>{songData.artist}</h3>
                {songData.tab?.url && (
                  <a href={songData.tab.url} target="_blank" rel="noreferrer" className="source-link">
                    Open in CifraClub
                  </a>
                )}
              </div>
            </div>

            <div className="controls">
               <button onClick={() => setAutoScroll(!autoScroll)}>
                 {autoScroll ? '⏸ Stop Scroll' : '▶ Auto Scroll'}
               </button>
               <button onClick={() => setSongData(null)} style={{marginLeft: '10px'}}>
                 ✖ Clear
               </button>
            </div>

            <div className="tab-container" ref={scrollRef}>
              {songData.tab?.content ? (
                <pre>{songData.tab.content}</pre>
              ) : (
                <div className="no-tab">
                  <p>Song detected, but no tab found automatically.</p>
                  <p>Try searching manually above.</p>
                </div>
              )}
            </div>
          </div>
        )}
      </main>
    </div>
  )
}

export default App
