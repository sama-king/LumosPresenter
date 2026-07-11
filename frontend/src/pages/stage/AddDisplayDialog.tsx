import { useState } from 'react'
import Icon from '../../components/Icon'
import type { DisplayDto } from '../../lib/types'
import { FieldLabel, SelectField } from './controls'

interface AddDisplayDialogProps {
  displays: DisplayDto[]
  onCreate: (name: string, useSettingsOfDisplayId: number | null) => void
  onClose: () => void
}

export default function AddDisplayDialog({ displays, onCreate, onClose }: AddDisplayDialogProps) {
  const [name, setName] = useState(`Display ${displays.length + 1}`)
  const [sourceId, setSourceId] = useState<'none' | string>('none')
  // Followers can't be sources themselves (no chains).
  const sourceCandidates = displays.filter(d => d.followsDisplayId === null)

  const create = () => onCreate(name.trim(), sourceId === 'none' ? null : Number(sourceId))

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60"
      role="presentation"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-label="Add display"
        onClick={e => e.stopPropagation()}
        className="w-96 space-y-5 rounded-xl border border-surface-variant bg-surface-container p-6 shadow-2xl"
      >
        <div className="flex items-center justify-between">
          <h2 className="font-display text-headline-md text-on-surface">Add Display</h2>
          <button
            type="button"
            title="Close"
            onClick={onClose}
            className="text-on-surface-variant transition-colors hover:text-on-surface"
          >
            <Icon name="close" size={20} />
          </button>
        </div>
        <label className="block space-y-2">
          <FieldLabel>Name</FieldLabel>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            className="w-full rounded-lg border border-surface-variant bg-surface-container-lowest px-3 py-2.5 text-body-md text-on-surface focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </label>
        <div className="space-y-2">
          <SelectField
            label="Use settings of"
            value={sourceId}
            options={[
              { value: 'none', label: 'None — start from defaults' },
              ...sourceCandidates.map(d => ({ value: String(d.id), label: d.name })),
            ]}
            onChange={setSourceId}
          />
          {sourceId !== 'none' && (
            <p className="font-mono text-mono-ui text-slate-muted">
              Persistent link: this display will always mirror that display's settings.
            </p>
          )}
        </div>
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
            disabled={name.trim() === ''}
            onClick={create}
            className="rounded-lg bg-primary px-4 py-2 font-mono text-status-label font-bold text-on-primary transition-opacity disabled:opacity-40"
          >
            Create
          </button>
        </div>
      </div>
    </div>
  )
}
