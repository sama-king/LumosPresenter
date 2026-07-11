import { useState } from 'react'
import Icon from '../../components/Icon'
import type { Translation } from '../../lib/types'

interface ResourcesBandProps {
  translations: Translation[]
  currentTranslation: string
  onSwitchTranslation: (code: string) => void
}

const COLLAPSE_KEY = 'lumos:resources-collapsed'

export default function ResourcesBand({
  translations,
  currentTranslation,
  onSwitchTranslation,
}: ResourcesBandProps) {
  const [collapsed, setCollapsed] = useState(
    () => localStorage.getItem(COLLAPSE_KEY) === '1',
  )

  const toggle = () => {
    setCollapsed(prev => {
      const next = !prev
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
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
            <h3 className="mb-2 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
              Offline Bibles
            </h3>
            <ul className="panel-scroll flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto pr-2">
              {translations.map(t => {
                const isActive = t.id === currentTranslation
                return (
                  <li key={t.id}>
                    <button
                      type="button"
                      onClick={() => onSwitchTranslation(t.id)}
                      title={isActive ? 'Active translation' : `Switch to ${t.name}`}
                      className={`flex w-full items-center justify-between rounded border px-3 py-1.5 text-left transition-colors ${
                        isActive
                          ? 'border-primary/40 bg-primary/5'
                          : 'border-outline-variant bg-surface-container-low hover:border-primary/30'
                      }`}
                    >
                      <span className="truncate text-body-md font-semibold text-on-surface">
                        {t.id}{' '}
                        <span className="font-normal text-on-surface-variant">— {t.name}</span>
                      </span>
                      <span className="ml-3 shrink-0 font-mono text-[10px] uppercase tracking-widest text-secondary">
                        Ready
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
          </div>

          <div className="flex min-w-0 flex-1 flex-col opacity-40" aria-disabled>
            <div className="mb-2 flex items-center gap-2">
              <h3 className="font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
                Online Sources
              </h3>
              <span className="rounded-full border border-outline-variant bg-surface-container px-2 py-0.5 font-mono text-[9px] uppercase tracking-widest text-amber-warning">
                Coming soon
              </span>
            </div>
            <div className="flex items-center justify-between rounded border border-outline-variant bg-surface-container-low px-3 py-2">
              <span className="text-body-md text-on-surface">
                Enable Online Sources{' '}
                <span className="text-on-surface-variant">(Requires Paid Plan)</span>
              </span>
              <span className="relative h-5 w-9 cursor-not-allowed rounded-full border border-outline-variant bg-surface-container-highest">
                <span className="absolute left-0.5 top-0.5 h-3.5 w-3.5 rounded-full bg-slate-muted" />
              </span>
            </div>
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
