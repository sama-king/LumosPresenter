import { useCallback, useEffect, useMemo, useState } from 'react'
import { usePersistentState } from '../../lib/persistentState'
import { api } from '../../lib/api'
import { useServerEvent } from '../../lib/events'
import type {
  ContentType,
  DisplayConfig,
  DisplayDto,
  FontDto,
  LiveEvent,
  LiveItemDto,
  MediaAssetDto,
  ViewportRect,
} from '../../lib/types'
import ActionBar from './ActionBar'
import AddDisplayDialog from './AddDisplayDialog'
import ConfigPanel from './ConfigPanel'
import DisplayTabs from './DisplayTabs'
import DisplayUrlChip from './DisplayUrlChip'
import PreviewPane from './PreviewPane'
import { clampViewport, configsEqual, type SectionId } from './stageConfig'

export default function StagePage() {
  // Server lists (re-fetched on mount). selectedId/draft/activeSection are durable across nav
  // so an in-progress edit survives leaving the tab; liveItem is driven by SSE.
  const [displays, setDisplays] = useState<DisplayDto[]>([])
  const [defaultConfig, setDefaultConfig] = useState<DisplayConfig | null>(null)
  const [fonts, setFonts] = useState<FontDto[]>([])
  const [media, setMedia] = useState<MediaAssetDto[]>([])
  const [selectedId, setSelectedId] = usePersistentState<number | null>('stage.selectedId', null)
  const [draft, setDraft] = usePersistentState<DisplayConfig | null>('stage.draft', null)
  const [activeSection, setActiveSection] = usePersistentState<SectionId>('stage.activeSection', 'scripture')
  const [liveItem, setLiveItem] = useState<LiveItemDto | null>(null)
  const [adding, setAdding] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const showError = useCallback((message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }, [])

  useEffect(() => {
    void Promise.all([api.getDisplays(), api.getFonts(), api.getMedia(), api.getLive()])
      .then(([displaysData, fontsData, mediaData, live]) => {
        setDisplays(displaysData.displays)
        setDefaultConfig(displaysData.defaultConfig)
        setFonts(fontsData.fonts)
        setMedia(mediaData.assets)
        setLiveItem(live.item)
        // Seed the selection/draft only on a cold start; a persisted selection (from
        // navigating back) is kept, so an in-progress edit isn't discarded. If the
        // persisted selection no longer exists on the server, fall back to the first display.
        setSelectedId(prevId => {
          const stillExists = prevId != null && displaysData.displays.some(d => d.id === prevId)
          const target = stillExists
            ? displaysData.displays.find(d => d.id === prevId)!
            : displaysData.displays[0]
          if (!target) return prevId
          if (!stillExists) {
            setDraft(structuredClone(target.config))
          } else {
            // Keep any unsaved draft; only initialize it if we somehow have none.
            setDraft(prevDraft => prevDraft ?? structuredClone(target.config))
          }
          return target.id
        })
      })
      .catch((err: Error) => showError(err.message))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showError])

  const refreshMedia = useCallback(
    () => api.getMedia().then(data => setMedia(data.assets)),
    [],
  )

  // The preview styles whatever is actually live, like the real displays do.
  useServerEvent<LiveEvent>('live', data => {
    setLiveItem('cleared' in data ? null : data)
  })

  const selected = displays.find(d => d.id === selectedId) ?? null
  const locked = selected?.followsDisplayId != null
  const dirty = useMemo(
    () => selected !== null && draft !== null && !configsEqual(draft, selected.config),
    [selected, draft],
  )

  /** Replaces a display in local state and refreshes its followers' effective configs. */
  const mergeDisplay = useCallback((updated: DisplayDto) => {
    setDisplays(prev =>
      prev.map(d =>
        d.id === updated.id
          ? updated
          : d.followsDisplayId === updated.id
            ? { ...d, config: updated.config }
            : d,
      ),
    )
  }, [])

  const selectDisplay = useCallback(
    (id: number) => {
      if (id === selectedId) return
      if (dirty && !window.confirm('Discard unsaved changes?')) return
      const display = displays.find(d => d.id === id)
      if (!display) return
      setSelectedId(id)
      setDraft(structuredClone(display.config))
    },
    [selectedId, dirty, displays],
  )

  const patchDraft = useCallback((patch: Partial<DisplayConfig>) => {
    setDraft(prev => (prev === null ? prev : { ...prev, ...patch }))
  }, [])

  // The source tab has no content of its own to preview; show scripture there.
  const activeType: ContentType = activeSection === 'source' ? 'scripture' : activeSection

  // The preview's draggable frame edits the ACTIVE content type's viewport
  // (media's is top-level; the text types nest it under .text).
  const changeViewport = useCallback(
    (rect: ViewportRect) => {
      const viewport = clampViewport(rect)
      setDraft(prev => {
        if (prev === null) return prev
        if (activeType === 'media') return { ...prev, media: { ...prev.media, viewport } }
        const section = prev[activeType]
        return { ...prev, [activeType]: { ...section, text: { ...section.text, viewport } } }
      })
    },
    [activeType],
  )

  const save = useCallback(() => {
    if (selectedId === null || draft === null) return
    setSaving(true)
    void api
      .saveDisplayConfig(selectedId, draft)
      .then(updated => {
        mergeDisplay(updated)
        setDraft(structuredClone(updated.config))
      })
      .catch((err: Error) => showError(err.message))
      .finally(() => setSaving(false))
  }, [selectedId, draft, mergeDisplay, showError])

  const reset = useCallback(() => {
    if (defaultConfig) setDraft(structuredClone(defaultConfig))
  }, [defaultConfig])

  const changeSource = useCallback(
    (followsDisplayId: number | null) => {
      if (selectedId === null) return
      void api
        .setDisplaySource(selectedId, followsDisplayId)
        .then(updated => {
          mergeDisplay(updated)
          setDraft(structuredClone(updated.config))
        })
        .catch((err: Error) => showError(err.message))
    },
    [selectedId, mergeDisplay, showError],
  )

  const addDisplay = useCallback(
    (name: string, useSettingsOfDisplayId: number | null) => {
      setAdding(false)
      void api
        .createDisplay({
          name: name === '' ? undefined : name,
          useSettingsOfDisplayId: useSettingsOfDisplayId ?? undefined,
        })
        .then(created => {
          setDisplays(prev => [...prev, created])
          setSelectedId(created.id)
          setDraft(structuredClone(created.config))
        })
        .catch((err: Error) => showError(err.message))
    },
    [showError],
  )

  const deleteDisplay = useCallback(() => {
    if (selected === null) return
    if (!window.confirm(`Delete ${selected.name}? Its open display windows will stop updating.`)) {
      return
    }
    void api
      .deleteDisplay(selected.id)
      .then(() => {
        setDisplays(prev => {
          const remaining = prev
            .filter(d => d.id !== selected.id)
            // Followers of the deleted display were detached server-side with its
            // config snapshotted; mirror that locally.
            .map(d =>
              d.followsDisplayId === selected.id ? { ...d, followsDisplayId: null } : d,
            )
          const next = remaining[0]
          setSelectedId(next ? next.id : null)
          setDraft(next ? structuredClone(next.config) : null)
          return remaining
        })
      })
      .catch((err: Error) => showError(err.message))
  }, [selected, showError])

  if (selected === null || draft === null) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <p className="font-mono text-status-label uppercase text-slate-muted">
          {error === '' ? 'Loading stage configuration…' : error}
        </p>
      </div>
    )
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex shrink-0 items-end justify-between border-b border-surface-variant px-gutter pt-5">
        <div className="space-y-3">
          <div className="flex items-center gap-4">
            <h2 className="font-display text-headline-lg text-on-surface">Stage Configuration</h2>
            {dirty && !locked && (
              <span className="rounded border border-amber-warning/40 px-2 py-0.5 font-mono text-mono-ui uppercase tracking-widest text-amber-warning">
                Editing
              </span>
            )}
          </div>
          <DisplayTabs
            displays={displays}
            selectedId={selectedId}
            dirty={dirty}
            onSelect={selectDisplay}
            onAdd={() => setAdding(true)}
          />
        </div>
        <div className="pb-3">
          <DisplayUrlChip displayId={selected.id} />
        </div>
      </div>

      <div className="grid min-h-0 flex-1 grid-cols-12">
        <div className="col-span-4 flex min-h-0 flex-col 2xl:col-span-3">
          <ConfigPanel
            display={selected}
            displays={displays}
            draft={draft}
            fonts={fonts}
            media={media}
            active={activeSection}
            onSelect={setActiveSection}
            onChange={patchDraft}
            onChangeSource={changeSource}
            onMediaChange={refreshMedia}
            onError={showError}
          />
        </div>
        <div className="col-span-8 flex min-h-0 flex-col 2xl:col-span-9">
          <PreviewPane
            draft={draft}
            fonts={fonts}
            liveItem={liveItem}
            activeType={activeType}
            editable={!locked}
            onViewportChange={changeViewport}
          />
        </div>
      </div>

      <ActionBar
        dirty={dirty}
        saving={saving}
        locked={locked}
        canDelete={displays.length > 1}
        error={error}
        onReset={reset}
        onDelete={deleteDisplay}
        onSave={save}
      />

      {adding && (
        <AddDisplayDialog
          displays={displays}
          onCreate={addDisplay}
          onClose={() => setAdding(false)}
        />
      )}
    </div>
  )
}
