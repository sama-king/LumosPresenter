import { useState } from 'react'
import { Link } from 'react-router-dom'
import Icon from '../../components/Icon'
import { isOfflineTranslation, type ApiBibleKeyStatus, type Translation } from '../../lib/types'

interface ResourcesBandProps {
  translations: Translation[]
  currentTranslation: string
  onSwitchTranslation: (code: string) => void
  /** Online sources off hides the api.bible translations from the pickers. */
  onlineEnabled: boolean
  onToggleOnline: (enabled: boolean) => void
  /** The toggle's own position, which stays put while there is no key to honour it. */
  onlineWanted: boolean
  /**
   * Null until the first fetch lands; `configured` false means no key is stored. The key
   * is entered on the settings page — this band only reports whether one exists, since a
   * credential prompt does not belong in the middle of a live service console.
   */
  apiKey: ApiBibleKeyStatus | null
}

const COLLAPSE_KEY = 'lumos:resources-collapsed'

export default function ResourcesBand({
  translations,
  currentTranslation,
  onSwitchTranslation,
  onlineEnabled,
  onToggleOnline,
  onlineWanted,
  apiKey,
}: ResourcesBandProps) {
  const [collapsed, setCollapsed] = useState(
    () => localStorage.getItem(COLLAPSE_KEY) === '1',
  )
  const hasKey = apiKey?.configured === true
  // Until the status arrives we do not know which of the two states to describe.
  const keyKnown = apiKey !== null

  const toggle = () => {
    setCollapsed(prev => {
      const next = !prev
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
  }

  // Where a translation's text lives is what splits the two columns: bundled/imported
  // text is on this machine, api.bible text is fetched and cached.
  const offline = translations.filter(isOfflineTranslation)
  const online = translations.filter(t => !isOfflineTranslation(t))

  /** One selectable translation. Availability is shown by the row's status, not a badge. */
  const row = (t: Translation, disabled: boolean) => {
    const isActive = t.id === currentTranslation
    return (
      <li key={t.id}>
        <button
          type="button"
          disabled={disabled}
          onClick={() => onSwitchTranslation(t.id)}
          title={
            disabled
              ? hasKey
                ? 'Enable online sources to use this translation'
                : 'Add an api.bible key to use this translation'
              : isActive
                ? 'Active translation'
                : `Switch to ${t.name}`
          }
          className={`flex w-full items-center justify-between rounded border px-3 py-1.5 text-left transition-colors ${
            isActive
              ? 'border-primary/40 bg-primary/5'
              : 'border-outline-variant bg-surface-container-low hover:border-primary/30'
          } ${disabled ? 'cursor-not-allowed opacity-50' : ''}`}
        >
          <span className="truncate text-body-md font-semibold text-on-surface">
            {t.id} <span className="font-normal text-on-surface-variant">— {t.name}</span>
          </span>
          <span
            className={`ml-3 shrink-0 font-mono text-[10px] uppercase tracking-widest ${
              disabled ? 'text-slate-muted' : 'text-secondary'
            }`}
          >
            {!disabled ? 'Ready' : hasKey ? 'Off' : 'No key'}
          </span>
        </button>
      </li>
    )
  }

  return (
    <section className="flex shrink-0 flex-col border-t border-outline-variant bg-surface-container-lowest">
      <button
        type="button"
        onClick={toggle}
        title={collapsed ? 'Show Bibles' : 'Hide Bibles'}
        className="flex items-center justify-between px-gutter py-2 text-on-surface transition-colors hover:bg-surface-container-low"
      >
        <span className="flex items-center gap-2">
          <Icon name="database" size={18} />
          <h2 className="font-mono text-status-label uppercase">Bibles</h2>
          {collapsed && (
            <span className="ml-2 font-mono text-mono-ui text-on-surface-variant">
              {currentTranslation}
            </span>
          )}
        </span>
        <Icon name={collapsed ? 'expand_less' : 'expand_more'} size={20} />
      </button>

      {!collapsed && (
        <div className="flex h-44 gap-8 px-gutter pb-4">
          <div className="flex min-w-0 flex-1 flex-col">
            <h3 className="mb-2 flex items-center gap-1.5 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
              <Icon name="offline_bolt" size={14} />
              Offline Bibles
            </h3>
            <ul className="panel-scroll flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto pr-2">
              {offline.map(t => row(t, false))}
            </ul>
          </div>

          <div className="flex min-w-0 flex-1 flex-col">
            <div className="mb-2 flex items-center gap-2">
              <h3 className="flex items-center gap-1.5 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
                <Icon name="cloud" size={14} />
                Online Sources
              </h3>
            </div>

            <div className="mb-2 rounded border border-outline-variant bg-surface-container-low px-3 py-2">
              <div className="flex items-center justify-between">
                <span className={`text-body-md ${hasKey ? 'text-on-surface' : 'text-slate-muted'}`}>
                  Enable Online Sources
                </span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={onlineEnabled}
                  aria-label="Enable online sources"
                  disabled={!hasKey}
                  onClick={() => onToggleOnline(!onlineWanted)}
                  title={
                    !hasKey
                      ? 'Add an api.bible key to enable online sources'
                      : onlineEnabled
                        ? 'Turn online sources off'
                        : 'Turn online sources on'
                  }
                  className={`relative h-5 w-9 shrink-0 rounded-full border transition-colors ${
                    onlineEnabled
                      ? 'border-primary bg-primary'
                      : 'border-outline-variant bg-surface-container-highest'
                  } ${hasKey ? '' : 'cursor-not-allowed opacity-50'}`}
                >
                  <span
                    className={`absolute top-0.5 h-3.5 w-3.5 rounded-full transition-all ${
                      onlineEnabled ? 'left-[18px] bg-navy-deep' : 'left-0.5 bg-slate-muted'
                    }`}
                  />
                </button>
              </div>

              {/* The key is entered on the settings page; this band only says whether
                  one is there, and points at where to fix it when it is not. */}
              {keyKnown && !hasKey && (
                <Link
                  to="/settings"
                  className="mt-2 flex items-center gap-1.5 font-mono text-mono-ui text-primary transition-colors hover:text-primary-container"
                >
                  <Icon name="key" size={14} />
                  Add an api.bible key in Settings
                </Link>
              )}
            </div>

            <ul className="panel-scroll flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto pr-2">
              {online.length === 0 ? (
                <li className="font-mono text-mono-ui italic text-slate-muted">
                  No online sources configured.
                </li>
              ) : (
                online.map(t => row(t, !onlineEnabled))
              )}
            </ul>
          </div>

          <div className="flex shrink-0 flex-col justify-center gap-2 opacity-40" aria-disabled>
            <button
              type="button"
              disabled
              title="Coming soon"
              className="flex cursor-not-allowed items-center gap-2 rounded border border-outline-variant bg-surface-container px-4 py-2 font-mono text-status-label uppercase"
            >
              <Icon name="upload" size={18} />
              Import
            </button>
            <button
              type="button"
              disabled
              title="Coming soon"
              className="flex cursor-not-allowed items-center gap-2 rounded border border-outline-variant bg-surface-container px-4 py-2 font-mono text-status-label uppercase"
            >
              <Icon name="download" size={18} />
              Download
            </button>
          </div>
        </div>
      )}
    </section>
  )
}
