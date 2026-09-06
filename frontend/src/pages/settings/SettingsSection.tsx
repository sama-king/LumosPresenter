/**
 * One labelled block of the settings page. The design groups these under tabs; here the
 * heading and its rule carry the same separation on a single scroll, so an operator can
 * find a setting by scanning rather than by remembering which tab it was filed under.
 */
export default function SettingsSection({
  title,
  caption,
  description,
  children,
}: {
  title: string
  /** Short right-aligned tag naming the subsystem the section configures. */
  caption: string
  description: string
  children: React.ReactNode
}) {
  return (
    <section className="flex flex-col gap-4">
      <div className="border-b border-outline-variant pb-3">
        <div className="flex items-baseline justify-between gap-4">
          <h2 className="font-display text-headline-md text-on-surface">{title}</h2>
          <span className="shrink-0 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
            {caption}
          </span>
        </div>
        <p className="mt-1 max-w-3xl text-body-md text-on-surface-variant">{description}</p>
      </div>
      {children}
    </section>
  )
}
