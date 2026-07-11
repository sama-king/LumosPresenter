import Icon from '../../components/Icon'
import type { DisplayDto } from '../../lib/types'

interface DisplayTabsProps {
  displays: DisplayDto[]
  selectedId: number | null
  dirty: boolean
  onSelect: (id: number) => void
  onAdd: () => void
}

export default function DisplayTabs({ displays, selectedId, dirty, onSelect, onAdd }: DisplayTabsProps) {
  return (
    <div className="flex items-center gap-1">
      {displays.map(display => {
        const active = display.id === selectedId
        return (
          <button
            key={display.id}
            type="button"
            onClick={() => onSelect(display.id)}
            className={
              active
                ? 'flex items-center gap-2 border-b-2 border-primary px-4 py-3 font-mono text-status-label text-primary'
                : 'flex items-center gap-2 border-b-2 border-transparent px-4 py-3 font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface'
            }
          >
            {display.name}
            {display.followsDisplayId !== null && (
              <Icon name="sync_lock" size={14} className="text-slate-muted" />
            )}
            {active && dirty && <span className="h-1.5 w-1.5 rounded-full bg-amber-warning" />}
          </button>
        )
      })}
      <button
        type="button"
        onClick={onAdd}
        className="ml-2 flex items-center gap-1.5 rounded-lg border border-dashed border-surface-variant px-3 py-1.5 font-mono text-mono-ui uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary"
      >
        <Icon name="add" size={16} />
        Add Display
      </button>
    </div>
  )
}
