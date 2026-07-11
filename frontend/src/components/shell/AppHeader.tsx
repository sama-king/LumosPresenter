import { NavLink } from 'react-router-dom'
import Icon from '../Icon'
import { useTheme } from '../../lib/theme'

/**
 * App-wide header, ported from stitch_designs/stage_configuration (the
 * authoritative chrome for all pages). Only Scripture routes today; the
 * other modules render as disabled placeholders until they exist.
 */

const NAV_ITEMS = [
  { label: 'Stage', icon: 'layers', to: '/stage', enabled: true },
  { label: 'Scripture', icon: 'auto_stories', to: '/scripture', enabled: true },
  { label: 'Songs', icon: 'music_note', to: '/songs', enabled: true },
  { label: 'Media', icon: 'folder_special', to: '/media', enabled: false },
]

const isMac = navigator.platform.toUpperCase().includes('MAC')

export default function AppHeader({ searchRef }: { searchRef?: React.Ref<HTMLInputElement> }) {
  const { theme, toggleTheme } = useTheme()
  return (
    <header className="sticky top-0 z-50 flex h-20 w-full shrink-0 items-center justify-between border-b border-surface-variant bg-app-bg px-gutter">
      <div className="flex items-center gap-8">
        <h1 className="wordmark-glow font-display text-headline-md leading-none tracking-tight text-primary">
          LumosPresenter
        </h1>
        <nav className="flex items-center gap-2">
          {NAV_ITEMS.map(item =>
            item.enabled ? (
              <NavLink
                key={item.label}
                to={item.to}
                className={({ isActive }) =>
                  isActive
                    ? 'flex items-center gap-2 rounded-lg border border-primary/20 bg-primary/10 px-4 py-2 text-primary'
                    : 'flex items-center gap-2 rounded-lg px-4 py-2 text-on-surface-variant transition-all hover:bg-surface-container-highest hover:text-on-surface'
                }
              >
                <Icon name={item.icon} />
                <span className="font-mono text-status-label">{item.label}</span>
              </NavLink>
            ) : (
              <span
                key={item.label}
                title="Coming soon"
                className="flex cursor-not-allowed items-center gap-2 rounded-lg px-4 py-2 text-on-surface-variant opacity-40"
              >
                <Icon name={item.icon} />
                <span className="font-mono text-status-label">{item.label}</span>
              </span>
            ),
          )}
        </nav>
      </div>
      <div className="flex items-center gap-6">
        <div className="group relative">
          <Icon
            name="search"
            className="absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant"
          />
          <input
            ref={searchRef}
            type="text"
            placeholder={`Quick search (${isMac ? '⌘' : 'Ctrl+'}K)`}
            className="w-64 rounded-full border border-surface-variant bg-surface-container-lowest py-2 pl-10 pr-4 font-mono text-status-label transition-all focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </div>
        <div className="flex items-center gap-4">
          <button
            type="button"
            title="Notifications"
            className="relative p-2 text-on-surface-variant transition-colors hover:text-primary"
          >
            <Icon name="notifications" size={24} />
            <span className="absolute right-2 top-2 h-2 w-2 rounded-full border-2 border-app-bg bg-rose-error" />
          </button>
          <button
            type="button"
            title={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
            onClick={toggleTheme}
            className="p-2 text-on-surface-variant transition-colors hover:text-primary"
          >
            <Icon name={theme === 'dark' ? 'light_mode' : 'dark_mode'} size={24} />
          </button>
          <button
            type="button"
            title="Settings"
            className="p-2 text-on-surface-variant transition-colors hover:text-primary"
          >
            <Icon name="settings" size={24} />
          </button>
          <div className="flex h-8 w-8 cursor-pointer items-center justify-center rounded-full bg-primary-container text-xs font-bold text-on-primary-container ring-2 ring-surface-variant">
            OP
          </div>
        </div>
      </div>
    </header>
  )
}
