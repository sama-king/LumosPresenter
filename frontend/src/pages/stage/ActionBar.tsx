import Icon from '../../components/Icon'

interface ActionBarProps {
  dirty: boolean
  saving: boolean
  /** Editing is locked while the display mirrors another display's settings. */
  locked: boolean
  canDelete: boolean
  error: string
  onReset: () => void
  onDelete: () => void
  onSave: () => void
}

export default function ActionBar({
  dirty,
  saving,
  locked,
  canDelete,
  error,
  onReset,
  onDelete,
  onSave,
}: ActionBarProps) {
  return (
    <div className="flex h-20 shrink-0 items-center justify-between border-t border-surface-variant bg-surface px-gutter">
      <div className="flex items-center gap-4">
        <button
          type="button"
          disabled={locked}
          onClick={onReset}
          className="font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface disabled:cursor-not-allowed disabled:opacity-40"
        >
          Reset to Default
        </button>
        {canDelete && (
          <button
            type="button"
            onClick={onDelete}
            className="flex items-center gap-1.5 font-mono text-status-label text-on-surface-variant transition-colors hover:text-rose-error"
          >
            <Icon name="delete" size={16} />
            Delete Display
          </button>
        )}
      </div>
      <div className="flex items-center gap-4">
        {error !== '' && (
          <span className="font-mono text-status-label text-rose-error">{error}</span>
        )}
        {dirty && !locked && error === '' && (
          <span className="font-mono text-mono-ui uppercase text-amber-warning">
            Unsaved changes
          </span>
        )}
        <button
          type="button"
          disabled={locked || !dirty || saving}
          onClick={onSave}
          className="flex items-center gap-2 rounded-lg bg-primary px-6 py-3 font-mono text-status-label font-bold text-on-primary transition-opacity disabled:opacity-40"
        >
          <Icon name="save" size={18} />
          {saving ? 'Saving…' : 'Save Configuration'}
        </button>
      </div>
    </div>
  )
}
