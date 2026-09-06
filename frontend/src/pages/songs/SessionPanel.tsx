import Icon from '../../components/Icon'
import type { SessionSong } from './SongsSession'

interface SessionPanelProps {
  session: SessionSong[]
  selectedId: number | null
  onPickSession: (id: number) => void
  onGoLiveSession: (id: number) => void
}

/**
 * Left column of the songs console — the analog of scripture's SearchPanel. Holds the
 * session history: songs sent live this session (whole songs, not individual sections),
 * most recent first. The library and its actions (New Song / import) live in the bottom band.
 */
export default function SessionPanel({
  session,
  selectedId,
  onPickSession,
  onGoLiveSession,
}: SessionPanelProps) {
  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-r border-outline-variant bg-surface-container-low">
      <div className="flex h-14 shrink-0 items-center gap-2 border-b border-outline-variant px-4 text-on-surface">
        <Icon name="history" size={18} />
        <h2 className="font-mono text-status-label uppercase">Session</h2>
      </div>

      <div className="panel-scroll min-h-0 flex-1 overflow-y-auto p-4">
        {session.length === 0 ? (
          <p className="text-mono-ui font-mono italic text-slate-muted">
            Songs shown live appear here.
          </p>
        ) : (
          <ul className="flex flex-col gap-1">
            {session.map(item => {
              const isSelected = item.id === selectedId
              return (
                <li key={item.id}>
                  <button
                    type="button"
                    onClick={() => onPickSession(item.id)}
                    onDoubleClick={() => onGoLiveSession(item.id)}
                    title="Click to preview · double-click to send live"
                    className={`w-full rounded px-2 py-2 text-left transition-colors ${
                      isSelected ? 'bg-surface-container-high' : 'hover:bg-surface-container-high'
                    }`}
                  >
                    <span className="flex items-baseline justify-between gap-2">
                      <span className="truncate text-body-md font-semibold text-on-surface">
                        {item.title}
                      </span>
                      <span className="shrink-0 font-mono text-mono-ui text-slate-muted">
                        {item.at}
                      </span>
                    </span>
                    {item.author && (
                      <span className="block truncate font-mono text-mono-ui text-on-surface-variant">
                        {item.author}
                      </span>
                    )}
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </aside>
  )
}
