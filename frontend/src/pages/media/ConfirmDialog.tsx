import Icon from '../../components/Icon'

interface ConfirmDialogProps {
  title: string
  message: string
  confirmLabel?: string
  onConfirm: () => void
  onClose: () => void
}

/**
 * Destructive-action confirmation, following the AddDisplayDialog modal idiom. Used for
 * removing a schedule entry and for unlinking gallery items.
 */
export default function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Delete',
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  return (
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-black/60"
      role="presentation"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-label={title}
        onClick={e => e.stopPropagation()}
        className="w-96 space-y-5 rounded-xl border border-surface-variant bg-surface-container p-6 shadow-2xl"
      >
        <div className="flex items-center justify-between">
          <h2 className="font-display text-headline-md text-on-surface">{title}</h2>
          <button
            type="button"
            title="Close"
            onClick={onClose}
            className="text-on-surface-variant transition-colors hover:text-on-surface"
          >
            <Icon name="close" size={20} />
          </button>
        </div>
        <p className="text-body-md text-on-surface-variant">{message}</p>
        <div className="flex justify-end gap-3 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg px-4 py-2 font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface"
          >
            Cancel
          </button>
          <button
            type="button"
            autoFocus
            onClick={() => {
              onConfirm()
              onClose()
            }}
            className="rounded-lg bg-rose-error px-4 py-2 font-mono text-status-label font-bold text-white transition-opacity hover:opacity-90"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
