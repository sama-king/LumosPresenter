import Icon from '../../components/Icon'

/** Shared form controls for the stage config panel, styled per docs/Design.md tokens. */

/** Group heading: prominent display-font title so it reads at a different level
 *  than the small mono field labels below it (Design.md type hierarchy). */
export function SectionLabel({ icon, children }: { icon: string; children: React.ReactNode }) {
  return (
    <h3 className="flex items-center gap-2 font-display text-body-md font-semibold uppercase text-on-surface">
      <Icon name={icon} size={18} className="text-primary" />
      {children}
    </h3>
  )
}

interface CollapsibleSectionProps {
  icon: string
  title: string
  open: boolean
  onOpen: () => void
  /** Controls kept in the header (e.g. an on/off switch), clickable while collapsed. */
  actions?: React.ReactNode
  children: React.ReactNode
}

/** Accordion card: the header expands it; the parent keeps only one open at a time. */
export function CollapsibleSection({ icon, title, open, onOpen, actions, children }: CollapsibleSectionProps) {
  return (
    <section className="rounded-xl border border-surface-variant/50 bg-surface-container">
      <div className="flex items-center gap-3 p-4">
        <button
          type="button"
          aria-expanded={open}
          onClick={onOpen}
          className="flex min-w-0 flex-1 items-center justify-between gap-2 text-left"
        >
          <SectionLabel icon={icon}>{title}</SectionLabel>
          <Icon
            name="expand_more"
            size={20}
            className={`text-slate-muted transition-transform ${open ? 'rotate-180' : ''}`}
          />
        </button>
        {actions}
      </div>
      {open && <div className="space-y-4 px-4 pb-4">{children}</div>}
    </section>
  )
}

export function FieldLabel({ children }: { children: React.ReactNode }) {
  return (
    <span className="block font-mono text-mono-ui tracking-wider text-slate-muted">
      {children}
    </span>
  )
}

/** Explanatory copy: italic interface font so it reads as an aside,
 *  not as another label or control. */
export function HelpText({ children }: { children: React.ReactNode }) {
  return <p className="text-[12px] italic leading-relaxed text-slate-muted">{children}</p>
}

/** How a labeled control lays out: label stacked above (default) or on the left, control on the right. */
export type FieldLayout = 'stacked' | 'row'

/**
 * Wraps a label + control. 'stacked' keeps the current label-above look; 'row' puts the label on
 * the left and the control on the right for a denser, more horizontal panel.
 */
function FieldShell({
  label,
  layout,
  children,
}: {
  label: string
  layout: FieldLayout
  children: React.ReactNode
}) {
  if (layout === 'row') {
    return (
      <label className="flex items-center justify-between gap-3">
        <FieldLabel>{label}</FieldLabel>
        {/* Fixed control column so every row's control shares the same edges. */}
        <span className="flex w-48 shrink-0 items-center">{children}</span>
      </label>
    )
  }
  return (
    <label className="block space-y-2">
      <FieldLabel>{label}</FieldLabel>
      {children}
    </label>
  )
}

interface SelectFieldProps {
  label: string
  value: string
  options: { value: string; label: string }[]
  onChange: (value: string) => void
  layout?: FieldLayout
}

export function SelectField({ label, value, options, onChange, layout = 'stacked' }: SelectFieldProps) {
  return (
    <FieldShell label={label} layout={layout}>
      <select
        value={value}
        onChange={e => onChange(e.target.value)}
        className="w-full rounded-lg border border-surface-variant bg-surface-container-lowest px-3 py-2.5 text-body-md text-on-surface focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
      >
        {options.map(option => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldShell>
  )
}

interface SliderFieldProps {
  label: string
  value: number
  min: number
  max: number
  unit?: string
  onChange: (value: number) => void
  layout?: FieldLayout
}

export function SliderField({
  label,
  value,
  min,
  max,
  unit = 'px',
  onChange,
  layout = 'stacked',
}: SliderFieldProps) {
  const range = (
    <input
      type="range"
      min={min}
      max={max}
      value={value}
      onChange={e => onChange(Number(e.target.value))}
      className="w-full accent-primary"
    />
  )
  const readout = (
    <span className="font-mono text-mono-ui text-on-surface">
      {value}
      {unit}
    </span>
  )
  if (layout === 'row') {
    // Label · slider · value on one line; the control column matches FieldShell's
    // row width so sliders align with selects and color swatches.
    return (
      <label className="flex items-center justify-between gap-3">
        <FieldLabel>{label}</FieldLabel>
        <span className="flex w-48 shrink-0 items-center gap-2">
          <span className="min-w-0 flex-1">{range}</span>
          <span className="w-12 shrink-0 text-right">{readout}</span>
        </span>
      </label>
    )
  }
  return (
    <label className="block space-y-2">
      <div className="flex items-center justify-between">
        <FieldLabel>{label}</FieldLabel>
        {readout}
      </div>
      {range}
    </label>
  )
}

interface ColorFieldProps {
  label: string
  value: string
  onChange: (value: string) => void
  layout?: FieldLayout
}

export function ColorField({ label, value, onChange, layout = 'stacked' }: ColorFieldProps) {
  const swatch = (
    <div className="flex w-full items-center gap-3 rounded-lg border border-surface-variant bg-surface-container-lowest px-3 py-2">
      <input
        type="color"
        value={value}
        onChange={e => onChange(e.target.value)}
        className="h-6 w-8 cursor-pointer rounded border-none bg-transparent"
      />
      <span className="font-mono text-mono-ui uppercase text-on-surface">{value}</span>
    </div>
  )
  return (
    <FieldShell label={label} layout={layout}>
      {swatch}
    </FieldShell>
  )
}

interface SegmentedProps<T extends string> {
  label: string
  value: T
  options: { value: T; icon: string; title: string }[]
  onChange: (value: T) => void
}

export function SegmentedIconToggle<T extends string>({
  label,
  value,
  options,
  onChange,
}: SegmentedProps<T>) {
  return (
    <div className="space-y-2">
      <FieldLabel>{label}</FieldLabel>
      <div className="flex rounded-lg border border-surface-variant bg-surface-container-lowest p-1">
        {options.map(option => (
          <button
            key={option.value}
            type="button"
            title={option.title}
            onClick={() => onChange(option.value)}
            className={
              option.value === value
                ? 'flex flex-1 items-center justify-center rounded-md bg-primary py-1.5 text-on-primary'
                : 'flex flex-1 items-center justify-center rounded-md py-1.5 text-on-surface-variant transition-colors hover:text-on-surface'
            }
          >
            <Icon name={option.icon} size={18} />
          </button>
        ))}
      </div>
    </div>
  )
}
