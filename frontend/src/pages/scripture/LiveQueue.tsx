import Icon from '../../components/Icon'
import type { QueueItem } from './liveComposer'

interface LiveQueueProps {
  queue: QueueItem[]
  liveId: string | null
  onPickItem: (item: QueueItem) => void
  onRemoveItem: (id: string) => void
  onClearAll: () => void
}

export default function LiveQueue({
  queue,
  liveId,
  onPickItem,
  onRemoveItem,
  onClearAll,
}: LiveQueueProps) {
  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-l border-outline-variant bg-surface-container-low">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex items-center gap-2 text-emerald-live">
          <Icon name="cast" size={18} />
          <h2 className="font-mono text-status-label uppercase">Live Queue</h2>
        </div>
        <button
          type="button"
          onClick={onClearAll}
          disabled={queue.length === 0}
          className="font-mono text-mono-ui uppercase tracking-widest text-rose-error transition-opacity hover:opacity-80 disabled:opacity-30"
        >
          Clear All
        </button>
      </div>

      <div className="panel-scroll min-h-0 flex-1 overflow-y-auto p-3">
        {queue.length === 0 ? (
          <p className="mt-8 text-center font-mono text-mono-ui italic text-slate-muted">
            Select verses in the preview to go live.
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {queue.map(item => {
              const isLive = item.id === liveId
              return (
                <li key={item.id} className="group relative">
                  <button
                    type="button"
                    onClick={() => onPickItem(item)}
                    title={isLive ? 'Live on displays' : 'Show on displays'}
                    className={
                      isLive
                        ? 'live-glow w-full rounded border border-emerald-live/50 border-l-4 border-l-emerald-live bg-surface-container-high p-3 text-left'
                        : 'w-full rounded border border-outline-variant bg-surface-container p-3 text-left transition-colors hover:border-emerald-live/40'
                    }
                  >
                    <span className="flex items-center gap-2">
                      {isLive && (
                        <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-live" />
                      )}
                      <span className="text-body-md font-semibold text-on-surface">
                        {item.reference}
                      </span>
                    </span>
                    <span className="mt-1 line-clamp-2 block text-mono-ui italic text-on-surface-variant">
                      “{item.text}”
                    </span>
                  </button>
                  <button
                    type="button"
                    onClick={() => onRemoveItem(item.id)}
                    title="Remove from queue"
                    className="invisible absolute right-2 top-2 flex items-center rounded bg-surface-container-highest p-0.5 text-on-surface-variant transition-colors hover:text-rose-error group-hover:visible"
                  >
                    <Icon name="close" size={14} />
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
