import Icon from '../../components/Icon'

/** One entry in the config panel's icon rail. Future content types (songs, pictures,
 *  lower thirds) become new entries — the rail scales without layout changes. */
export interface RailSection {
  id: string
  icon: string
  label: string
  /** Shows a small live-status dot on the icon (e.g. reference line enabled). */
  dot?: boolean
}

interface ConfigRailProps {
  sections: RailSection[]
  /** Meta entries pinned to the bottom of the rail (e.g. settings source). */
  footer?: RailSection[]
  active: string
  onSelect: (id: string) => void
}

/**
 * Vertical icon rail for the stage config panel (inspector-style navigation).
 * Hovering an icon peeks the full section name in a tooltip chip.
 */
export default function ConfigRail({ sections, footer = [], active, onSelect }: ConfigRailProps) {
  const item = (section: RailSection) => {
    const isActive = section.id === active
    return (
      <button
        key={section.id}
        type="button"
        aria-label={section.label}
        aria-current={isActive ? 'true' : undefined}
        onClick={() => onSelect(section.id)}
        className={`group relative flex h-10 w-10 items-center justify-center rounded-lg transition-colors ${
          isActive
            ? 'border border-primary/30 bg-primary/15 text-primary'
            : 'text-on-surface-variant hover:bg-surface-container-highest hover:text-on-surface'
        }`}
      >
        <Icon name={section.icon} size={20} />
        {section.dot && (
          <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full bg-emerald-live" />
        )}
        {/* Hover peek: full section name in a chip beside the rail. */}
        <span className="pointer-events-none absolute left-full top-1/2 z-20 ml-3 hidden -translate-y-1/2 whitespace-nowrap rounded border border-surface-variant bg-surface-container-highest px-2.5 py-1.5 font-mono text-mono-ui uppercase tracking-wider text-on-surface shadow-xl group-hover:block">
          {section.label}
        </span>
      </button>
    )
  }

  return (
    <nav
      aria-label="Configuration sections"
      className="flex w-14 shrink-0 flex-col items-center justify-between border-r border-surface-variant bg-surface-container-lowest py-4"
    >
      <div className="flex flex-col items-center gap-2">{sections.map(item)}</div>
      <div className="flex flex-col items-center gap-2">{footer.map(item)}</div>
    </nav>
  )
}
