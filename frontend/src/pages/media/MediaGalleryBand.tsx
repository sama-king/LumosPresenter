import { useCallback, useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import type { MediaLibraryItemDto } from '../../lib/types'
import BrowseDialog from './BrowseDialog'
import ContextMenu, { type MenuAction } from './ContextMenu'
import MediaThumb from '../../components/MediaThumb'

interface MediaGalleryBandProps {
  items: MediaLibraryItemDto[]
  selectedIds: string[]
  previewId: string | null
  onSelectionChange: (ids: string[]) => void
  onPreview: (id: string) => void
  onGoLive: (id: string) => void
  onAddPaths: (paths: string[]) => void
  onAddToSchedule: (ids: string[]) => void
  onCreateSlideshow: (ids: string[]) => void
  onCreateQueue: (ids: string[]) => void
  onDelete: (ids: string[]) => void
  onError: (message: string) => void
}

const COLLAPSE_KEY = 'lumos:media-gallery-collapsed'
type Tab = 'image' | 'video'

/**
 * Bottom band: the gallery itself, mirroring scripture's ResourcesBand and the song library
 * band. Collapsible, roughly mid-screen when open. Images and video get separate tabs since
 * they group differently — images into timed slideshows, video into play queues.
 *
 * Selection is multi-item (click, ⌘/ctrl-click to toggle, shift-click for a range), which is
 * what makes a slideshow or queue creatable from here; right-clicking inside a selection acts
 * on the whole selection, right-clicking outside it acts on the one tile.
 */
export default function MediaGalleryBand({
  items,
  selectedIds,
  previewId,
  onSelectionChange,
  onPreview,
  onGoLive,
  onAddPaths,
  onAddToSchedule,
  onCreateSlideshow,
  onCreateQueue,
  onDelete,
  onError,
}: MediaGalleryBandProps) {
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')
  const [tab, setTab] = useState<Tab>('image')
  const [browsing, setBrowsing] = useState(false)
  const [picking, setPicking] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const [menu, setMenu] = useState<{ x: number; y: number; ids: string[] } | null>(null)
  const anchorRef = useRef<string | null>(null)

  const visible = items.filter(item => item.kind === tab)
  const selected = new Set(selectedIds)

  const toggle = () =>
    setCollapsed(prev => {
      const next = !prev
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })

  /** Click semantics: plain replaces, ⌘/ctrl toggles, shift extends from the anchor. */
  const click = (item: MediaLibraryItemDto, e: React.MouseEvent) => {
    if (e.shiftKey && anchorRef.current) {
      const from = visible.findIndex(v => v.id === anchorRef.current)
      const to = visible.findIndex(v => v.id === item.id)
      if (from !== -1 && to !== -1) {
        const [lo, hi] = from < to ? [from, to] : [to, from]
        onSelectionChange(visible.slice(lo, hi + 1).map(v => v.id))
        return
      }
    }
    if (e.metaKey || e.ctrlKey) {
      anchorRef.current = item.id
      onSelectionChange(
        selected.has(item.id) ? selectedIds.filter(id => id !== item.id) : [...selectedIds, item.id],
      )
      return
    }
    anchorRef.current = item.id
    onSelectionChange([item.id])
    onPreview(item.id)
  }

  const openMenu = (item: MediaLibraryItemDto, e: React.MouseEvent) => {
    e.preventDefault()
    // Right-clicking inside a multi-selection acts on all of it; outside it, the menu
    // narrows to the tile under the pointer (and selects it, so the target is visible).
    const inSelection = selected.has(item.id) && selectedIds.length > 1
    const ids = inSelection ? selectedIds : [item.id]
    if (!inSelection) onSelectionChange([item.id])
    setMenu({ x: e.clientX, y: e.clientY, ids })
  }

  /**
   * "Add from Disk" opens the system file dialog — the server shows it, since only the server
   * can get real paths back. A console on another machine can't use that (the dialog would
   * open on the server's screen), so the server says so and we fall back to the in-console
   * browser.
   */
  const addFromDisk = () => {
    setPicking(true)
    api
      .pickMediaFiles(tab)
      .then(result => {
        if (!result.available) setBrowsing(true)
        else if (result.paths.length > 0) onAddPaths(result.paths)
      })
      .catch((err: Error) => onError(err.message))
      .finally(() => setPicking(false))
  }

  /**
   * Drag-and-drop from Finder/Explorer. A dropped File carries no path, so the only usable
   * payload is the text/uri-list the OS attaches — file:// URLs the server resolves back to
   * paths. When a drop arrives without one there is nothing we can link, so say so rather
   * than silently copying the bytes in (which the linked-file model forbids).
   */
  const drop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      setDragOver(false)
      const uriList = e.dataTransfer.getData('text/uri-list')
      const paths = uriList
        .split(/\r?\n/)
        .map(line => line.trim())
        .filter(line => line !== '' && !line.startsWith('#'))
      if (paths.length > 0) {
        onAddPaths(paths)
        return
      }
      onError('That drop carried no file path — use “Add from Disk” instead.')
    },
    [onAddPaths, onError],
  )

  const menuActions = (ids: string[]): MenuAction[] => {
    const multiple = ids.length > 1
    const picked = items.filter(item => ids.includes(item.id))
    const allImages = picked.every(item => item.kind === 'image')
    const allVideos = picked.every(item => item.kind === 'video')
    if (!multiple) {
      return [
        { label: 'Add to Schedule', icon: 'playlist_add', onSelect: () => onAddToSchedule(ids) },
        { label: 'Remove from Gallery', icon: 'delete', danger: true, onSelect: () => onDelete(ids) },
      ]
    }
    return [
      {
        label: 'Create Slideshow',
        icon: 'slideshow',
        disabled: !allImages,
        onSelect: () => onCreateSlideshow(ids),
      },
      {
        label: 'Create Queue',
        icon: 'queue_play_next',
        disabled: !allVideos,
        onSelect: () => onCreateQueue(ids),
      },
      { label: `Add ${ids.length} to Schedule`, icon: 'playlist_add', onSelect: () => onAddToSchedule(ids) },
      {
        label: `Remove ${ids.length} from Gallery`,
        icon: 'delete',
        danger: true,
        onSelect: () => onDelete(ids),
      },
    ]
  }

  const tabButton = (value: Tab, label: string, icon: string) => {
    const count = items.filter(item => item.kind === value).length
    return (
      <button
        type="button"
        onClick={e => {
          e.stopPropagation()
          setTab(value)
        }}
        className={`flex items-center gap-1.5 rounded px-3 py-1 font-mono text-mono-ui uppercase tracking-widest transition-colors ${
          tab === value
            ? 'bg-primary/10 text-primary'
            : 'text-on-surface-variant hover:text-on-surface'
        }`}
      >
        <Icon name={icon} size={14} />
        {label}
        <span className="text-slate-muted">{count}</span>
      </button>
    )
  }

  return (
    <section className="flex shrink-0 flex-col border-t border-outline-variant bg-surface-container-lowest">
      <div className="flex items-center justify-between px-gutter py-2">
        <button
          type="button"
          onClick={toggle}
          title={collapsed ? 'Show gallery' : 'Hide gallery'}
          className="flex items-center gap-2 text-on-surface transition-colors hover:text-primary"
        >
          <Icon name="perm_media" size={18} />
          <h2 className="font-mono text-status-label uppercase">Gallery</h2>
          <Icon name={collapsed ? 'expand_less' : 'expand_more'} size={20} />
        </button>

        <div className="flex items-center gap-3">
          {!collapsed && (
            <div className="flex items-center gap-1">
              {tabButton('image', 'Images', 'image')}
              {tabButton('video', 'Video', 'movie')}
            </div>
          )}
          <button
            type="button"
            onClick={addFromDisk}
            disabled={picking}
            className="flex items-center gap-2 rounded border border-outline-variant bg-surface-container px-3 py-1.5 font-mono text-status-label uppercase text-on-surface transition-colors hover:border-primary hover:text-primary"
          >
            <Icon name="add" size={18} />
            Add from Disk
          </button>
        </div>
      </div>

      {!collapsed && (
        <div
          onDragOver={e => {
            e.preventDefault()
            setDragOver(true)
          }}
          onDragLeave={() => setDragOver(false)}
          onDrop={drop}
          className={`panel-scroll h-[38vh] overflow-y-auto border-t px-gutter py-4 transition-colors ${
            dragOver ? 'border-primary bg-primary/5' : 'border-transparent'
          }`}
        >
          {visible.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center gap-2 text-center">
              <Icon name="upload_file" size={28} className="text-slate-muted" />
              <p className="font-mono text-mono-ui italic text-slate-muted">
                {dragOver
                  ? 'Drop to link these files'
                  : `Drag ${tab === 'image' ? 'images' : 'video'} here, or use Add from Disk.`}
              </p>
              <p className="font-mono text-[10px] uppercase tracking-widest text-slate-muted">
                Files are linked, never copied
              </p>
            </div>
          ) : (
            <ul className="grid grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-3">
              {visible.map(item => {
                const isSelected = selected.has(item.id)
                const isPreview = item.id === previewId
                return (
                  <li key={item.id}>
                    <div
                      role="button"
                      tabIndex={0}
                      onClick={e => click(item, e)}
                      onDoubleClick={() => onGoLive(item.id)}
                      onContextMenu={e => openMenu(item, e)}
                      title={`${item.title}\n${item.sourcePath}\nClick to preview · double-click to go live · right-click for options`}
                      className={`flex cursor-pointer select-none flex-col overflow-hidden rounded-lg border-2 transition-colors ${
                        isSelected
                          ? 'border-primary bg-primary/5'
                          : isPreview
                            ? 'border-primary/40 bg-surface-container'
                            : 'border-outline-variant bg-surface-container hover:border-primary/30'
                      }`}
                    >
                      <MediaThumb item={item} className="h-24 w-full" />
                      <span className="truncate px-2 py-1.5 text-mono-ui font-mono text-on-surface">
                        {item.title}
                      </span>
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      )}

      {menu && (
        <ContextMenu
          x={menu.x}
          y={menu.y}
          actions={menuActions(menu.ids)}
          onClose={() => setMenu(null)}
        />
      )}
      {browsing && (
        <BrowseDialog
          onAdd={onAddPaths}
          onClose={() => setBrowsing(false)}
          onError={onError}
        />
      )}
    </section>
  )
}
