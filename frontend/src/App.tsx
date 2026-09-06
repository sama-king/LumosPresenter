import { Navigate, Route, Routes } from 'react-router-dom'
import AppShell from './components/shell/AppShell'
import { EventStreamProvider } from './lib/events'
import AdminPage from './pages/AdminPage'
import DisplayPage from './pages/DisplayPage'
import MediaPage from './pages/media/MediaPage'
import StagePage from './pages/stage/StagePage'
import ScripturePage from './pages/scripture/ScripturePage'
import SettingsPage from './pages/settings/SettingsPage'
import SongsPage from './pages/songs/SongsPage'

export default function App() {
  return (
    <EventStreamProvider>
      {/* Per-tab UI state survives navigation via usePersistentState (a module-level store),
          so no cross-route providers are needed here. */}
      <Routes>
        <Route path="/" element={<Navigate to="/scripture" replace />} />
        <Route element={<AppShell />}>
          <Route path="/scripture" element={<ScripturePage />} />
          <Route path="/songs" element={<SongsPage />} />
          <Route path="/media" element={<MediaPage />} />
          <Route path="/stage" element={<StagePage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
        {/* Legacy engine/audio/media test console — no designed shell yet. */}
        <Route path="/admin" element={<AdminPage />} />
        {/* Projection surfaces are per-display; the bare path keeps old bookmarks working. */}
        <Route path="/display" element={<Navigate to="/display/1" replace />} />
        <Route path="/display/:id" element={<DisplayPage />} />
      </Routes>
    </EventStreamProvider>
  )
}
