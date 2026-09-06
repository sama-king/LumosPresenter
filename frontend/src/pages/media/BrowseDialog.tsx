import { useCallback, useEffect, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import type { BrowseResponse } from '../../lib/types'

interface BrowseDialogProps {
  onAdd: (paths: string[]) => void
  onClose: () => void
  onError: (message: string) => void
}

/**
 * Server-side "Add from disk" browser. This exists because a browser cannot hand us a real
 * file path — a file input gives bytes and a bare name, never a location — and the gallery
 * links files rather than copying them. The console and the server run on the same machine,
 * so the server lists directories and the operator picks; every path we store comes from here.
 */
export default function BrowseDialog({ onAdd, onClose, onError }: BrowseDialogProps) {
  const [listing, setListing] = useState<BrowseResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [picked, setPicked] = useState<Set<string>>(new Set())

  const load = useCallback(
    (path?: string) => {
      setLoading(true)
      void api
        .browseMedia(path)
        .then(setListing)
        .catch((err: Error) => onError(err.message))
        .finally(() => setLoading(false))
    },
    [onError],
  )

  useEffect(() => load(), [load])

  const toggle = (path: string) =>
    setPicked(prev => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })

  // Navigating to another folder keeps the picks: an operator can gather a slideshow's
  // worth of images from several folders in one pass.
  const openFolder = (path: string) => load(path)

  const files = listing?.files ?? []
  const allPicked = files.length > 0 && files.every(f => picked.has(f.path))

  return (
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-black/60"
      role="presentation"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-label="Add media from disk"
        onClick={e => e.stopPropagation()}
        className="flex h-[70vh] w-[46rem] flex-col rounded-xl border border-surface-variant bg-surface-container shadow-2xl"
      >
        <div className="flex shrink-0 items-center justify-between border-b border-outline-variant px-6 py-4">
          <h2 className="font-display text-headline-md text-on-surface">Add Media from Disk</h2>
          <button
            type="button"
            title="Close"
            onClick={onClose}
            className="text-on-surface-variant transition-colors hover:text-on-surface"
          >
            <Icon name="close" size={20} />
          </button>
        </div>

        <div className="flex shrink-0 items-center gap-2 border-b border-outline-variant px-6 py-2.5">
          <button
            type="button"
            disabled={!listing?.parent}
            onClick={() => listing?.parent && openFolder(listing.parent)}
            title="Parent folder"
            className="flex items-center rounded border border-outline-variant bg-surface-container-low p-1.5 text-on-surface-variant transition-colors hover:text-on-surface disabled:opacity-30"
          >
            <Icon name="arrow_upward" size={16} />
          </button>
          <span className="truncate font-mono text-mono-ui text-on-surface-variant" dir="rtl">
            {listing?.path ?? '…'}
          </span>
        </div>

        <div className="panel-scroll min-h-0 flex-1 overflow-y-auto px-6 py-3">
          {loading ? (
            <p className="mt-8 text-center font-mono text-mono-ui italic text-slate-muted">
              Reading folder…
            </p>
          ) : (
            <ul className="flex flex-col gap-0.5">
              {listing?.directories.map(dir => (
                <li key={dir.path}>
                  <button
                    type="button"
                    onClick={() => openFolder(dir.path)}
                    className="flex w-full items-center gap-2.5 rounded px-2 py-1.5 text-left text-body-md text-on-surface transition-colors hover:bg-surface-container-high"
                  >
                    <Icon name="folder" size={18} className="shrink-0 text-primary" />
                    <span className="truncate">{dir.name}</span>
                  </button>
                </li>
              ))}
              {files.map(file => {
                const isPicked = picked.has(file.path)
                return (
                  <li key={file.path}>
                    <button
                      type="button"
                      onClick={() => toggle(file.path)}
                      className={`flex w-full items-center gap-2.5 rounded px-2 py-1.5 text-left text-body-md transition-colors ${
                        isPicked
                          ? 'bg-primary/10 text-on-surface'
                          : 'text-on-surface-variant hover:bg-surface-container-high'
                      }`}
                    >
                      <Icon
                        name={isPicked ? 'check_box' : 'check_box_outline_blank'}
                        size={18}
                        className={`shrink-0 ${isPicked ? 'text-primary' : 'text-slate-muted'}`}
                      />
                      <Icon
                        name={file.kind === 'video' ? 'movie' : 'image'}
                        size={18}
                        className="shrink-0 text-slate-muted"
                      />
                      <span className="truncate">{file.name}</span>
                    </button>
                  </li>
                )
              })}
              {listing && listing.directories.length === 0 && files.length === 0 && (
                <li className="mt-8 text-center font-mono text-mono-ui italic text-slate-muted">
                  No folders or supported media here.
                </li>
              )}
            </ul>
          )}
        </div>

        <div className="flex shrink-0 items-center justify-between border-t border-outline-variant px-6 py-4">
          <button
            type="button"
            disabled={files.length === 0}
            onClick={() =>
              setPicked(prev => {
                const next = new Set(prev)
                for (const file of files) {
                  if (allPicked) next.delete(file.path)
                  else next.add(file.path)
                }
                return next
              })
            }
            className="font-mono text-mono-ui uppercase tracking-widest text-primary transition-opacity hover:opacity-80 disabled:opacity-30"
          >
            {allPicked ? 'Deselect folder' : 'Select all here'}
          </button>
          <div className="flex items-center gap-3">
            <span className="font-mono text-mono-ui text-slate-muted">
              {picked.size} selected
            </span>
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg px-4 py-2 font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface"
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={picked.size === 0}
              onClick={() => {
                onAdd([...picked])
                onClose()
              }}
              className="rounded-lg bg-primary px-4 py-2 font-mono text-status-label font-bold text-on-primary transition-opacity disabled:opacity-40"
            >
              Add to Gallery
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
